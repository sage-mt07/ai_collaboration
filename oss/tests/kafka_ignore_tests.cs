using System;
using System.Linq;
using KsqlDsl.Ksql;
using KsqlDsl.Metadata;
using KsqlDsl.Modeling; // Added for KafkaIgnoreAttribute and KafkaKeyAttribute
using Xunit;

namespace KsqlDsl.Tests
{
    /// <summary>
    /// Test entities for KafkaIgnore attribute testing
    /// </summary>
    public class OrderEntityWithIgnoredProperties
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; }
        public decimal Amount { get; set; }
        
        [KafkaIgnore]
        public DateTime InternalTimestamp { get; set; }
        
        [KafkaIgnore(Reason = "Debug information, not for production")]
        public string DebugInfo { get; set; }
        
        [KafkaIgnore]
        public bool IsProcessedInternally { get; set; }
        
        public string Region { get; set; }
    }

    public class ProductEntityAllIncluded
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public bool IsActive { get; set; }
        public decimal Price { get; set; }
    }

    public class CustomerEntityAllIgnored
    {
        [KafkaIgnore]
        public int CustomerId { get; set; }
        
        [KafkaIgnore]
        public string CustomerName { get; set; }
        
        [KafkaIgnore]
        public bool IsVerified { get; set; }
    }

    public class EmptyEntity
    {
        // No properties
    }

    public class InheritanceTestEntity : OrderEntityWithIgnoredProperties
    {
        public string AdditionalField { get; set; }
        
        [KafkaIgnore]
        public string AnotherIgnoredField { get; set; }
    }

    /// <summary>
    /// Unit tests for KafkaIgnoreAttribute functionality
    /// </summary>
    public class KafkaIgnoreAttributeTests
    {
        [Fact]
        public void KafkaIgnoreAttribute_BasicFunctionality_Should_Work()
        {
            // Arrange
            var attribute = new KafkaIgnoreAttribute();
            
            // Act & Assert
            Assert.NotNull(attribute);
            Assert.Equal(string.Empty, attribute.Reason);
            Assert.Equal("KafkaIgnore", attribute.ToString());
        }

        [Fact]
        public void KafkaIgnoreAttribute_WithReason_Should_IncludeReasonInToString()
        {
            // Arrange
            var reason = "Test reason";
            var attribute = new KafkaIgnoreAttribute { Reason = reason };
            
            // Act & Assert
            Assert.Equal(reason, attribute.Reason);
            Assert.Equal($"KafkaIgnore: {reason}", attribute.ToString());
        }

        [Fact]
        public void CreateStatement_WithIgnoredProperties_Should_ExcludeIgnoredFields()
        {
            // Arrange
            var entityType = typeof(OrderEntityWithIgnoredProperties);
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream);
            
            // Assert
            var expected = "CREATE STREAM OrderEntityWithIgnoredProperties (OrderId INT, CustomerId VARCHAR, Amount DECIMAL, Region VARCHAR)";
            Assert.Equal(expected, result);
            
            // Verify ignored properties are not included
            Assert.DoesNotContain("InternalTimestamp", result);
            Assert.DoesNotContain("DebugInfo", result);
            Assert.DoesNotContain("IsProcessedInternally", result);
        }

        [Fact]
        public void CreateStatement_WithAllPropertiesIncluded_Should_IncludeAllFields()
        {
            // Arrange
            var entityType = typeof(ProductEntityAllIncluded);
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Table);
            
            // Assert
            var expected = "CREATE TABLE ProductEntityAllIncluded (ProductId INT, ProductName VARCHAR, IsActive BOOLEAN, Price DECIMAL)";
            Assert.Equal(expected, result);
            
            // Verify all properties are included
            Assert.Contains("ProductId", result);
            Assert.Contains("ProductName", result);
            Assert.Contains("IsActive", result);
            Assert.Contains("Price", result);
        }

        [Fact]
        public void CreateStatement_WithAllPropertiesIgnored_Should_GenerateEmptyColumnList()
        {
            // Arrange
            var entityType = typeof(CustomerEntityAllIgnored);
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream);
            
            // Assert
            var expected = "CREATE STREAM CustomerEntityAllIgnored ()";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CreateStatement_WithEmptyEntity_Should_GenerateEmptyColumnList()
        {
            // Arrange
            var entityType = typeof(EmptyEntity);
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Table);
            
            // Assert
            var expected = "CREATE TABLE EmptyEntity ()";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetSchemaProperties_Should_ReturnOnlyNonIgnoredProperties()
        {
            // Arrange
            var entityType = typeof(OrderEntityWithIgnoredProperties);
            
            // Act
            var schemaProperties = KsqlCreateStatementBuilder.GetSchemaProperties(entityType);
            
            // Assert
            Assert.Equal(4, schemaProperties.Length);
            var propertyNames = schemaProperties.Select(p => p.Name).ToArray();
            
            Assert.Contains("OrderId", propertyNames);
            Assert.Contains("CustomerId", propertyNames);
            Assert.Contains("Amount", propertyNames);
            Assert.Contains("Region", propertyNames);
            
            Assert.DoesNotContain("InternalTimestamp", propertyNames);
            Assert.DoesNotContain("DebugInfo", propertyNames);
            Assert.DoesNotContain("IsProcessedInternally", propertyNames);
        }

        [Fact]
        public void GetIgnoredProperties_Should_ReturnOnlyIgnoredProperties()
        {
            // Arrange
            var entityType = typeof(OrderEntityWithIgnoredProperties);
            
            // Act
            var ignoredProperties = KsqlCreateStatementBuilder.GetIgnoredProperties(entityType);
            
            // Assert
            Assert.Equal(3, ignoredProperties.Length);
            var propertyNames = ignoredProperties.Select(p => p.Name).ToArray();
            
            Assert.Contains("InternalTimestamp", propertyNames);
            Assert.Contains("DebugInfo", propertyNames);
            Assert.Contains("IsProcessedInternally", propertyNames);
            
            Assert.DoesNotContain("OrderId", propertyNames);
            Assert.DoesNotContain("CustomerId", propertyNames);
            Assert.DoesNotContain("Amount", propertyNames);
            Assert.DoesNotContain("Region", propertyNames);
        }

        [Fact]
        public void CreateStatement_WithInheritance_Should_HandleIgnoredPropertiesCorrectly()
        {
            // Arrange
            var entityType = typeof(InheritanceTestEntity);
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream);
            
            // Assert
            // Should include inherited non-ignored properties + new non-ignored properties
            Assert.Contains("OrderId", result);
            Assert.Contains("CustomerId", result);
            Assert.Contains("Amount", result);
            Assert.Contains("Region", result);
            Assert.Contains("AdditionalField", result);
            
            // Should exclude inherited ignored properties + new ignored properties
            Assert.DoesNotContain("InternalTimestamp", result);
            Assert.DoesNotContain("DebugInfo", result);
            Assert.DoesNotContain("IsProcessedInternally", result);
            Assert.DoesNotContain("AnotherIgnoredField", result);
        }

        [Fact]
        public void GetSchemaProperties_WithNullEntityType_Should_ThrowArgumentNullException()
        {
            // Arrange
            Type entityType = null;
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                KsqlCreateStatementBuilder.GetSchemaProperties(entityType));
        }

        [Fact]
        public void GetIgnoredProperties_WithNullEntityType_Should_ThrowArgumentNullException()
        {
            // Arrange
            Type entityType = null;
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                KsqlCreateStatementBuilder.GetIgnoredProperties(entityType));
        }

        [Fact]
        public void CreateStatement_WithIgnoredPropertiesAndWithOptions_Should_ExcludeIgnoredFieldsAndIncludeOptions()
        {
            // Arrange
            var entityType = typeof(OrderEntityWithIgnoredProperties);
            var options = new KsqlWithOptions
            {
                TopicName = "orders-topic",
                ValueFormat = "AVRO"
            };
            
            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream, options);
            
            // Assert
            var expected = "CREATE STREAM OrderEntityWithIgnoredProperties (OrderId INT, CustomerId VARCHAR, Amount DECIMAL, Region VARCHAR) WITH (KAFKA_TOPIC='orders-topic', VALUE_FORMAT='AVRO')";
            Assert.Equal(expected, result);
            
            // Verify ignored properties are not included
            Assert.DoesNotContain("InternalTimestamp", result);
            Assert.DoesNotContain("DebugInfo", result);
            Assert.DoesNotContain("IsProcessedInternally", result);
        }

        [Fact]
        public void KafkaIgnoreAttribute_WithMultipleInstances_Should_NotBeAllowed()
        {
            // Arrange & Act
            var attributeUsage = typeof(KafkaIgnoreAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .Cast<AttributeUsageAttribute>()
                .FirstOrDefault();
            
            // Assert
            Assert.NotNull(attributeUsage);
            Assert.False(attributeUsage.AllowMultiple, "KafkaIgnoreAttribute should not allow multiple instances");
            Assert.True(attributeUsage.Inherited, "KafkaIgnoreAttribute should be inherited");
            Assert.Equal(AttributeTargets.Property, attributeUsage.ValidOn);
        }

        [Fact]
        public void CreateStatement_Performance_WithManyIgnoredProperties_Should_BeEfficient()
        {
            // This test ensures that filtering ignored properties doesn't significantly impact performance
            // Arrange
            var entityType = typeof(OrderEntityWithIgnoredProperties);
            
            // Act - Measure performance over multiple calls
            var start = DateTime.UtcNow;
            for (int i = 0; i < 1000; i++)
            {
                KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream);
            }
            var duration = DateTime.UtcNow - start;
            
            // Assert - Should complete within reasonable time (less than 1 second for 1000 calls)
            Assert.True(duration.TotalMilliseconds < 1000, $"Performance test failed: took {duration.TotalMilliseconds}ms for 1000 calls");
        }
    }
}