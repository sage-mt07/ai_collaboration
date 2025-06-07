
using System;
using System.Linq;
using System.Linq.Expressions;
using KsqlDsl;
using Xunit;

public class Order
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

public class KsqlTranslationTests
{
    [Fact]
    public void SelectProjection_Should_GenerateExpectedKsql()
    {
        Expression<Func<Order, object>> expr = o => new { o.OrderId, o.Amount };
        var result = new KsqlProjectionBuilder().Build(expr.Body);
        Assert.Equal("SELECT OrderId, Amount", result);
    }
    [Fact]
    public void WhereClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<Order, bool>> expr = o => o.Amount > 1000 && o.CustomerId == "C001";
        var result = new KsqlConditionBuilder().Build(expr.Body);
        Assert.Equal("WHERE ((Amount > 1000) AND (CustomerId = 'C001'))", result);
    }

    [Fact]
    public void GroupByClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<Order, object>> expr = o => new { o.CustomerId, o.Region };
        var result =  KsqlGroupByBuilder.Build(expr.Body);
        Assert.Equal("GROUP BY CustomerId, Region", result);
    }

    [Fact]
    public void AggregateClause_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, object>> expr = g => new { Total = g.Sum(x => x.Amount) };
        var result =  KsqlAggregateBuilder.Build(expr.Body);
        Assert.Equal("SELECT SUM(Amount) AS Total", result);
    }

    [Fact]
    public void LatestByOffset_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, object>> expr = g => new { LatestAmount = g.LatestByOffset(x => x.Amount) };
        var result =  KsqlAggregateBuilder.Build(expr.Body);
        Assert.Equal("SELECT LATEST_BY_OFFSET(Amount) AS LatestAmount", result);
    }

    [Fact]
    public void WindowClause_Should_GenerateExpectedKsql()
    {
        var result = new KsqlWindowBuilder().Build("TumblingWindow.Of(TimeSpan.FromMinutes(1))");
        Assert.Equal("WINDOW TUMBLING (SIZE 1 MINUTES)", result);
    }
    [Fact]
    public void JoinClause_SingleKey_Should_GenerateExpectedKsql()
    {
        Expression<Func<IQueryable<Order>, IQueryable<Customer>, IQueryable<object>>> expr =
            (orders, customers) =>
                orders.Join(customers,
                            o => o.CustomerId,
                            c => c.CustomerId,
                            (o, c) => new { o.OrderId, c.CustomerName });

        var result = new KsqlJoinBuilder().Build(expr.Body);
        Assert.Equal("SELECT o.OrderId, c.CustomerName FROM Order o JOIN Customer c ON o.CustomerId = c.CustomerId", result);
    }

    [Fact]
    public void JoinClause_CompositeKey_Should_GenerateExpectedKsql()
    {
        Expression<Func<IQueryable<Order>, IQueryable<Customer>, IQueryable<object>>> expr =
            (orders, customers) =>
                orders.Join(customers,
                            o => new { o.CustomerId, o.Region },
                            c => new { c.CustomerId, c.Region },
                            (o, c) => new { o.OrderId });

        var result = new KsqlJoinBuilder().Build(expr.Body);
        Assert.Equal("SELECT o.OrderId FROM Order o JOIN Customer c ON o.CustomerId = c.CustomerId AND o.Region = c.Region", result);
    }

}
public class Customer
{
    public string CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string Region { get; set; }
}