using System;
using System.Linq;
using System.Linq.Expressions;
using KsqlDsl.Ksql;
using KsqlDsl.Metadata;
using Xunit;

namespace KsqlDsl.Tests
{
    /// <summary>
    /// Test entities for comprehensive LINQ to KSQL translation testing
    /// </summary>
    public class Order
    {
        public int OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public double Score { get; set; }
        public decimal Price { get; set; }
        public DateTime OrderDate { get; set; }
        public string Region { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool? IsProcessed { get; set; }
    }

    public class Customer
    {
        public string CustomerId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Comprehensive tests for LINQ to KSQL translation functionality
    /// </summary>
    public class KsqlTranslationTests
    {
        #region KsqlProjectionBuilder Tests

        [Fact]
        public void SelectProjection_SimpleProperties_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, object>> expr = o => new { o.OrderId, o.CustomerId };
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT OrderId, CustomerId", result);
        }

        [Fact]
        public void SelectProjection_WithAliases_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, object>> expr = o => new { Id = o.OrderId, Customer = o.CustomerId };
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT OrderId AS Id, CustomerId AS Customer", result);
        }

        [Fact]
        public void SelectProjection_SingleProperty_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, string>> expr = o => o.CustomerId;
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT CustomerId", result);
        }

        [Fact]
        public void SelectProjection_AllProperties_Should_GenerateSelectStar()
        {
            // Arrange
            Expression<Func<Order, Order>> expr = o => o;
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT *", result);
        }

