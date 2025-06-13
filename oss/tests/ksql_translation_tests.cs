using System;
using System.Linq;
using System.Linq.Expressions;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl;
using KsqlDsl.Ksql;
using Xunit;

public class OrderStringId
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }

    [DecimalPrecision(18, 4)]
    public decimal Amount { get; set; }

    public string Region { get; set; }
    public bool IsActive { get; set; }
    public double Score { get; set; }
    public decimal Price { get; set; }

    [DateTimeFormat(Format = "yyyy-MM-dd'T'HH:mm:ss.SSS", Region = "Asia/Tokyo")]
    public DateTime OrderTime { get; set; }
}

public class CustomerStringId
{
    public string CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string Region { get; set; }
}
public class KsqlTranslationStringIdTests
{
    [Fact]
    public void SelectProjection_Should_GenerateExpectedKsql()
    {
        Expression<Func<OrderStringId, object>> expr = o => new { o.OrderId, o.Amount };
        var result = new KsqlProjectionBuilder().Build(expr.Body);
        Assert.Equal("SELECT OrderId, Amount", result);
    }

    [Fact]
    public void WhereClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<OrderStringId, bool>> expr = o => o.Amount > 1000 && o.CustomerId == "C001";
        var result = new KsqlConditionBuilder().Build(expr.Body);
        Assert.Equal("WHERE ((Amount > 1000) AND (CustomerId = 'C001'))", result);
    }

    [Fact]
    public void GroupByClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<OrderStringId, object>> expr = o => new { o.CustomerId, o.Region };
        var result = KsqlGroupByBuilder.Build(expr.Body);
        Assert.Equal("GROUP BY CustomerId, Region", result);
    }

    [Fact]
    public void AggregateClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, OrderStringId>, object>> expr = g => new { Total = g.Sum(x => x.Amount) };
        var result = KsqlAggregateBuilder.Build(expr.Body);
        Assert.Equal("SELECT SUM(Amount) AS Total", result);
    }

    [Fact]
    public void LatestByOffset_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, OrderStringId>, object>> expr = g => new { LatestAmount = g.LatestByOffset(x => x.Amount) };
        var result = KsqlAggregateBuilder.Build(expr.Body);
        Assert.Equal("SELECT LATEST_BY_OFFSET(Amount) AS LatestAmount", result);
    }

    [Fact]
    public void TumblingWindowClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromMinutes(1));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 1 MINUTES)", result);
    }

    [Fact]
    public void TumblingWindowClause_WithAllOptions_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(5))
            .Retention(TimeSpan.FromHours(2))
            .GracePeriod(TimeSpan.FromSeconds(10))
            .EmitFinal();
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES, RETENTION 2 HOURS, GRACE PERIOD 10 SECONDS) EMIT FINAL", result);
    }

    [Fact]
    public void HoppingWindowClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromMinutes(10))
            .AdvanceBy(TimeSpan.FromMinutes(5));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES)", result);
    }

    [Fact]
    public void HoppingWindowClause_WithAllOptions_Should_GenerateExpectedKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromMinutes(10))
            .AdvanceBy(TimeSpan.FromMinutes(5))
            .Retention(TimeSpan.FromHours(1))
            .GracePeriod(TimeSpan.FromSeconds(30))
            .EmitFinal();
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES, RETENTION 1 HOURS, GRACE PERIOD 30 SECONDS) EMIT FINAL", result);
    }

    [Fact]
    public void SessionWindowClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<ISessionWindow>> expr = () => Window.SessionWindow().Gap(TimeSpan.FromSeconds(30));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW SESSION (GAP 30 SECONDS)", result);
    }

    [Fact]
    public void HavingClause_Sum_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, OrderStringId>, bool>> expr = g => g.Sum(x => x.Amount) > 1000;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (SUM(Amount) > 1000)", result);
    }

    [Fact]
    public void JoinClause_SingleKey_Should_GenerateExpectedKsql()
    {
        Expression<Func<IQueryable<OrderStringId>, IQueryable<CustomerStringId>, IQueryable<object>>> expr =
            (orders, customers) =>
                orders.Join(customers,
                            o => o.CustomerId,
                            c => c.CustomerId,
                            (o, c) => new { o.OrderId, c.CustomerName });

        var result = new KsqlJoinBuilder().Build(expr.Body);
        Assert.Equal("SELECT o.OrderId, c.CustomerName FROM OrderStringId o JOIN CustomerStringId c ON o.CustomerId = c.CustomerId", result);
    }

    [Fact]
    public void JoinClause_CompositeKey_Should_GenerateExpectedKsql()
    {
        Expression<Func<IQueryable<OrderStringId>, IQueryable<CustomerStringId>, IQueryable<object>>> expr =
            (orders, customers) =>
                orders.Join(customers,
                            o => new { o.CustomerId, o.Region },
                            c => new { c.CustomerId, c.Region },
                            (o, c) => new { o.OrderId });

        var result = new KsqlJoinBuilder().Build(expr.Body);
        Assert.Equal("SELECT o.OrderId FROM OrderStringId o JOIN CustomerStringId c ON o.CustomerId = c.CustomerId AND o.Region = c.Region", result);
    }

    [Fact]
    public void SelectProjection_WithUnaryExpression_Should_GenerateExpectedKsql()
    {
        // Arrange - UnaryExpression (Convert) が挿入される LINQ 式
        Expression<Func<OrderStringId, object>> expr = o => new { o.OrderId, o.CustomerId };

        // Act
        var result = new KsqlProjectionBuilder().Build(expr.Body);

        // Assert
        Assert.Equal("SELECT OrderId, CustomerId", result);
    }

    [Fact]
    public void SelectProjection_WithUnaryExpressionSingleProperty_Should_GenerateExpectedKsql()
    {
        // Arrange - 単一プロパティでUnaryExpressionが挿入される場合
        Expression<Func<OrderStringId, object>> expr = o => new { o.OrderId };

        // Act
        var result = new KsqlProjectionBuilder().Build(expr.Body);

        // Assert
        Assert.Equal("SELECT OrderId", result);
    }

    [Fact]
    public void SelectProjection_WithUnaryExpressionAndAlias_Should_GenerateExpectedKsql()
    {
        // Arrange - エイリアス付きUnaryExpressionが挿入される場合
        Expression<Func<OrderStringId, object>> expr = o => new { Id = o.OrderId, CustomerStringId = o.CustomerId };

        // Act
        var result = new KsqlProjectionBuilder().Build(expr.Body);

        // Assert - 実際のエイリアス名に合わせて修正
        Assert.Equal("SELECT OrderId AS Id, CustomerId AS CustomerStringId", result);
    }

    [Fact]
    public void SelectProjection_WithUnaryExpressionMultipleProperties_Should_GenerateExpectedKsql()
    {
        // Arrange - 複数プロパティでUnaryExpressionが挿入される場合
        Expression<Func<OrderStringId, object>> expr = o => new { o.OrderId, o.CustomerId, o.Amount, o.Region };

        // Act
        var result = new KsqlProjectionBuilder().Build(expr.Body);

        // Assert
        Assert.Equal("SELECT OrderId, CustomerId, Amount, Region", result);
    }
}