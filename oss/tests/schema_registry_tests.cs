using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.SchemaRegistry.Implementation;
using Xunit;

namespace KsqlDsl.Tests.SchemaRegistry
{
    /// <summary>
    /// Test entities for schema registry testing
    /// </summary>
    public class OrderEntityForRegistry
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime OrderDate { get; set; }
        public bool IsProcessed { get; set; }

        [KafkaIgnore(Reason = "Internal tracking")]
        public DateTime InternalTimestamp { get; set; }

        [KafkaIgnore]
        public string DebugInfo { get; set; } = string.Empty;
    }

    public class ProductEntityForRegistry
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public Guid ProductGuid { get; set; }
    }

    public class CustomerEntityWithNullables
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int? Age { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public bool? IsVerified { get; set; }

        [KafkaIgnore]
        public string? InternalNotes { get; set; }
    }

    /// <summary>
    /// Mock schema registry client for testing (Avro schemas only)
    /// </summary>
    public class MockSchemaRegistryClient : ISchemaRegistryClient
    {
        private readonly Dictionary<string, SchemaInfo> _schemas = new();
        private readonly Dictionary<int, SchemaInfo> _schemasById = new();
        private readonly Dictionary<string, List<int>> _subjectVersions = new();
        private int _nextSchemaId = 1;
        private bool _disposed = false;

        public async Task<int> RegisterSchemaAsync(string subject, string schema)
        {
            await Task.Delay(1); // Simulate async operation

            var schemaId = _nextSchemaId++;
            var version = GetNextVersion(subject);

            var schemaInfo = new SchemaInfo
            {
                Id = schemaId,
                Version = version,
                Subject = subject,
                Schema = schema,
                SchemaType = SchemaType.Avro // KsqlDsl supports Avro only
            };

            _schemas[subject] = schemaInfo;
            _schemasById[schemaId] = schemaInfo;

            if (!_subjectVersions.ContainsKey(subject))
                _subjectVersions[subject] = new List<int>();
            _subjectVersions[subject].Add(version);

            return schemaId;
        }

        public async Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema)
        {
            await Task.Delay(1); // Simulate async operation

            var keySchemaId = await RegisterKeySchemaAsync(topicName, keySchema);
            var valueSchemaId = await RegisterValueSchemaAsync(topicName, valueSchema);

            return (keySchemaId, valueSchemaId);
        }

        public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
        {
            await Task.Delay(1); // Simulate async operation

            var subject = $"{topicName}-key";
            return await RegisterSchemaAsync(subject, keySchema);
        }

        public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
        {
            await Task.Delay(1); // Simulate async operation

            var subject = $"{topicName}-value";
            return await RegisterSchemaAsync(subject, valueSchema);
        }

        public async Task<SchemaInfo> GetLatestSchemaAsync(string subject)
        {
            await Task.Delay(1); // Simulate async operation

            if (_schemas.TryGetValue(subject, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Subject '{subject}' not found");
        }

        public async Task<SchemaInfo> GetSchemaByIdAsync(int schemaId)
        {
            await Task.Delay(1); // Simulate async operation

            if (_schemasById.TryGetValue(schemaId, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Schema with ID '{schemaId}' not found");
        }

        public async Task<bool> CheckCompatibilityAsync(string subject, string schema)
        {
            await Task.Delay(1); // Simulate async operation

            // Simple mock: always compatible if subject exists
            return _schemas.ContainsKey(subject);
        }

        public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
        {
            await Task.Delay(1); // Simulate async operation

            if (_subjectVersions.TryGetValue(subject, out var versions))
                return versions;

            return new List<int>();
        }

        public async Task<SchemaInfo> GetSchemaAsync(string subject, int version)
        {
            await Task.Delay(1); // Simulate async operation

            if (_schemas.TryGetValue(subject, out var schema) && schema.Version == version)
                return schema;

            throw new SchemaRegistryOperationException($"Schema for subject '{subject}' version {version} not found");
        }

        public async Task<int> DeleteSchemaAsync(string subject, int version)
        {
            await Task.Delay(1); // Simulate async operation

            if (_schemas.ContainsKey(subject))
            {
                _schemas.Remove(subject);
                return version;
            }

            throw new SchemaRegistryOperationException($"Schema for subject '{subject}' version {version} not found");
        }

        public async Task<IList<string>> GetAllSubjectsAsync()
        {
            await Task.Delay(1); // Simulate async operation
            return _schemas.Keys.ToList();
        }

        private int GetNextVersion(string subject)
        {
            if (_subjectVersions.TryGetValue(subject, out var versions))
                return versions.Max() + 1;
            return 1;
        }

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Unit tests for schema generation
    /// </summary>
    public class SchemaGeneratorTests
    {
        [Fact]
        public void GenerateSchema_BasicEntity_Should_CreateValidAvroSchema()
        {
            // Act
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();

            // Assert
            Assert.NotNull(schema);
            Assert.Contains("OrderEntityForRegistry", schema);
            Assert.Contains("OrderId", schema);
            Assert.Contains("CustomerId", schema);
            Assert.Contains("Amount", schema);
            Assert.Contains("IsProcessed", schema);

            // Verify ignored properties are not included
            Assert.DoesNotContain("InternalTimestamp", schema);
            Assert.DoesNotContain("DebugInfo", schema);
        }

        [Fact]
        public void GenerateSchema_WithCustomNamespace_Should_UseCustomNamespace()
        {
            // Arrange
            var customNamespace = "com.company.events";

            // Act
            var schema = SchemaGenerator.GenerateSchema(typeof(ProductEntityForRegistry), customNamespace);

            // Assert
            Assert.Contains(customNamespace, schema);
            Assert.Contains("ProductEntityForRegistry", schema);
        }

        [Fact]
        public void GenerateSchema_WithOptions_Should_ApplyOptions()
        {
            // Arrange
            var options = new SchemaGenerationOptions
            {
                CustomName = "Order",
                Namespace = "com.events",
                Documentation = "Order event schema",
                PrettyFormat = true
            };

            // Act
            var schema = SchemaGenerator.GenerateSchema(typeof(OrderEntityForRegistry), options);

            // Assert
            Assert.Contains("Order", schema);
            Assert.Contains("com.events", schema);
            Assert.Contains("Order event schema", schema);
        }

        [Fact]
        public void GenerateSchema_WithNullableProperties_Should_CreateUnionTypes()
        {
            // Act
            var schema = SchemaGenerator.GenerateSchema<CustomerEntityWithNullables>();

            // Assert
            Assert.Contains("Age", schema);
            Assert.Contains("LastLoginDate", schema);
            Assert.Contains("IsVerified", schema);
            Assert.Contains("null", schema); // Should contain null for nullable types

            // Verify ignored nullable property is not included
            Assert.DoesNotContain("InternalNotes", schema);
        }

        [Fact]
        public void ValidateSchema_ValidSchema_Should_ReturnTrue()
        {
            // Arrange
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();

            // Act
            var isValid = SchemaGenerator.ValidateSchema(schema);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void ValidateSchema_InvalidSchema_Should_ReturnFalse()
        {
            // Arrange
            var invalidSchema = "{ invalid json }";

            // Act
            var isValid = SchemaGenerator.ValidateSchema(invalidSchema);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void GetGenerationStats_Should_ReturnCorrectStatistics()
        {
            // Act
            var stats = SchemaGenerator.GetGenerationStats(typeof(OrderEntityForRegistry));

            // Assert
            Assert.Equal(7, stats.TotalProperties); // All properties including ignored ones
            Assert.Equal(5, stats.IncludedProperties); // Properties not marked with [KafkaIgnore]
            Assert.Equal(2, stats.IgnoredProperties); // Properties marked with [KafkaIgnore]
            Assert.Contains("InternalTimestamp", stats.IgnoredPropertyNames);
            Assert.Contains("DebugInfo", stats.IgnoredPropertyNames);
        }

        [Fact]
        public void GenerateKeySchema_PrimitiveType_Should_CreateSimpleAvroSchema()
        {
            // Act - String key
            var stringKeySchema = SchemaGenerator.GenerateKeySchema<string>();
            var intKeySchema = SchemaGenerator.GenerateKeySchema<int>();
            var guidKeySchema = SchemaGenerator.GenerateKeySchema<Guid>();

            // Assert
            Assert.Contains("string", stringKeySchema);
            Assert.Contains("int", intKeySchema);
            Assert.Contains("uuid", guidKeySchema);
        }

        [Fact]
        public void GenerateKeySchema_ComplexType_Should_CreateRecordSchema()
        {
            // Arrange
            var keyType = typeof(OrderEntityForRegistry);

            // Act
            var keySchema = SchemaGenerator.GenerateKeySchema(keyType);

            // Assert
            Assert.Contains("OrderEntityForRegistryKey", keySchema);
            Assert.Contains("OrderId", keySchema);
            Assert.Contains("CustomerId", keySchema);

            // Verify ignored properties are not included
            Assert.DoesNotContain("InternalTimestamp", keySchema);
            Assert.DoesNotContain("DebugInfo", keySchema);
        }

        [Fact]
        public void GenerateTopicSchemas_Should_CreateBothKeyAndValueSchemas()
        {
            // Act
            var (keySchema, valueSchema) = SchemaGenerator.GenerateTopicSchemas<string, OrderEntityForRegistry>();

            // Assert
            Assert.Contains("string", keySchema);
            Assert.Contains("OrderEntityForRegistry", valueSchema);
        }

        [Fact]
        public void GenerateTopicSchemas_WithTopicName_Should_UseCustomNames()
        {
            // Act
            var (keySchema, valueSchema) = SchemaGenerator.GenerateTopicSchemas<int, OrderEntityForRegistry>("orders");

            // Assert
            Assert.Contains("int", keySchema); // Primitive key remains as primitive
            Assert.Contains("ordersValue", valueSchema);
        }

        [Fact]
        public void GenerateKeySchema_NullableType_Should_CreateUnionType()
        {
            // Act
            var nullableIntKeySchema = SchemaGenerator.GenerateKeySchema<int?>();

            // Assert
            Assert.Contains("null", nullableIntKeySchema);
            Assert.Contains("int", nullableIntKeySchema);
        }

        [Fact]
        public void GenerateSchema_EmptyNamespace_Should_ThrowArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                SchemaGenerator.GenerateSchema(typeof(OrderEntityForRegistry), string.Empty));
        }
    }

    /// <summary>
    /// Unit tests for schema registry client
    /// </summary>
    public class SchemaRegistryClientTests
    {
        [Fact]
        public async Task RegisterSchemaAsync_ValidSchema_Should_ReturnSchemaId()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var subject = "orders-value";

            // Act
            var schemaId = await client.RegisterSchemaAsync(subject, schema);

            // Assert
            Assert.True(schemaId > 0);
        }

        [Fact]
        public async Task GetLatestSchemaAsync_ExistingSubject_Should_ReturnSchema()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var subject = "orders-value";

            await client.RegisterSchemaAsync(subject, schema);

            // Act
            var retrievedSchema = await client.GetLatestSchemaAsync(subject);

            // Assert
            Assert.NotNull(retrievedSchema);
            Assert.Equal(subject, retrievedSchema.Subject);
            Assert.Equal(schema, retrievedSchema.Schema);
            Assert.Equal(SchemaType.Avro, retrievedSchema.SchemaType);
        }

        [Fact]
        public async Task GetSchemaByIdAsync_ExistingId_Should_ReturnSchema()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var subject = "orders-value";

            var schemaId = await client.RegisterSchemaAsync(subject, schema);

            // Act
            var retrievedSchema = await client.GetSchemaByIdAsync(schemaId);

            // Assert
            Assert.NotNull(retrievedSchema);
            Assert.Equal(schemaId, retrievedSchema.Id);
            Assert.Equal(schema, retrievedSchema.Schema);
        }

        [Fact]
        public async Task CheckCompatibilityAsync_ExistingSubject_Should_ReturnTrue()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var subject = "orders-value";

            await client.RegisterSchemaAsync(subject, schema);

            // Act
            var isCompatible = await client.CheckCompatibilityAsync(subject, schema);

            // Assert
            Assert.True(isCompatible);
        }

        [Fact]
        public async Task GetAllSubjectsAsync_WithRegisteredSubjects_Should_ReturnSubjects()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema1 = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var schema2 = SchemaGenerator.GenerateSchema<ProductEntityForRegistry>();

            await client.RegisterSchemaAsync("orders-value", schema1);
            await client.RegisterSchemaAsync("products-value", schema2);

            // Act
            var subjects = await client.GetAllSubjectsAsync();

            // Assert
            Assert.Contains("orders-value", subjects);
            Assert.Contains("products-value", subjects);
        }

        [Fact]
        public async Task RegisterTopicSchemasAsync_ValidSchemas_Should_ReturnBothSchemaIds()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var topicName = "orders";
            var keySchema = SchemaGenerator.GenerateKeySchema<string>();
            var valueSchema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();

            // Act
            var (keySchemaId, valueSchemaId) = await client.RegisterTopicSchemasAsync(topicName, keySchema, valueSchema);

            // Assert
            Assert.True(keySchemaId > 0);
            Assert.True(valueSchemaId > 0);
            Assert.NotEqual(keySchemaId, valueSchemaId);
        }

        [Fact]
        public async Task RegisterKeySchemaAsync_ValidSchema_Should_ReturnSchemaId()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var topicName = "orders";
            var keySchema = SchemaGenerator.GenerateKeySchema<string>();

            // Act
            var keySchemaId = await client.RegisterKeySchemaAsync(topicName, keySchema);

            // Assert
            Assert.True(keySchemaId > 0);

            // Verify key subject naming
            var retrievedSchema = await client.GetLatestSchemaAsync($"{topicName}-key");
            Assert.Equal(keySchema, retrievedSchema.Schema);
        }

        [Fact]
        public async Task RegisterValueSchemaAsync_ValidSchema_Should_ReturnSchemaId()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var topicName = "orders";
            var valueSchema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();

            // Act
            var valueSchemaId = await client.RegisterValueSchemaAsync(topicName, valueSchema);

            // Assert
            Assert.True(valueSchemaId > 0);

            // Verify value subject naming
            var retrievedSchema = await client.GetLatestSchemaAsync($"{topicName}-value");
            Assert.Equal(valueSchema, retrievedSchema.Schema);
        }

        [Fact]
        public async Task RegisterTopicSchemasAsync_NullTopicName_Should_ThrowArgumentException()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var keySchema = SchemaGenerator.GenerateKeySchema<string>();
            var valueSchema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.RegisterTopicSchemasAsync(null, keySchema, valueSchema));
        }

        [Fact]
        public async Task RegisterSchemaAsync_EmptySchema_Should_ThrowArgumentException()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var subject = "orders-value";

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.RegisterSchemaAsync(subject, string.Empty));
        }

        [Fact]
        public async Task GetLatestSchemaAsync_NonExistentSubject_Should_ThrowException()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var subject = "non-existent-subject";

            // Act & Assert
            await Assert.ThrowsAsync<SchemaRegistryOperationException>(() =>
                client.GetLatestSchemaAsync(subject));
        }

        [Fact]
        public async Task GetSchemaByIdAsync_NonExistentId_Should_ThrowException()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var nonExistentId = 999;

            // Act & Assert
            await Assert.ThrowsAsync<SchemaRegistryOperationException>(() =>
                client.GetSchemaByIdAsync(nonExistentId));
        }

        [Fact]
        public async Task GetSchemaByIdAsync_InvalidId_Should_ThrowArgumentException()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var invalidId = 0;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.GetSchemaByIdAsync(invalidId));
        }
    }

    /// <summary>
    /// Integration tests combining schema generation and registry operations
    /// </summary>
    public class SchemaRegistryIntegrationTests
    {
        [Fact]
        public async Task EndToEndTopicSchemaFlow_Should_WorkCorrectly()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var topicName = "orders";

            // Act - Generate both key and value schemas
            var (keySchema, valueSchema) = SchemaGenerator.GenerateTopicSchemas<string, OrderEntityForRegistry>(topicName);

            // Act - Register both schemas
            var (keySchemaId, valueSchemaId) = await client.RegisterTopicSchemasAsync(topicName, keySchema, valueSchema);

            // Act - Retrieve and verify
            var retrievedKeySchema = await client.GetLatestSchemaAsync($"{topicName}-key");
            var retrievedValueSchema = await client.GetLatestSchemaAsync($"{topicName}-value");

            // Assert
            Assert.True(keySchemaId > 0);
            Assert.True(valueSchemaId > 0);
            Assert.NotEqual(keySchemaId, valueSchemaId);

            Assert.Equal(keySchema, retrievedKeySchema.Schema);
            Assert.Equal(valueSchema, retrievedValueSchema.Schema);
            Assert.Equal($"{topicName}-key", retrievedKeySchema.Subject);
            Assert.Equal($"{topicName}-value", retrievedValueSchema.Subject);
            Assert.Equal(SchemaType.Avro, retrievedKeySchema.SchemaType);
            Assert.Equal(SchemaType.Avro, retrievedValueSchema.SchemaType);
        }

        [Fact]
        public async Task ComplexKeySchemaRegistration_Should_ExcludeIgnoredProperties()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var topicName = "orders";

            // Act - Use complex type as key (will be treated as record schema)
            var keySchema = SchemaGenerator.GenerateKeySchema<OrderEntityForRegistry>();
            var keySchemaId = await client.RegisterKeySchemaAsync(topicName, keySchema);
            var retrievedKeySchema = await client.GetLatestSchemaAsync($"{topicName}-key");

            // Assert
            Assert.DoesNotContain("InternalTimestamp", retrievedKeySchema.Schema);
            Assert.DoesNotContain("DebugInfo", retrievedKeySchema.Schema);
            Assert.Contains("OrderId", retrievedKeySchema.Schema);
            Assert.Contains("CustomerId", retrievedKeySchema.Schema);
        }

        [Fact]
        public void GenerateSchema_NullType_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => SchemaGenerator.GenerateSchema(null));
        }

        [Fact]
        public void GenerateKeySchema_NullType_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => SchemaGenerator.GenerateKeySchema((Type)null));
        }

        [Fact]
        public async Task RegisterSchemaAsync_ValidSchema_Should_ReturnSchemaId()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var subject = "orders-value";

            // Act
            var schemaId = await client.RegisterSchemaAsync(subject, schema);

            // Assert
            Assert.True(schemaId > 0);
        }

        [Fact]
        public void PrimitiveKeyTypes_Should_GenerateCorrectAvroTypes()
        {
            // Test all supported primitive key types
            var stringKeySchema = SchemaGenerator.GenerateKeySchema<string>();
            var intKeySchema = SchemaGenerator.GenerateKeySchema<int>();
            var longKeySchema = SchemaGenerator.GenerateKeySchema<long>();
            var guidKeySchema = SchemaGenerator.GenerateKeySchema<Guid>();
            var bytesKeySchema = SchemaGenerator.GenerateKeySchema<byte[]>();

            // Assert correct Avro types
            Assert.Equal("\"string\"", stringKeySchema);
            Assert.Equal("\"int\"", intKeySchema);
            Assert.Equal("\"long\"", longKeySchema);
            Assert.Contains("uuid", guidKeySchema);
            Assert.Equal("\"bytes\"", bytesKeySchema);
        }

        [Fact]
        public async Task MultipleSchemaRegistration_Should_MaintainSeparateVersions()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var subject = "orders-value";

            var schema1 = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var schema2 = SchemaGenerator.GenerateSchema<ProductEntityForRegistry>();

            // Act
            var schemaId1 = await client.RegisterSchemaAsync(subject, schema1);
            var schemaId2 = await client.RegisterSchemaAsync("products-value", schema2);

            var versions1 = await client.GetSchemaVersionsAsync(subject);
            var versions2 = await client.GetSchemaVersionsAsync("products-value");

            // Assert
            Assert.NotEqual(schemaId1, schemaId2);
            Assert.Single(versions1);
            Assert.Single(versions2);
            Assert.Equal(1, versions1[0]);
            Assert.Equal(1, versions2[0]);
        }

        [Fact]
        public async Task SchemaWithIgnoredProperties_Should_ExcludeFromRegistration()
        {
            // Arrange
            var client = new MockSchemaRegistryClient();
            var subject = "orders-value";

            // Act
            var schema = SchemaGenerator.GenerateSchema<OrderEntityForRegistry>();
            var schemaId = await client.RegisterSchemaAsync(subject, schema);
            var retrievedSchema = await client.GetLatestSchemaAsync(subject);

            // Assert
            Assert.DoesNotContain("InternalTimestamp", retrievedSchema.Schema);
            Assert.DoesNotContain("DebugInfo", retrievedSchema.Schema);
            Assert.Contains("OrderId", retrievedSchema.Schema);
            Assert.Contains("CustomerId", retrievedSchema.Schema);
        }

        [Fact]
        public void SchemaRegistryConfig_Should_HaveCorrectDefaults()
        {
            // Act
            var config = new SchemaRegistryConfig();

            // Assert
            Assert.Equal("http://localhost:8081", config.Url);
            Assert.Equal(30000, config.TimeoutMs);
            Assert.Equal(1000, config.MaxCachedSchemas);
            Assert.NotNull(config.Properties);
            Assert.Empty(config.Properties);
        }

        [Fact]
        public void SchemaGenerationOptions_Should_HaveCorrectDefaults()
        {
            // Act
            var options = new SchemaGenerationOptions();

            // Assert
            Assert.True(options.PrettyFormat);
            Assert.False(options.UseKebabCase);
            Assert.Null(options.CustomName);
            Assert.Null(options.Namespace);
            Assert.Null(options.Documentation);
        }
    }
}