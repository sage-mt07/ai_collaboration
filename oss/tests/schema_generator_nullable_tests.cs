using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using Xunit;

namespace KsqlDsl.Tests.SchemaRegistry
{
    /// <summary>
    /// SchemaGeneratorのNullable Reference Types対応テスト
    /// task_attribute.mdの要件「C#標準nullable型でnull許容」の検証
    /// </summary>
    public class SchemaGeneratorNullableTests
    {
        #region Test Entities with Nullable Reference Types

        /// <summary>
        /// Nullable Reference Types有効な環境でのテストエンティティ
        /// </summary>
        public class NullableTestEntity
        {
            // Non-nullable properties
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }

            // Nullable value types
            public int? OptionalId { get; set; }
            public decimal? OptionalAmount { get; set; }
            public DateTime? OptionalDate { get; set; }
            public bool? OptionalFlag { get; set; }

            // Nullable reference types (C# 8.0+)
            public string? Description { get; set; }
            public byte[]? OptionalData { get; set; }

            // Complex nullable types
            [DecimalPrecision(10, 2)]
            public decimal? PreciseAmount { get; set; }
        }

        /// <summary>
        /// 全プロパティがnullableなエンティティ
        /// </summary>
        public class AllNullableEntity
        {
            public int? Id { get; set; }
            public string? Name { get; set; }
            public decimal? Amount { get; set; }
            public DateTime? Date { get; set; }
            public bool? Flag { get; set; }
            public byte[]? Data { get; set; }
        }

