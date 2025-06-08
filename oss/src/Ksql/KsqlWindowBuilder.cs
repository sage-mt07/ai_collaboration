using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

public class KsqlWindowBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();
    private string _windowType = "";
    private string _size = "";
    private string _advanceBy = "";
    private string _gap = "";
    private string _retention = "";
    private string _gracePeriod = "";
    private string _emitBehavior = ""; // "FINAL" or empty (default CHANGES)

    public string Build(Expression expression)
    {
        Visit(expression);
        return BuildWindowClause();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // First visit the object to ensure we process method chains in the correct order
        if (node.Object != null)
        {
            Visit(node.Object);
        }

        var methodName = node.Method.Name;

        switch (methodName)
        {
            case "TumblingWindow":
                _windowType = "TUMBLING";
                break;
            
            case "HoppingWindow":
                _windowType = "HOPPING";
                break;
            
            case "SessionWindow":
                _windowType = "SESSION";
                break;
            
            case "Size":
                _size = ExtractTimeSpanValue(node);
                break;
            
            case "AdvanceBy":
                _advanceBy = ExtractTimeSpanValue(node);
                break;
            
            case "Gap":
                _gap = ExtractTimeSpanValue(node);
                break;
            
            case "Retention":
                _retention = ExtractTimeSpanValue(node);
                break;
            
            case "GracePeriod":
                _gracePeriod = ExtractTimeSpanValue(node);
                break;
            
            case "EmitFinal":
                _emitBehavior = "FINAL";
                break;
        }

        return node;
    }

    private string ExtractTimeSpanValue(MethodCallExpression node)
    {
        if (node.Arguments.Count > 0)
        {
            var arg = node.Arguments[0];
            
            // Handle TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), etc.
            if (arg is MethodCallExpression timeSpanCall && timeSpanCall.Method.DeclaringType == typeof(TimeSpan))
            {
                var value = ExtractConstantValue(timeSpanCall.Arguments[0]);
                var unit = timeSpanCall.Method.Name switch
                {
                    "FromMinutes" => "MINUTES",
                    "FromSeconds" => "SECONDS",
                    "FromHours" => "HOURS",
                    "FromDays" => "DAYS",
                    _ => "UNKNOWN"
                };
                return $"{value} {unit}";
            }
            
            // Handle direct constants
            if (arg is ConstantExpression constant)
            {
                if (constant.Value is TimeSpan timeSpan)
                {
                    return FormatTimeSpan(timeSpan);
                }
            }
        }
        
        return "UNKNOWN";
    }

    private string ExtractConstantValue(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value?.ToString() ?? "0";
        }
        return "UNKNOWN";
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays} DAYS";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours} HOURS";
        if (timeSpan.TotalMinutes >= 1)
            return $"{(int)timeSpan.TotalMinutes} MINUTES";
        if (timeSpan.TotalSeconds >= 1)
            return $"{(int)timeSpan.TotalSeconds} SECONDS";
        
        return "0 SECONDS";
    }

    private string BuildWindowClause()
    {
        return _windowType switch
        {
            "TUMBLING" => BuildTumblingClause(),
            "HOPPING" => BuildHoppingClause(),
            "SESSION" => BuildSessionClause(),
            _ => "WINDOW UNKNOWN"
        };
    }

    private string BuildTumblingClause()
    {
        var clause = $"WINDOW TUMBLING (SIZE {_size}";
        
        if (!string.IsNullOrEmpty(_retention))
            clause += $", RETENTION {_retention}";
        
        if (!string.IsNullOrEmpty(_gracePeriod))
            clause += $", GRACE PERIOD {_gracePeriod}";
        
        clause += ")";
        
        if (!string.IsNullOrEmpty(_emitBehavior))
            clause += $" EMIT {_emitBehavior}";
        
        return clause;
    }

    private string BuildHoppingClause()
    {
        var clause = $"WINDOW HOPPING (SIZE {_size}";
        
        if (!string.IsNullOrEmpty(_advanceBy))
            clause += $", ADVANCE BY {_advanceBy}";
        
        if (!string.IsNullOrEmpty(_retention))
            clause += $", RETENTION {_retention}";
        
        if (!string.IsNullOrEmpty(_gracePeriod))
            clause += $", GRACE PERIOD {_gracePeriod}";
        
        clause += ")";
        
        if (!string.IsNullOrEmpty(_emitBehavior))
            clause += $" EMIT {_emitBehavior}";
        
        return clause;
    }

    private string BuildSessionClause()
    {
        var clause = $"WINDOW SESSION (GAP {_gap})";
        
        // Note: SESSION windows do not support RETENTION, GRACE PERIOD, or EMIT FINAL
        // They always emit changes immediately when sessions close
        
        return clause;
    }
}

