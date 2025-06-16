using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.Attributes;

namespace KsqlDsl.Avro
{
    public static class AvroSchemaExtensions
    {
        public static (string keySchema, string valueSchema) GenerateKeyValueSchemas(
            this EntityModel entityModel)
        {
            if (entityModel == null)
                throw new ArgumentNullException(nameof(entityModel));

            var keySchema = GenerateKeySchemaFromModel(entityModel);
            var valueSchema = GenerateValueSchemaFromModel(entityModel);

            return (keySchema, valueSchema);
        }

        private static string GenerateKeySchemaFromModel(EntityModel entityModel)
        {
            var keyProperties = entityModel.KeyProperties;

            if (keyProperties.Length == 0)
                return "\"string\"";

            if (keyProperties.Length == 1)
            {
                var keyProperty = keyProperties[0];
                return GeneratePrimitiveKeySchema(keyProperty.PropertyType);
            }

            return GenerateCompositeKeySchema(keyProperties);
        }

        private static string GenerateValueSchemaFromModel(EntityModel entityModel)
        {
            var entityType = entityModel.EntityType;
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;

            var schema = new AvroSchema
            {
                Type = "record",
                Name = $"{topicName}_value",
                Namespace = $"{entityType.Namespace}.Avro",
                Fields = GenerateFieldsFromProperties(entityModel.AllProperties)
            };

            return JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        private static string GeneratePrimitiveKeySchema(Type keyType)
        {
            var underlyingType = Nullable.GetUnderlyingType(keyType) ?? keyType;

            return underlyingType switch
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

        private static string GenerateCompositeKeySchema(PropertyInfo[] keyProperties)
        {
            var fields = new List<AvroField>();

            foreach (var prop in keyProperties.OrderBy(p => p.GetCustomAttribute<KeyAttribute>()?.Order ?? 0))
            {
                fields.Add(new AvroField
                {
                    Name = prop.Name,
                    Type = MapPropertyToAvroType(prop)
                });
            }

            var schema = new AvroSchema
            {
                Type = "record",
                Name = "CompositeKey",
                Fields = fields
            };

            return JsonSerializer.Serialize(schema, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        private static List<AvroField> GenerateFieldsFromProperties(PropertyInfo[] properties)
        {
            var fields = new List<AvroField>();

            foreach (var property in properties)
            {
                if (property.GetCustomAttribute<KsqlDsl.Modeling.KafkaIgnoreAttribute>() != null)
                    continue;

                fields.Add(new AvroField
                {
                    Name = property.Name,
                    Type = MapPropertyToAvroType(property)
                });
            }

            return fields;
        }

        private static object MapPropertyToAvroType(PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            var isNullable = Nullable.GetUnderlyingType(propertyType) != null ||
                           (!propertyType.IsValueType && IsNullableReferenceType(property));

            var avroType = GetBasicAvroType(property, underlyingType);

            return isNullable ? new object[] { "null", avroType } : avroType;
        }

        private static object GetBasicAvroType(PropertyInfo property, Type underlyingType)
        {
            if (underlyingType == typeof(decimal))
            {
                var decimalAttr = property.GetCustomAttribute<Ksql.EntityFrameworkCore.Modeling.DecimalPrecisionAttribute>();
                return new
                {
                    type = "bytes",
                    logicalType = "decimal",
                    precision = decimalAttr?.Precision ?? 18,
                    scale = decimalAttr?.Scale ?? 4
                };
            }

            if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
            {
                return new
                {
                    type = "long",
                    logicalType = "timestamp-millis"
                };
            }

            if (underlyingType == typeof(Guid))
            {
                return new
                {
                    type = "string",
                    logicalType = "uuid"
                };
            }

            return underlyingType switch
            {
                Type t when t == typeof(bool) => "boolean",
                Type t when t == typeof(int) => "int",
                Type t when t == typeof(long) => "long",
                Type t when t == typeof(float) => "float",
                Type t when t == typeof(double) => "double",
                Type t when t == typeof(string) => "string",
                Type t when t == typeof(byte[]) => "bytes",
                _ => "string"
            };
        }

        private static bool IsNullableReferenceType(PropertyInfo property)
        {
            try
            {
                var nullabilityContext = new System.Diagnostics.CodeAnalysis.NullabilityInfoContext();
                var nullabilityInfo = nullabilityContext.Create(property);
                return nullabilityInfo.WriteState == System.Diagnostics.CodeAnalysis.NullabilityState.Nullable;
            }
            catch
            {
                return !property.PropertyType.IsValueType;
            }
        }
    }
}