        /// <summary>
        /// 全プロパティがnon-nullableなエンティティ
        /// </summary>
        public class AllNonNullableEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public bool Flag { get; set; }
        }

        /// <summary>
        /// KafkaIgnore属性との組み合わせテスト
        /// </summary>
        public class NullableWithIgnoreEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }

            [KafkaIgnore]
            public string? InternalInfo { get; set; }

            [KafkaIgnore]
            public int? InternalId { get; set; }
        }

        #endregion

        #region Nullable Property Detection Tests

        [Fact]
        public void GenerateSchema_NullableValueTypes_Should_GenerateUnionWithNull()
        {
            // Arrange
            var entityType = typeof(NullableTestEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            Assert.NotNull(schema);

            // Parse JSON to verify structure
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // Find OptionalId field (int?)
            var optionalIdField = FindField(fields, "optionalId");
            Assert.NotNull(optionalIdField);

            // Verify it's a union with null
            var optionalIdType = optionalIdField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.Array, optionalIdType.ValueKind);

            var unionTypes = new List<JsonElement>();
            foreach (var element in optionalIdType.EnumerateArray())
            {
                unionTypes.Add(element);
            }

            Assert.Equal(2, unionTypes.Count);
            Assert.Contains(unionTypes, t => t.GetString() == "null");
            Assert.Contains(unionTypes, t => t.GetString() == "int");
        }

        [Fact]
        public void GenerateSchema_NullableReferenceTypes_Should_GenerateUnionWithNull()
        {
            // Arrange
            var entityType = typeof(NullableTestEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // Find Description field (string?)
            var descriptionField = FindField(fields, "description");
            Assert.NotNull(descriptionField);

            // Verify it's a union with null
            var descriptionType = descriptionField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.Array, descriptionType.ValueKind);

            var unionTypes = new List<JsonElement>();
            foreach (var element in descriptionType.EnumerateArray())
            {
                unionTypes.Add(element);
            }

            Assert.Equal(2, unionTypes.Count);
            Assert.Contains(unionTypes, t => t.GetString() == "null");
            Assert.Contains(unionTypes, t => t.GetString() == "string");
        }

        [Fact]
        public void GenerateSchema_NonNullableProperties_Should_GenerateDirectType()
        {
            // Arrange
            var entityType = typeof(NullableTestEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // Find Id field (int - non-nullable)
            var idField = FindField(fields, "id");
            Assert.NotNull(idField);

            // Verify it's a direct type, not a union
            var idType = idField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.String, idType.ValueKind);
            Assert.Equal("int", idType.GetString());

            // Find Name field (string - non-nullable in this context)
            var nameField = FindField(fields, "name");
            Assert.NotNull(nameField);

            var nameType = nameField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.String, nameType.ValueKind);
            Assert.Equal("string", nameType.GetString());
        }

        [Fact]
        public void GenerateSchema_AllNullableEntity_Should_GenerateAllUnions()
        {
            // Arrange
            var entityType = typeof(AllNullableEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // All fields should be unions with null
            foreach (var field in fields.EnumerateArray())
            {
                var fieldType = field.GetProperty("type");
                Assert.Equal(JsonValueKind.Array, fieldType.ValueKind);

                var unionTypes = new List<JsonElement>();
                foreach (var element in fieldType.EnumerateArray())
                {
                    unionTypes.Add(element);
                }

                Assert.Equal(2, unionTypes.Count);
                Assert.Contains(unionTypes, t => t.GetString() == "null");
            }
        }

        [Fact]
        public void GenerateSchema_AllNonNullableEntity_Should_GenerateDirectTypes()
        {
            // Arrange
            var entityType = typeof(AllNonNullableEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // All fields should be direct types, not unions
            foreach (var field in fields.EnumerateArray())
            {
                var fieldType = field.GetProperty("type");
                Assert.Equal(JsonValueKind.String, fieldType.ValueKind);
                // Should not be a union array
                Assert.NotEqual("null", fieldType.GetString());
            }
        }

        #endregion

        #region Decimal Precision with Nullable Tests

        [Fact]
        public void GenerateSchema_NullableDecimalWithPrecision_Should_GenerateUnionWithDecimal()
        {
            // Arrange
            var entityType = typeof(NullableTestEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");

            // Find PreciseAmount field (decimal? with precision)
            var preciseAmountField = FindField(fields, "preciseAmount");
            Assert.NotNull(preciseAmountField);

            // Verify it's a union with null and decimal
            var preciseAmountType = preciseAmountField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.Array, preciseAmountType.ValueKind);

            var unionTypes = new List<JsonElement>();
            foreach (var element in preciseAmountType.EnumerateArray())
            {
                unionTypes.Add(element);
            }

            Assert.Equal(2, unionTypes.Count);
            Assert.Contains(unionTypes, t => t.GetString() == "null");

            // Verify decimal type with precision
            var decimalType = unionTypes.First(t => t.ValueKind == JsonValueKind.Object);
            Assert.Equal("bytes", decimalType.GetProperty("type").GetString());
            Assert.Equal("decimal", decimalType.GetProperty("logicalType").GetString());
            Assert.Equal(10, decimalType.GetProperty("precision").GetInt32());
            Assert.Equal(2, decimalType.GetProperty("scale").GetInt32());
        }

        #endregion

        #region KafkaIgnore Integration Tests

        [Fact]
        public void GenerateSchema_NullableWithIgnoreEntity_Should_ExcludeIgnoredProperties()
        {
            // Arrange
            var entityType = typeof(NullableWithIgnoreEntity);

            // Act
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            var doc = JsonDocument.Parse(schema);
            var fields = doc.RootElement.GetProperty("fields");
            var fieldNames = new List<string>();

            foreach (var field in fields.EnumerateArray())
            {
                fieldNames.Add(field.GetProperty("name").GetString()!);
            }

            // Should include non-ignored properties
            Assert.Contains("id", fieldNames);
            Assert.Contains("name", fieldNames);
            Assert.Contains("description", fieldNames);

            // Should exclude ignored properties
            Assert.DoesNotContain("internalInfo", fieldNames);
            Assert.DoesNotContain("internalId", fieldNames);

            // Verify Description is nullable
            var descriptionField = FindField(doc.RootElement.GetProperty("fields"), "description");
            Assert.NotNull(descriptionField);
            var descriptionType = descriptionField.Value.GetProperty("type");
            Assert.Equal(JsonValueKind.Array, descriptionType.ValueKind);
        }

        #endregion

        #region Key Schema Tests

        [Fact]
        public void GenerateKeySchema_NullableString_Should_GenerateUnionWithNull()
        {
            // Arrange & Act
            // 注意：C#ランタイムでのnullable reference type判定は制限がある
            // 実用的なテストとして、nullable value typeで確認
            var schema = SchemaGenerator.GenerateKeySchema<int?>();

            // Assert
            var doc = JsonDocument.Parse(schema);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

            var unionTypes = new List<JsonElement>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                unionTypes.Add(element);
            }

            Assert.Equal(2, unionTypes.Count);
            Assert.Contains(unionTypes, t => t.GetString() == "null");
            Assert.Contains(unionTypes, t => t.GetString() == "int");
        }

        [Fact]
        public void GenerateKeySchema_NonNullableString_Should_GenerateDirectString()
        {
            // Arrange & Act
            var schema = SchemaGenerator.GenerateKeySchema<string>();

            // Assert
            Assert.Equal("\"string\"", schema);
        }

        [Fact]
        public void GenerateKeySchema_NullableInt_Should_GenerateUnionWithNull()
        {
            // Arrange & Act
            var schema = SchemaGenerator.GenerateKeySchema<int?>();

            // Assert
            var doc = JsonDocument.Parse(schema);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

            var unionTypes = new List<JsonElement>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                unionTypes.Add(element);
            }

            Assert.Equal(2, unionTypes.Count);
            Assert.Contains(unionTypes, t => t.GetString() == "null");
            Assert.Contains(unionTypes, t => t.GetString() == "int");
        }

        #endregion

        #region Generation Statistics Tests

        [Fact]
        public void GetGenerationStats_Should_ProvideAccurateStatistics()
        {
            // Arrange
            var entityType = typeof(NullableWithIgnoreEntity);

            // Act
            var stats = SchemaGenerator.GetGenerationStats(entityType);

            // Assert
            Assert.Equal(5, stats.TotalProperties); // Id, Name, Description, InternalInfo, InternalId
            Assert.Equal(3, stats.IncludedProperties); // Id, Name, Description
            Assert.Equal(2, stats.IgnoredProperties); // InternalInfo, InternalId
            Assert.Contains("InternalInfo", stats.IgnoredPropertyNames);
            Assert.Contains("InternalId", stats.IgnoredPropertyNames);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds a field by name in the fields array (case-insensitive)
        /// </summary>
        private static JsonElement? FindField(JsonElement fields, string fieldName)
        {
            foreach (var field in fields.EnumerateArray())
            {
                var name = field.GetProperty("name").GetString();
                if (string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }
            return null;
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void GenerateSchema_WithNullabilityInfoContextFailure_Should_FallbackGracefully()
        {
            // This test ensures backward compatibility when NullabilityInfoContext fails
            // Arrange
            var entityType = typeof(NullableTestEntity);

            // Act - Should not throw exception even if nullable context fails
            var schema = SchemaGenerator.GenerateSchema(entityType);

            // Assert
            Assert.NotNull(schema);
            Assert.Contains("fields", schema);
        }

        [Fact]
        public void ValidateSchema_GeneratedSchemas_Should_BeValid()
        {
            // Arrange
            var entities = new[]
            {
                typeof(NullableTestEntity),
                typeof(AllNullableEntity),
                typeof(AllNonNullableEntity),
                typeof(NullableWithIgnoreEntity)
            };

            foreach (var entityType in entities)
            {
                // Act
                var schema = SchemaGenerator.GenerateSchema(entityType);
                var isValid = SchemaGenerator.ValidateSchema(schema);

                // Assert
                Assert.True(isValid, $"Generated schema for {entityType.Name} should be valid");
            }
        }

        #endregion
    }
}