// Supporting classes for type-safe window configuration
public static class Window
{
    public static ITumblingWindow TumblingWindow() => new TumblingWindowImpl();
    public static IHoppingWindow HoppingWindow() => new HoppingWindowImpl();
    public static ISessionWindow SessionWindow() => new SessionWindowImpl();
}

public interface ITumblingWindow
{
    ITumblingWindow Size(TimeSpan duration);
    
    /// <summary>
    /// Sets the retention period for the window. Data older than this period will be discarded.
    /// </summary>
    /// <param name="duration">The retention period</param>
    /// <returns>The window builder for method chaining</returns>
    ITumblingWindow Retention(TimeSpan duration);
    
    /// <summary>
    /// Sets the grace period for late-arriving data.
    /// </summary>
    /// <param name="duration">The grace period</param>
    /// <returns>The window builder for method chaining</returns>
    ITumblingWindow GracePeriod(TimeSpan duration);
    
    /// <summary>
    /// Configures the window to emit final results only.
    /// WARNING: EMIT FINAL only emits when a new event arrives AFTER the window ends.
    /// If no event occurs at window close, the final result may never be emitted.
    /// </summary>
    /// <returns>The window builder for method chaining</returns>
    ITumblingWindow EmitFinal();
}

public interface IHoppingWindow
{
    IHoppingWindow Size(TimeSpan duration);
    IHoppingWindow AdvanceBy(TimeSpan duration);
    
    /// <summary>
    /// Sets the retention period for the window. Data older than this period will be discarded.
    /// </summary>
    /// <param name="duration">The retention period</param>
    /// <returns>The window builder for method chaining</returns>
    IHoppingWindow Retention(TimeSpan duration);
    
    /// <summary>
    /// Sets the grace period for late-arriving data.
    /// </summary>
    /// <param name="duration">The grace period</param>
    /// <returns>The window builder for method chaining</returns>
    IHoppingWindow GracePeriod(TimeSpan duration);
    
    /// <summary>
    /// Configures the window to emit final results only.
    /// WARNING: EMIT FINAL only emits when a new event arrives AFTER the window ends.
    /// If no event occurs at window close, the final result may never be emitted.
    /// </summary>
    /// <returns>The window builder for method chaining</returns>
    IHoppingWindow EmitFinal();
}

public interface ISessionWindow
{
    /// <summary>
    /// Sets the session gap. If no events arrive within this gap, the session is considered closed.
    /// Note: Session windows do not support RETENTION, GRACE PERIOD, or EMIT FINAL.
    /// They always emit changes immediately when sessions close.
    /// </summary>
    /// <param name="duration">The session gap</param>
    /// <returns>The window builder for method chaining</returns>
    ISessionWindow Gap(TimeSpan duration);
}

internal class TumblingWindowImpl : ITumblingWindow
{
    public ITumblingWindow Size(TimeSpan duration) => this;
    public ITumblingWindow Retention(TimeSpan duration) => this;
    public ITumblingWindow GracePeriod(TimeSpan duration) => this;
    public ITumblingWindow EmitFinal() => this;
}

internal class HoppingWindowImpl : IHoppingWindow
{
    public IHoppingWindow Size(TimeSpan duration) => this;
    public IHoppingWindow AdvanceBy(TimeSpan duration) => this;
    public IHoppingWindow Retention(TimeSpan duration) => this;
    public IHoppingWindow GracePeriod(TimeSpan duration) => this;
    public IHoppingWindow EmitFinal() => this;
}

internal class SessionWindowImpl : ISessionWindow
{
    public ISessionWindow Gap(TimeSpan duration) => this;
}
