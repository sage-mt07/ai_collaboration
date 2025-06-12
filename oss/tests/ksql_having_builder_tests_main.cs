using System;
using System.Linq;
using System.Linq.Expressions;
using KsqlDsl;
using KsqlDsl.Ksql;
using KsqlDsl.Tests;
using Xunit;

public class KsqlHavingBuilderTests
{
    [Fact]
    public void HavingClause_Sum_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Sum(x => x.Amount) > 1000;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (SUM(Amount) > 1000)", result);
    }

    [Fact]
    public void HavingClause_Count_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Count() >= 5;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (COUNT(*) >= 5)", result);
    }

    [Fact]
    public void HavingClause_Max_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Max(x => x.Score) < 100.0;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (MAX(Score) < 100)", result);
    }

    [Fact]
    public void HavingClause_Min_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Min(x => x.Price) >= 10.5m;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (MIN(Price) >= 10.5)", result);
    }

    [Fact]
    public void HavingClause_ComplexCondition_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = 
            g => g.Sum(x => x.Amount) > 1000 && g.Count() >= 3;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING ((SUM(Amount) > 1000) AND (COUNT(*) >= 3))", result);
    }

    [Fact]
    public void HavingClause_OrCondition_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = 
            g => g.Sum(x => x.Amount) > 1000 || g.Max(x => x.Score) > 95.0;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING ((SUM(Amount) > 1000) OR (MAX(Score) > 95))", result);
    }

    [Fact]
    public void HavingClause_EqualityComparison_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Count() == 10;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (COUNT(*) = 10)", result);
    }

    [Fact]
    public void HavingClause_NotEqualComparison_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = g => g.Sum(x => x.Amount) != 0;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (SUM(Amount) <> 0)", result);
    }

    [Fact]
    public void HavingClause_LatestByOffset_Should_GenerateExpectedKsql()
    {
        Expression<Func<IGrouping<string, Order>, bool>> expr = 
            g => g.LatestByOffset(x => x.Amount) > 500;
        var result = new KsqlHavingBuilder().Build(expr.Body);
        Assert.Equal("HAVING (LATEST_BY_OFFSET(Amount) > 500)", result);
    }
}