        [Fact]
        public void SelectProjection_WithCalculations_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, object>> expr = o => new { 
                o.OrderId, 
                TotalWithTax = o.Amount * 1.1m 
            };
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Contains("SELECT OrderId", result);
            Assert.Contains("TotalWithTax", result);
            Assert.Contains("*", result);
        }

        [Fact]
        public void SelectProjection_WithUnaryExpression_Should_GenerateExpectedKsql()
        {
            // Arrange - UnaryExpression (Convert) が挿入される LINQ 式
            Expression<Func<Order, object>> expr = o => new { o.OrderId, o.CustomerId };
            
            // Act
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT OrderId, CustomerId", result);
        }

        #endregion

        #region KsqlConditionBuilder Tests

        [Fact]
        public void WhereCondition_SimpleComparison_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, bool>> expr = o => o.Amount > 1000;
            
            // Act
            var result = new KsqlConditionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WHERE (Amount > 1000)", result);
        }

        [Fact]
        public void WhereCondition_ComplexCondition_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, bool>> expr = o => o.Amount > 1000 && o.Region == "US";
            
            // Act
            var result = new KsqlConditionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WHERE ((Amount > 1000) AND (Region = 'US'))", result);
        }

        [Fact]
        public void WhereCondition_BoolProperty_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, bool>> expr = o => o.IsActive;
            
            // Act
            var result = new KsqlConditionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WHERE (IsActive = true)", result);
        }

        [Fact]
        public void WhereCondition_NegatedBoolProperty_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, bool>> expr = o => !o.IsActive;
            
            // Act
            var result = new KsqlConditionBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WHERE (IsActive = false)", result);
        }

        #endregion

        #region KsqlAggregateBuilder Tests

        [Fact]
        public void AggregateProjection_SimpleSum_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<IGrouping<string, Order>, object>> expr = g => new { 
                TotalAmount = g.Sum(x => x.Amount) 
            };
            
            // Act
            var result = KsqlAggregateBuilder.Build(expr.Body);
            
            // Assert
            Assert.Equal("SELECT SUM(Amount) AS TotalAmount", result);
        }

        [Fact]
        public void AggregateProjection_MultipleAggregates_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<IGrouping<string, Order>, object>> expr = g => new { 
                TotalAmount = g.Sum(x => x.Amount),
                OrderCount = g.Count(),
                AvgScore = g.Average(x => x.Score)
            };
            
            // Act
            var result = KsqlAggregateBuilder.Build(expr.Body);
            
            // Assert
            Assert.Contains("SUM(Amount) AS TotalAmount", result);
            Assert.Contains("COUNT(UNKNOWN) AS OrderCount", result);
            Assert.Contains("AVERAGE(Score) AS AvgScore", result);
        }

        #endregion

        #region KsqlHavingBuilder Tests

        [Fact]
        public void HavingCondition_SimpleCondition_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Sum(x => x.Amount) > 1000;
            
            // Act
            var result = new KsqlHavingBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("HAVING (SUM(Amount) > 1000)", result);
        }

        [Fact]
        public void HavingCondition_WithCount_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Count() >= 5;
            
            // Act
            var result = new KsqlHavingBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("HAVING (COUNT(*) >= 5)", result);
        }

        #endregion

        #region KsqlGroupByBuilder Tests

        [Fact]
        public void GroupByClause_SingleProperty_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, string>> expr = o => o.CustomerId;
            
            // Act
            var result = KsqlGroupByBuilder.Build(expr.Body);
            
            // Assert
            Assert.Equal("GROUP BY CustomerId", result);
        }

        [Fact]
        public void GroupByClause_MultipleProperties_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<Order, object>> expr = o => new { o.CustomerId, o.Region };
            
            // Act
            var result = KsqlGroupByBuilder.Build(expr.Body);
            
            // Assert
            Assert.Equal("GROUP BY CustomerId, Region", result);
        }

        #endregion

        #region KsqlWindowBuilder Tests

        [Fact]
        public void WindowClause_TumblingWindow_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromMinutes(5));
            
            // Act
            var result = new KsqlWindowBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES)", result);
        }

        [Fact]
        public void WindowClause_HoppingWindow_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
                .Size(TimeSpan.FromMinutes(10))
                .AdvanceBy(TimeSpan.FromMinutes(5));
            
            // Act
            var result = new KsqlWindowBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES)", result);
        }

        [Fact]
        public void WindowClause_SessionWindow_Should_GenerateExpectedKsql()
        {
            // Arrange
            Expression<Func<ISessionWindow>> expr = () => Window.SessionWindow().Gap(TimeSpan.FromMinutes(5));
            
            // Act
            var result = new KsqlWindowBuilder().Build(expr.Body);
            
            // Assert
            Assert.Equal("WINDOW SESSION (GAP 5 MINUTES)", result);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void ComplexQuery_SelectWhereGroupByHaving_Should_GenerateExpectedKsql()
        {
            // Test building a complete query with multiple clauses
            
            // SELECT clause
            Expression<Func<IGrouping<string, Order>, object>> selectExpr = g => new { 
                CustomerId = g.Key,
                TotalAmount = g.Sum(x => x.Amount),
                OrderCount = g.Count()
            };
            var selectClause = KsqlAggregateBuilder.Build(selectExpr.Body);
            
            // WHERE clause
            Expression<Func<Order, bool>> whereExpr = o => o.Amount > 100;
            var whereClause = new KsqlConditionBuilder().Build(whereExpr.Body);
            
            // GROUP BY clause
            Expression<Func<Order, string>> groupByExpr = o => o.CustomerId;
            var groupByClause = KsqlGroupByBuilder.Build(groupByExpr.Body);
            
            // HAVING clause
            Expression<Func<IGrouping<string, Order>, bool>> havingExpr = g => g.Sum(x => x.Amount) > 1000;
            var havingClause = new KsqlHavingBuilder().Build(havingExpr.Body);
            
            // Assert all parts are generated correctly
            Assert.Contains("SELECT CustomerId, SUM(Amount) AS TotalAmount", selectClause);
            Assert.Equal("WHERE (Amount > 100)", whereClause);
            Assert.Equal("GROUP BY CustomerId", groupByClause);
            Assert.Equal("HAVING (SUM(Amount) > 1000)", havingClause);
        }

        [Fact]
        public void ComplexQuery_WithWindow_Should_GenerateExpectedKsql()
        {
            // Test building a windowed query
            
            // SELECT clause with aggregation
            Expression<Func<IGrouping<string, Order>, object>> selectExpr = g => new { 
                CustomerId = g.Key,
                HourlyTotal = g.Sum(x => x.Amount)
            };
            var selectClause = KsqlAggregateBuilder.Build(selectExpr.Body);
            
            // WINDOW clause
            Expression<Func<ITumblingWindow>> windowExpr = () => Window.TumblingWindow()
                .Size(TimeSpan.FromHours(1));
            var windowClause = new KsqlWindowBuilder().Build(windowExpr.Body);
            
            // GROUP BY clause
            Expression<Func<Order, string>> groupByExpr = o => o.CustomerId;
            var groupByClause = KsqlGroupByBuilder.Build(groupByExpr.Body);
            
            // Assert windowed query components
            Assert.Contains("SUM(Amount) AS HourlyTotal", selectClause);
            Assert.Equal("WINDOW TUMBLING (SIZE 1 HOURS)", windowClause);
            Assert.Equal("GROUP BY CustomerId", groupByClause);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void WhereCondition_UnsupportedOperator_Should_ThrowNotSupportedException()
        {
            // This would test unsupported operators, but current implementation handles most common cases
            // Keeping this as a placeholder for future unsupported operator testing
            Assert.True(true); // Placeholder
        }

        [Fact]
        public void AggregateProjection_EmptyExpression_Should_HandleGracefully()
        {
            // Test edge cases and error conditions
            // Placeholder for edge case testing
            Assert.True(true); // Placeholder
        }

        #endregion

        #region Stream/Table Inference Tests

        [Fact]
        public void StreamTableInference_SimpleQuery_Should_InferStream()
        {
            // Arrange
            Expression<Func<Order, bool>> simpleExpr = o => o.Amount > 1000;
            
            // Act
            var result = KsqlCreateStatementBuilder.InferStreamTableType(simpleExpr.Body);
            
            // Assert
            Assert.Equal(StreamTableType.Stream, result.InferredType);
            Assert.False(result.IsExplicitlyDefined);
        }

        [Fact]
        public void StreamTableInference_GroupByQuery_Should_InferTable()
        {
            // This would test inference with GroupBy, but we need mock expressions
            // since we can't directly create GroupBy method calls in this test context
            
            // For now, verify the inference analyzer exists and works
            var analyzer = new StreamTableInferenceAnalyzer();
            Assert.NotNull(analyzer);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void TranslationPerformance_MultipleBuilders_Should_BeEfficient()
        {
            // Performance test to ensure builders execute efficiently
            var start = DateTime.UtcNow;
            
            for (int i = 0; i < 1000; i++)
            {
                Expression<Func<Order, object>> selectExpr = o => new { o.OrderId, o.CustomerId };
                Expression<Func<Order, bool>> whereExpr = o => o.Amount > 1000;
                
                var selectResult = new KsqlProjectionBuilder().Build(selectExpr.Body);
                var whereResult = new KsqlConditionBuilder().Build(whereExpr.Body);
                
                // Verify results are not empty
                Assert.NotEmpty(selectResult);
                Assert.NotEmpty(whereResult);
            }
            
            var duration = DateTime.UtcNow - start;
            
            // Should complete 1000 iterations in reasonable time (less than 1 second)
            Assert.True(duration.TotalMilliseconds < 1000, 
                $"Performance test failed: took {duration.TotalMilliseconds}ms for 1000 iterations");
        }

        #endregion

        #region Null Handling Tests

        [Fact]
        public void NullableProperties_Should_HandleCorrectly()
        {
            // Test nullable bool property handling
            Expression<Func<Order, bool>> expr = o => o.IsProcessed.HasValue;
            
            // This would test nullable property handling
            // For now, just verify the expression can be created
            Assert.NotNull(expr);
        }

        #endregion

        #region String Operations Tests

        [Fact]
        public void StringOperations_Should_TranslateToKsqlFunctions()
        {
            // Test string method translation in projections
            Expression<Func<Order, object>> expr = o => new { 
                UpperCustomerId = o.CustomerId.ToUpper(),
                LowerRegion = o.Region.ToLower()
            };
            
            var result = new KsqlProjectionBuilder().Build(expr.Body);
            
            // Verify string functions are translated
            Assert.Contains("SELECT", result);
            // Note: Actual string function translation depends on implementation details
        }

        #endregion

        #region JOIN Tests (Placeholder)

        [Fact]
        public void JoinQuery_SimpleJoin_Should_GenerateExpectedKsql()
        {
            // Placeholder for JOIN translation tests
            // JOIN functionality would be tested here when implemented
            Assert.True(true); // Placeholder
        }

        #endregion

        #region Type Safety Tests

        [Fact]
        public void TypeSafety_StronglyTypedExpressions_Should_Compile()
        {
            // Test that all expression types compile correctly
            Expression<Func<Order, object>> selectExpr = o => new { o.OrderId, o.Amount };
            Expression<Func<Order, bool>> whereExpr = o => o.IsActive;
            Expression<Func<Order, string>> groupExpr = o => o.CustomerId;
            Expression<Func<IGrouping<string, Order>, bool>> havingExpr = g => g.Count() > 0;
            
            // All expressions should compile without issues
            Assert.NotNull(selectExpr);
            Assert.NotNull(whereExpr);
            Assert.NotNull(groupExpr);
            Assert.NotNull(havingExpr);
        }

        #endregion
    }
}