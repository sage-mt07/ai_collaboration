using System;
using System.Linq;
using System.Linq.Expressions;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Ksql;
using KsqlDsl.Metadata;
using Xunit;

namespace KsqlDsl.Tests
{
    // Test entities for CREATE statement testing
    public class SimpleProduct
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public bool IsActive { get; set; }
    }

    public class DetailedOrder
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; }
        
        [DecimalPrecision(18, 4)]
        public decimal Amount { get; set; }
        
        public DateTime OrderDate { get; set; }
        public double Score { get; set; }
        public bool IsProcessed { get; set; }
        public Guid CorrelationId { get; set; }
        public int? OptionalQuantity { get; set; }
    }

    public class KsqlCreateStatementBuilderTests
    {
        [Fact]
        public void CreateStreamStatement_Should_GenerateValidKsql()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);
            var streamType = StreamTableType.Stream;

            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, streamType);

            // Assert
            var expected = "CREATE STREAM SimpleProduct (ProductId INT, ProductName VARCHAR, IsActive BOOLEAN)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CreateTableStatement_Should_GenerateValidKsql()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);
            var streamType = StreamTableType.Table;

            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, streamType);

            // Assert
            var expected = "CREATE TABLE SimpleProduct (ProductId INT, ProductName VARCHAR, IsActive BOOLEAN)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CreateTableStatement_WithOptions_Should_GenerateKsqlWithClause()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);
            var streamType = StreamTableType.Table;
            var options = new KsqlWithOptions
            {
                TopicName = "products-topic",
                KeyFormat = "JSON",
                ValueFormat = "AVRO",
                Partitions = 3,
                Replicas = 2
            };

            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, streamType, options);

            // Assert
            var expected = "CREATE TABLE SimpleProduct (ProductId INT, ProductName VARCHAR, IsActive BOOLEAN) WITH (KAFKA_TOPIC='products-topic', KEY_FORMAT='JSON', VALUE_FORMAT='AVRO', PARTITIONS=3, REPLICAS=2)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CreateStreamStatement_WithComplexTypes_Should_GenerateValidKsql()
        {
            // Arrange
            var entityType = typeof(DetailedOrder);
            var streamType = StreamTableType.Stream;

            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, streamType);

            // Assert
            var expected = "CREATE STREAM DetailedOrder (OrderId INT, CustomerId VARCHAR, Amount DECIMAL(18, 4), OrderDate TIMESTAMP, Score DOUBLE, IsProcessed BOOLEAN, CorrelationId VARCHAR, OptionalQuantity INT)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void CreateStatement_WithNullEntityType_Should_ThrowArgumentNullException()
        {
            // Arrange
            Type entityType = null;
            var streamType = StreamTableType.Stream;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                KsqlCreateStatementBuilder.BuildCreateStatement(entityType, streamType));
        }

        [Fact]
        public void CreateStatement_WithInvalidStreamTableType_Should_ThrowArgumentException()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);
            var invalidType = (StreamTableType)999; // Invalid enum value

            // Act & Assert
            Assert.Throws<ArgumentException>(() => 
                KsqlCreateStatementBuilder.BuildCreateStatement(entityType, invalidType));
        }

        [Fact]
        public void KsqlWithOptions_BuildWithClause_Should_HandleAllOptions()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "test-topic",
                KeyFormat = "AVRO",
                ValueFormat = "JSON",
                Partitions = 6,
                Replicas = 3
            };

            // Act
            var result = options.BuildWithClause();

            // Assert
            Assert.StartsWith(" WITH (", result);
            Assert.Contains("KAFKA_TOPIC='test-topic'", result);
            Assert.Contains("KEY_FORMAT='AVRO'", result);
            Assert.Contains("VALUE_FORMAT='JSON'", result);
            Assert.Contains("PARTITIONS=6", result);
            Assert.Contains("REPLICAS=3", result);
            Assert.EndsWith(")", result);
        }

        [Fact]
        public void InferStreamTableType_BasicFunctionality_Should_Work()
        {
            // Arrange - Simple test to verify basic functionality works
            Expression<Func<SimpleProduct, bool>> simpleExpr = p => p.IsActive;

            // Act
            var result = KsqlCreateStatementBuilder.InferStreamTableType(simpleExpr.Body);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(StreamTableType.Stream, result.InferredType);
            Assert.False(result.IsExplicitlyDefined);
            Assert.False(string.IsNullOrEmpty(result.Reason));
        }

        [Fact]
        public void BuildCreateStatement_BasicFunctionality_Should_Work()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);

            // Act
            var streamResult = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Stream);
            var tableResult = KsqlCreateStatementBuilder.BuildCreateStatement(entityType, StreamTableType.Table);

            // Assert
            Assert.StartsWith("CREATE STREAM SimpleProduct", streamResult);
            Assert.StartsWith("CREATE TABLE SimpleProduct", tableResult);
            Assert.Contains("ProductId INT", streamResult);
            Assert.Contains("ProductName VARCHAR", streamResult);
            Assert.Contains("IsActive BOOLEAN", streamResult);
        }

        [Fact]
        public void BuildCreateStatementWithInference_SimpleQuery_Should_GenerateStream()
        {
            // Arrange
            var entityType = typeof(SimpleProduct);
            Expression<Func<SimpleProduct, bool>> simpleExpr = p => p.IsActive;

            // Act
            var result = KsqlCreateStatementBuilder.BuildCreateStatementWithInference(entityType, simpleExpr.Body);

            // Assert
            var expected = "CREATE STREAM SimpleProduct (ProductId INT, ProductName VARCHAR, IsActive BOOLEAN)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void InferStreamTableType_WithNullExpression_Should_ThrowArgumentNullException()
        {
            // Arrange
            Expression nullExpression = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                KsqlCreateStatementBuilder.InferStreamTableType(nullExpression));
        }
    }

    // Mock extension methods for testing explicit AsStream/AsTable markers and LINQ operations
    public static class MockLinqExtensions
    {
        public static IQueryable<T> AsStream<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsTable<T>(this IQueryable<T> source) => source;

        public static IQueryable<IGrouping<TKey, T>> GroupBy<T, TKey>(
            IQueryable<T> source,
            Expression<Func<T, TKey>> keySelector) => null;

        public static TResult Sum<T, TResult>(
            IGrouping<string, T> source,
            Expression<Func<T, TResult>> selector) => default;

        public static int Count<T>(
            IGrouping<string, T> source) => 0;
    }
}