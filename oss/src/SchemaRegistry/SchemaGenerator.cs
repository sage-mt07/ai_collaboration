using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using KsqlDsl.Ksql;
using KsqlDsl.Modeling;

namespace KsqlDsl.SchemaRegistry;

public static class SchemaGenerator
{
    public static string GenerateSchema<T>()
    {
        return GenerateSchema(typeof(T));
    }

    public static string GenerateSchema(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        var schema = new AvroSchema
        {
            Type = "record",
            Name = type.Name,
            Namespace = type.Namespace ?? "KsqlDsl.Generated",
            Fields = GenerateFields(type)
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(schema, options);
    }

    public static string GenerateSchema(Type type, string namespaceName)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));
        if (string.IsNullOrEmpty(namespaceName))
            throw new ArgumentException("Namespace cannot be null or empty", nameof(namespaceName));

        var schema = new AvroSchema
        {
            Type = "record",
            Name = type.Name,
            Namespace = namespaceName,
            Fields = GenerateFields(type)
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(schema, options);
    }

    public static string GenerateSchema(Type type, SchemaGenerationOptions options)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var schema = new AvroSchema
        {
            Type = "record",
            Name = options.CustomName ?? type.Name,
            Namespace = options.Namespace ?? type.Namespace ?? "KsqlDsl.Generated",
            Doc = options.Documentation,
            Fields = GenerateFields(type)
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = options.PrettyFormat,
            PropertyNamingPolicy = options.UseKebabCase ? JsonNamingPolicy.KebabCaseLower : JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(schema, jsonOptions);
    }

    public static string GenerateKeySchema<T>()
    {
        return GenerateKeySchema(typeof(T));
    }

    public static string GenerateKeySchema(Type keyType)
    {
        if (keyType == null)
            throw new ArgumentNullException(nameof(keyType));

        // Handle primitive types directly
        if (IsPrimitiveType(keyType))
        {
            return GeneratePrimitiveKeySchema(keyType);
        }

        // Handle nullable primitive types
        var underlyingType = Nullable.GetUnderlyingType(keyType);
        if (underlyingType != null && IsPrimitiveType(underlyingType))
        {
            return GenerateNullablePrimitiveKeySchema(underlyingType);
        }

        // Handle complex types as records
        var schema = new AvroSchema
        {
            Type = "record",
            Name = $"{keyType.Name}Key",
            Namespace = keyType.Namespace ?? "KsqlDsl.Generated",
            Fields = GenerateFields(keyType)
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(schema, options);
    }

    public static (string keySchema, string valueSchema) GenerateTopicSchemas<TKey, TValue>()
    {
        var keySchema = GenerateKeySchema<TKey>();
        var valueSchema = GenerateSchema<TValue>();
        return (keySchema, valueSchema);
    }

    public static (string keySchema, string valueSchema) GenerateTopicSchemas<TKey, TValue>(string topicName)
    {
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));

        var keySchema = GenerateKeySchema<TKey>();

        // For value schema, use custom naming based on topic name
        var valueType = typeof(TValue);
        var valueOptions = new SchemaGenerationOptions
        {
            CustomName = $"{ToPascalCase(topicName)}Value",
            Namespace = valueType.Namespace ?? "KsqlDsl.Generated"
        };
        var valueSchema = GenerateSchema(valueType, valueOptions);

        return (keySchema, valueSchema);
    }

    private static List<AvroField> GenerateFields(Type type)
    {
        var properties = KsqlCreateStatementBuilder.GetSchemaProperties(type);
        var fields = new List<AvroField>();

        foreach (var property in properties)
        {
            var field = new AvroField
            {
                Name = property.Name,
                Type = MapToAvroType(property),
                Doc = GetPropertyDocumentation(property)
            };

            // Add default value if the property is nullable
            if (IsNullableProperty(property))
            {
                field.Default = null;
            }

            fields.Add(field);
        }

        return fields;
    }

    private static object MapToAvroType(PropertyInfo property)
    {
        var isNullable = IsNullableProperty(property);
        var avroType = GetAvroType(property);

        // Handle nullable types by creating a union with null
        if (isNullable)
        {
            return new object[] { "null", avroType };
        }

        return avroType;
    }

    private static object GetAvroType(PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // Handle decimal with precision
        if (underlyingType == typeof(decimal))
        {
            var decimalAttr = property.GetCustomAttribute<DecimalPrecisionAttribute>();
            if (decimalAttr != null)
            {
                return new
                {
                    type = "bytes",
                    logicalType = "decimal",
                    precision = decimalAttr.Precision,
                    scale = decimalAttr.Scale
                };
            }
            return new
            {
                type = "bytes",
                logicalType = "decimal",
                precision = 18,
                scale = 4
            };
        }

        // Handle DateTime types
        if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
        {
            return new
            {
                type = "long",
                logicalType = "timestamp-millis"
            };
        }

        // Handle Guid
        if (underlyingType == typeof(Guid))
        {
            return new
            {
                type = "string",
                logicalType = "uuid"
            };
        }

        // Handle basic types
        return underlyingType switch
        {
            Type t when t == typeof(bool) => "boolean",
            Type t when t == typeof(int) => "int",
            Type t when t == typeof(long) => "long",
            Type t when t == typeof(float) => "float",
            Type t when t == typeof(double) => "double",
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(byte[]) => "bytes",
            Type t when t == typeof(char) => "string",
            Type t when t == typeof(short) => "int", // Avro doesn't have short, use int
            Type t when t == typeof(byte) => "int", // Avro doesn't have byte, use int
            _ => "string" // Default fallback
        };
    }

    private static bool IsNullableProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;

        // 1. Nullable value types (int?, decimal?, bool?, etc.)
        if (Nullable.GetUnderlyingType(propertyType) != null)
            return true;

        // 2. Value types are non-nullable by default
        if (propertyType.IsValueType)
            return false;

        // 3. For reference types, check nullable context using NullabilityInfoContext (C# 8.0+)
        try
        {
            // 修正：正しい名前空間で NullabilityInfoContext を使用
            var nullabilityContext = new NullabilityInfoContext();
            var nullabilityInfo = nullabilityContext.Create(property);

            // WriteState indicates if the property can be assigned null
            // ReadState indicates if the property can return null
            return nullabilityInfo.WriteState == NullabilityState.Nullable ||
                   nullabilityInfo.ReadState == NullabilityState.Nullable;
        }
        catch
        {
            // Fallback: if NullabilityInfoContext fails, assume reference types are nullable
            // This provides backward compatibility for cases where nullable context is not available
            return !propertyType.IsValueType;
        }
    }

    private static bool IsPrimitiveType(Type type)
    {
        return type == typeof(string) ||
               type == typeof(int) ||
               type == typeof(long) ||
               type == typeof(Guid) ||
               type == typeof(byte[]);
    }

    private static string GeneratePrimitiveKeySchema(Type primitiveType)
    {
        return primitiveType switch
        {
            Type t when t == typeof(string) => "\"string\"",
            Type t when t == typeof(int) => "\"int\"",
            Type t when t == typeof(long) => "\"long\"",
            Type t when t == typeof(byte[]) => "\"bytes\"",
            Type t when t == typeof(Guid) => JsonSerializer.Serialize(new
            {
                type = "string",
                logicalType = "uuid"
            }),
            _ => "\"string\""
        };
    }

    private static string GenerateNullablePrimitiveKeySchema(Type primitiveType)
    {
        // 内部型のオブジェクト表現を直接作成
        object innerTypeObj = primitiveType switch
        {
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(int) => "int",
            Type t when t == typeof(long) => "long",
            Type t when t == typeof(byte[]) => "bytes",
            Type t when t == typeof(Guid) => new { type = "string", logicalType = "uuid" },
            _ => "string"
        };

        // Union配列を作成：["null", 型]
        var unionArray = new object[] { "null", innerTypeObj };

        // JSON配列として直接シリアライズ
        return JsonSerializer.Serialize(unionArray);
    }

    private static string? GetPropertyDocumentation(PropertyInfo property)
    {
        // Check for KafkaIgnore reason (shouldn't occur here, but safety check)
        var kafkaIgnoreAttr = property.GetCustomAttribute<KafkaIgnoreAttribute>();
        if (kafkaIgnoreAttr != null)
            return null; // This property should have been filtered out

        // TODO: Add XML documentation parsing if needed
        // For now, return null - can be extended later
        return null;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var words = input.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var result = string.Empty;

        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                result += char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word.Substring(1).ToLowerInvariant() : string.Empty);
            }
        }

        return result;
    }

    public static bool ValidateSchema(string schema)
    {
        if (string.IsNullOrEmpty(schema))
            return false;

        try
        {
            // Try to parse as JSON first
            using var document = JsonDocument.Parse(schema);
            var root = document.RootElement;

            // For Avro record schemas, check basic structure
            if (root.ValueKind == JsonValueKind.Object)
            {
                // Must have "type" property
                if (!root.TryGetProperty("type", out var typeElement))
                    return false;

                var typeValue = typeElement.GetString();

                // For record types, must have "name" property
                if (typeValue == "record")
                {
                    if (!root.TryGetProperty("name", out var nameElement))
                        return false;

                    var nameValue = nameElement.GetString();
                    if (string.IsNullOrEmpty(nameValue))
                        return false;
                }

                return true;
            }

            // For primitive schemas (like "string", "int"), just check if it's a valid string
            if (root.ValueKind == JsonValueKind.String)
            {
                var primitiveType = root.GetString();
                return !string.IsNullOrEmpty(primitiveType);
            }

            // For union types (arrays), validate each element
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.GetArrayLength() > 0;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static SchemaGenerationStats GetGenerationStats(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        var allProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var schemaProperties = KsqlCreateStatementBuilder.GetSchemaProperties(type);
        var ignoredProperties = KsqlCreateStatementBuilder.GetIgnoredProperties(type);

        return new SchemaGenerationStats
        {
            TotalProperties = allProperties.Length,
            IncludedProperties = schemaProperties.Length,
            IgnoredProperties = ignoredProperties.Length,
            IgnoredPropertyNames = ignoredProperties.Select(p => p.Name).ToList()
        };
    }
}