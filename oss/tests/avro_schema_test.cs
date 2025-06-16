using KsqlDsl.Attributes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.Services;
using KsqlDsl.Tests.SchemaRegistry;
using System;
using System.Threading.Tasks;
using Xunit;

namespace KsqlDsl.Tests
{
    [Topic("avro-test-events")]
    public class AvroTestEvent
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AvroTestContext : KafkaContext
    {
        public EventSet<AvroTestEvent> AvroEvents => Set<AvroTestEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<AvroTestEvent>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .UseSchemaRegistry("http://localhost:8081")
                .EnableDebugLogging();
        }
    }

    public class AvroSchemaRegistrationTests
    {
        [Fact]
        public async Task AvroSchemaRegistrationService_RegisterAllSchemasAsync_Should_Work()
        {
            // Arrange
            var mockClient = new MockSchemaRegistryClient();
            var service = new AvroSchemaRegistrationService(mockClient, Validation.ValidationMode.Strict, true);
            
            var modelBuilder = new ModelBuilder();
            modelBuilder.Event<AvroTestEvent>();
            modelBuilder.Build();
            
            var entityModels = modelBuilder.GetEntityModels();

            // Act
            await service.RegisterAllSchemasAsync(entityModels);

            // Assert
            var registeredSchemas = await service.GetRegisteredSchemasAsync();
            Assert.Contains("avro-test-events-key", registeredSchemas);
            Assert.Contains("avro-test-events-value", registeredSchemas);
        }

        [Fact]
        public async Task AvroSchemaRegistrationService_WithNullClient_Should_Skip()
        {
            // Arrange
            var service = new AvroSchemaRegistrationService(null, Validation.ValidationMode.Strict, true);
            
            var modelBuilder = new ModelBuilder();
            modelBuilder.Event<AvroTestEvent>();
            modelBuilder.Build();
            
            var entityModels = modelBuilder.GetEntityModels();

            // Act & Assert - Should not throw
            await service.RegisterAllSchemasAsync(entityModels);
        }

        [Fact]
        public async Task ModelBuilder_BuildAsync_Should_RegisterSchemas()
        {
            // Arrange
            var mockClient = new MockSchemaRegistryClient();
            var service = new AvroSchemaRegistrationService(mockClient, Validation.ValidationMode.Strict, true);
            
            var modelBuilder = new ModelBuilder();
            modelBuilder.SetSchemaRegistrationService(service);
            modelBuilder.Event<AvroTestEvent>();

            // Act
            await modelBuilder.BuildAsync();

            // Assert
            var registeredSchemas = await service.GetRegisteredSchemasAsync();
            Assert.Contains("avro-test-events-key", registeredSchemas);
            Assert.Contains("avro-test-events-value", registeredSchemas);
        }

        [Fact]
        public async Task ModelBuilder_CheckEntitySchemaCompatibilityAsync_Should_Work()
        {
            // Arrange
            var mockClient = new MockSchemaRegistryClient();
            var service = new AvroSchemaRegistrationService(mockClient, Validation.ValidationMode.Strict, true);
            
            var modelBuilder = new ModelBuilder();
            modelBuilder.SetSchemaRegistrationService(service);
            modelBuilder.Event<AvroTestEvent>();
            await modelBuilder.BuildAsync();

            // Act
            var isCompatible = await modelBuilder.CheckEntitySchemaCompatibilityAsync<AvroTestEvent>();

            // Assert
            Assert.True(isCompatible);
        }

        [Fact]
        public void KafkaContext_WithSchemaRegistration_Should_Initialize()
        {
            // Arrange & Act
            using var context = new AvroTestContext();

            // Assert
            Assert.NotNull(context);
            Assert.NotNull(context.Options);
            Assert.True(context.Options.EnableAutoSchemaRegistration);
        }

        [Fact]
        public async Task KafkaContext_GetRegisteredSchemasAsync_Should_Work()
        {
            // Arrange
            using var context = new AvroTestContext();

            // Act
            var schemas = await context.GetRegisteredSchemasAsync();

            // Assert
            Assert.NotNull(schemas);
        }

        [Fact]
        public async Task KafkaContext_CheckEntitySchemaCompatibilityAsync_Should_Work()
        {
            // Arrange
            using var context = new AvroTestContext();

            // Act
            var isCompatible = await context.CheckEntitySchemaCompatibilityAsync<AvroTestEvent>();

            // Assert - Default should be false since no actual client is configured
            Assert.False(isCompatible);
        }
    }
}