using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Metadata;

/// <summary>
/// Analyzer for inferring STREAM vs TABLE based on LINQ expression patterns
/// </summary>
internal class StreamTableInferenceAnalyzer : ExpressionVisitor
{
    private bool _hasGroupBy = false;
    private bool _hasAggregate = false;
    private bool _hasWindow = false;
    private bool _hasAsStream = false;
    private bool _hasAsTable = false;
    private readonly HashSet<string> _aggregateMethods = new()
        {
            "Sum", "Count", "Max", "Min", "Average", "Avg",
            "LatestByOffset", "EarliestByOffset", "CollectList", "CollectSet"
        };

    public InferenceResult AnalyzeExpression(Expression expression)
    {
        // Reset state
        _hasGroupBy = _hasAggregate = _hasWindow = _hasAsStream = _hasAsTable = false;

        Visit(expression);

        return DetermineResult();
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var methodName = node.Method.Name;

        switch (methodName)
        {
            case "GroupBy":
                _hasGroupBy = true;
                break;
            case "AsStream":
                _hasAsStream = true;
                break;
            case "AsTable":
                _hasAsTable = true;
                break;
            case "Window":
            case "TumblingWindow":
            case "HoppingWindow":
            case "SessionWindow":
                _hasWindow = true;
                break;
            default:
                // Check if method name contains aggregate function names or is a mock method
                if (_aggregateMethods.Contains(methodName) ||
                    methodName.Contains("Count") ||
                    methodName.StartsWith("Mock"))
                    _hasAggregate = true;
                break;
        }

        return base.VisitMethodCall(node);
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        // Visit the lambda body to analyze the expression
        Visit(node.Body);
        return node;
    }

    private InferenceResult DetermineResult()
    {
        // Explicit markers take highest priority
        if (_hasAsStream)
            return new InferenceResult
            {
                InferredType = StreamTableType.Stream,
                IsExplicitlyDefined = true,
                Reason = "Explicit .AsStream() marker"
            };

        if (_hasAsTable)
            return new InferenceResult
            {
                InferredType = StreamTableType.Table,
                IsExplicitlyDefined = true,
                Reason = "Explicit .AsTable() marker"
            };

        // Pattern-based inference
        if (_hasGroupBy || _hasAggregate || _hasWindow)
            return new InferenceResult
            {
                InferredType = StreamTableType.Table,
                IsExplicitlyDefined = false,
                Reason = $"Contains {(_hasGroupBy ? "GroupBy" : _hasAggregate ? "Aggregate" : "Window")} operation"
            };

        // Default to STREAM for simple operations like Where/Select
        return new InferenceResult
        {
            InferredType = StreamTableType.Stream,
            IsExplicitlyDefined = false,
            Reason = "Simple query without aggregation (default to STREAM)"
        };
    }
}

