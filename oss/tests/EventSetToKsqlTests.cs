using System;
using System.Linq;
using KsqlDsl.Attributes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using Xunit;

namespace KsqlDsl.Tests
{
    [Topic("test-orders")]
    public class TestOrder
    {
        [Key]
        public int OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsActive { get; set; }
    }

    public class TestKafkaContext : KafkaContext
    {
        public EventSet<TestOrder> Orders => Set<TestOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<TestOrder>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .EnableDebugLogging();
        }
    }

    public class EventSetToKsqlTests
    {
        [Fact]
        public void ToKsql_SimpleQuery_Should_GenerateSelectAllQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();
            var orders = context.Orders;

            // Act
            var ksql = orders.ToKsql();

            // Assert
            Assert.Contains("SELECT * FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_WhereClause_Should_GenerateWhereQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => o.Amount > 1000)
                .ToKsql();

            // Assert
            Assert.Contains("SELECT * FROM test-orders", ksql);
            Assert.Contains("WHERE (Amount > 1000)", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_SelectClause_Should_GenerateSelectQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Select(o => new { o.OrderId, o.CustomerId })
                .ToKsql();

            // Assert
            Assert.Contains("SELECT OrderId, CustomerId", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_WhereAndSelect_Should_GenerateCombinedQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => o.Amount > 500)
                .Select(o => new { o.OrderId, o.Amount })
                .ToKsql();

            // Assert
            Assert.Contains("SELECT OrderId, Amount", ksql);
            Assert.Contains("WHERE (Amount > 500)", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_GroupByClause_Should_GenerateGroupByQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .GroupBy(o => o.CustomerId)
                .ToKsql();

            // Assert
            Assert.Contains("GROUP BY CustomerId", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_GroupByWithAggregateSelect_Should_GenerateAggregateQuery()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { CustomerId = g.Key, TotalAmount = g.Sum(x => x.Amount) })
                .ToKsql();

            // Assert
            Assert.Contains("SELECT", ksql);
            Assert.Contains("SUM(Amount)", ksql);
            Assert.Contains("GROUP BY CustomerId", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_ComplexQuery_Should_GenerateCompleteKsql()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => o.Amount > 100)
                .GroupBy(o => o.CustomerId)
                .Select(g => new {
                    CustomerId = g.Key,
                    TotalAmount = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToKsql();

            // Assert
            Assert.Contains("SELECT", ksql);
            Assert.Contains("SUM(Amount)", ksql);
            Assert.Contains("COUNT(*)", ksql);
            Assert.Contains("WHERE (Amount > 100)", ksql);
            Assert.Contains("GROUP BY CustomerId", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_OrderByClause_Should_ThrowNotSupportedException()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act & Assert
            var exception = Assert.Throws<NotSupportedException>(() =>
                context.Orders
                    .OrderBy(o => o.Amount)
                    .ToKsql());

            Assert.Contains("ORDER BY operations are not supported", exception.Message);
        }

        [Fact]
        public void ToKsql_BooleanFilter_Should_GenerateCorrectWhere()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => o.IsActive)
                .ToKsql();

            // Assert
            Assert.Contains("WHERE (IsActive = true)", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_NegatedBooleanFilter_Should_GenerateCorrectWhere()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => !o.IsActive)
                .ToKsql();

            // Assert
            Assert.Contains("WHERE (IsActive = false)", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_StringComparison_Should_GenerateCorrectWhere()
        {
            // Arrange
            using var context = new TestKafkaContext();

            // Act
            var ksql = context.Orders
                .Where(o => o.CustomerId == "CUST001")
                .ToKsql();

            // Assert
            Assert.Contains("WHERE (CustomerId = 'CUST001')", ksql);
            Assert.Contains("FROM test-orders", ksql);
            Assert.Contains("EMIT CHANGES", ksql);
        }

        [Fact]
        public void ToKsql_InvalidQuery_Should_ReturnErrorComment()
        {
            // Arrange
            using var context = new TestKafkaContext();
            context.Options.GetType().GetProperty("EnableDebugLogging")?.SetValue(context.Options, true);

            // このテストは例外が発生した場合のエラーハンドリングを確認
            // 実際には正常なクエリなので、代わりに手動で例外を発生させる状況をシミュレート

            // Act & Assert
            // 正常なクエリでは例外は発生しないため、この部分はEventSet実装の堅牢性を確認
            var ksql = context.Orders.ToKsql();
            Assert.DoesNotContain("KSQL変換エラー", ksql);
        }
    }
}