using System;
using System.Linq.Expressions;
using KsqlDsl;
using KsqlDsl.Ksql;
using Xunit;

public class WindowClauseTests
{
    [Fact]
    public void TumblingWindow_WithMinutes_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromMinutes(1));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 1 MINUTES)", result);
    }

    [Fact]
    public void TumblingWindow_WithHours_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromHours(2));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 2 HOURS)", result);
    }

    [Fact]
    public void TumblingWindow_WithAllOptions_Should_GenerateExpectedKsql()
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
    public void TumblingWindow_WithRetentionOnly_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(5))
            .Retention(TimeSpan.FromHours(1));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES, RETENTION 1 HOURS)", result);
    }

    [Fact]
    public void TumblingWindow_WithGracePeriodOnly_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(5))
            .GracePeriod(TimeSpan.FromSeconds(30));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES, GRACE PERIOD 30 SECONDS)", result);
    }

    [Fact]
    public void TumblingWindow_WithEmitFinalOnly_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(5))
            .EmitFinal();
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES) EMIT FINAL", result);
    }

    [Fact]
    public void HoppingWindow_WithSizeAndAdvanceBy_Should_GenerateExpectedKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromMinutes(10))
            .AdvanceBy(TimeSpan.FromMinutes(5));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES)", result);
    }

    [Fact]
    public void HoppingWindow_WithAllOptions_Should_GenerateExpectedKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromMinutes(10))
            .AdvanceBy(TimeSpan.FromMinutes(5))
            .Retention(TimeSpan.FromHours(3))
            .GracePeriod(TimeSpan.FromSeconds(15))
            .EmitFinal();
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES, RETENTION 3 HOURS, GRACE PERIOD 15 SECONDS) EMIT FINAL", result);
    }

    [Fact]
    public void HoppingWindow_WithDifferentUnits_Should_GenerateExpectedKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromHours(1))
            .AdvanceBy(TimeSpan.FromMinutes(30));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 1 HOURS, ADVANCE BY 30 MINUTES)", result);
    }

    [Fact]
    public void SessionWindow_WithSeconds_Should_GenerateExpectedKsql()
    {
        Expression<Func<ISessionWindow>> expr = () => Window.SessionWindow().Gap(TimeSpan.FromSeconds(30));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW SESSION (GAP 30 SECONDS)", result);
    }

    [Fact]
    public void SessionWindow_WithMinutes_Should_GenerateExpectedKsql()
    {
        Expression<Func<ISessionWindow>> expr = () => Window.SessionWindow().Gap(TimeSpan.FromMinutes(5));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW SESSION (GAP 5 MINUTES)", result);
    }

    [Fact]
    public void TumblingWindow_WithSeconds_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromSeconds(45));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 45 SECONDS)", result);
    }

    [Fact]
    public void HoppingWindow_OnlySizeSet_Should_GeneratePartialKsql()
    {
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow().Size(TimeSpan.FromMinutes(10));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES)", result);
    }

    [Fact]
    public void TumblingWindow_WithDays_Should_GenerateExpectedKsql()
    {
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow().Size(TimeSpan.FromDays(1));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 1 DAYS)", result);
    }

    [Fact]
    public void TumblingWindow_DefaultEmitBehavior_Should_NotIncludeEmitClause()
    {
        // Test that default behavior (EMIT CHANGES) is implicit and not included in output
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(5))
            .Retention(TimeSpan.FromHours(1));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW TUMBLING (SIZE 5 MINUTES, RETENTION 1 HOURS)", result);
        Assert.DoesNotContain("EMIT", result);
    }

    [Fact]
    public void HoppingWindow_DefaultEmitBehavior_Should_NotIncludeEmitClause()
    {
        // Test that default behavior (EMIT CHANGES) is implicit and not included in output
        Expression<Func<IHoppingWindow>> expr = () => Window.HoppingWindow()
            .Size(TimeSpan.FromMinutes(10))
            .AdvanceBy(TimeSpan.FromMinutes(5))
            .GracePeriod(TimeSpan.FromSeconds(30));
        var result = new KsqlWindowBuilder().Build(expr.Body);
        Assert.Equal("WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES, GRACE PERIOD 30 SECONDS)", result);
        Assert.DoesNotContain("EMIT", result);
    }

    [Fact]
    public void EmitFinal_EdgeCase_Documentation_Test()
    {
        // This test documents the EMIT FINAL edge case behavior
        // EMIT FINAL only emits when a new event arrives AFTER the window ends
        // If no event occurs at window close, the final result may never be emitted
        
        Expression<Func<ITumblingWindow>> expr = () => Window.TumblingWindow()
            .Size(TimeSpan.FromMinutes(1))
            .EmitFinal();
        var result = new KsqlWindowBuilder().Build(expr.Body);
        
        // Verify the syntax is correct
        Assert.Equal("WINDOW TUMBLING (SIZE 1 MINUTES) EMIT FINAL", result);
        
        // This test serves as documentation that EMIT FINAL has specific behavior:
        // - Results are only emitted when a trigger event arrives after window end
        // - Without a trigger event, final results may never be emitted
        // - This is by design in KSQL and must be considered in application logic
        Assert.Contains("EMIT FINAL", result);
    }
}