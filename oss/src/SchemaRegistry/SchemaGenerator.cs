using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Ksql;
using KsqlDsl.Modeling;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Generates Avro schemas from C# POCO types, respecting KafkaIgnore attributes
/// </summary>
public static class SchemaGenerator
{
    /// <summary>
    /// Generates an Avro schema from a C# type
    /// </summary>
    /// <typeparam name="T">The type to generate schema for</typeparam>
    /// <returns>Avro schema as JSON string</returns>
    public static string GenerateSchema<T>()
    {
        return GenerateSchema(typeof(T));
    }

    /// <summary>
    /// Generates an Avro schema from a C# type
    /// </summary>
    /// <param name="type">The type to generate schema for</param>
    /// <returns>Avro schema as JSON string</returns>
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

    /// <summary>
    /// Generates schema with custom namespace
    /// </summary>
    /// <param name="type">The type to generate schema for</param>
    /// <param name="namespaceName">Custom namespace for the schema</param>
    /// <returns>Avro schema as JSON string</returns>
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

    /// <summary>
    /// Generates schema with additional metadata
    /// </summary>
    /// <param name="type">The type to generate schema for</param>
    /// <param name="options">Schema generation options</param>
    /// <returns>Avro schema as JSON string</returns>
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

    /// <summary>
    /// Generates fields for the Avro schema, excluding properties marked with [KafkaIgnore]
    /// </summary>
    /// <param name="type">The type to analyze</param>
    /// <returns>List of Avro field definitions</returns>
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

    /// <summary>
    /// Maps C# property types to Avro types
    /// </summary>
    /// <param name="property">The property to map</param>
    /// <returns>Avro type definition</returns>
    private static object MapToAvroType(PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        var isNullable = underlyingType != null;
        var actualType = underlyingType ?? propertyType;

        var avroType = GetAvroType(actualType, property);

        // Handle nullable types by creating a union with null
        if (isNullable)
        {
            return new object[] { "null", avroType };
        }

        return avroType;
    }

    /// <summary>
    /// Gets the basic Avro type for a C# type
    /// </summary>
    /// <param name="type">The C# type</param>
    /// <param name="property">The property (for attribute analysis)</param>
    /// <returns>Avro type definition</returns>
    private static object GetAvroType(Type type, PropertyInfo property)
    {
        // Handle decimal with precision
        if (type == typeof(decimal))
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
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return new
            {
                type = "long",
                logicalType = "timestamp-millis"
            };
        }

        // Handle Guid
        if (type == typeof(Guid))
        {
            return new
            {
                type = "string",
                logicalType = "uuid"
            };
        }

        // Handle basic types
        return type switch
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

    /// <summary>
    /// Checks if a property is nullable
    /// </summary>
    /// <param name="property">The property to check</param>
    /// <returns>True if nullable, false otherwise</returns>
    private static bool IsNullableProperty(PropertyInfo property)
    {
        // Check for Nullable<T>
        if (Nullable.GetUnderlyingType(property.PropertyType) != null)
            return true;

        // Check for reference types (except string which we treat as non-nullable by default)
        if (!property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            return true;

        // Check for nullable reference type annotations (C# 8+)
        var nullableContext = new NullabilityInfoContext();
        var nullabilityInfo = nullableContext.Create(property);
        return nullabilityInfo.WriteState == NullabilityState.Nullable;
    }

    /// <summary>
    /// Gets documentation for a property from XML comments or attributes
    /// </summary>
    /// <param name="property">The property</param>
    /// <returns>Documentation string or null</returns>
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

    /// <summary>
    /// Validates that the generated schema is valid Avro
    /// </summary>
    /// <param name="schema">The schema JSON to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidateSchema(string schema)
    {
        if (string.IsNullOrEmpty(schema))
            return false;

        try
        {
            var avroSchema = JsonSerializer.Deserialize<AvroSchema>(schema);
            return avroSchema != null && 
                   !string.IsNullOrEmpty(avroSchema.Type) && 
                   !string.IsNullOrEmpty(avroSchema.Name);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets schema generation statistics
    /// </summary>
    /// <param name="type">The type to analyze</param>
    /// <returns>Schema generation statistics</returns>
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





