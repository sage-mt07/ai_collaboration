using System;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

/// <summary>
/// Builder for generating KSQL SELECT projection clauses from LINQ expressions
/// </summary>
internal class KsqlProjectionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Builds a KSQL SELECT clause from a LINQ expression
    /// </summary>
    /// <param name="expression">The expression to build from</param>
    /// <returns>KSQL SELECT clause</returns>
    public string Build(Expression expression)
    {
        _sb.Clear();
        Visit(expression);
        return _sb.Length > 0 ? "SELECT " + _sb.ToString().TrimEnd(',', ' ') : "SELECT *";
    }

    /// <summary>
    /// Handles anonymous type projections: new { o.Id, o.Name }
    /// </summary>
    /// <param name="node">The NewExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitNew(NewExpression node)
    {
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            var arg = node.Arguments[i];
            var alias = node.Members?[i]?.Name;

            if (arg is MemberExpression member)
            {
                var memberName = member.Member.Name;
                
                if (!string.IsNullOrEmpty(alias) && alias != memberName)
                {
                    _sb.Append($"{memberName} AS {alias}, ");
                }
                else
                {
                    _sb.Append($"{memberName}, ");
                }
            }
            else if (arg is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                // Handle UnaryExpression wrapping (like type conversions)
                var memberName = unaryMember.Member.Name;
                
                if (!string.IsNullOrEmpty(alias) && alias != memberName)
                {
                    _sb.Append($"{memberName} AS {alias}, ");
                }
                else
                {
                    _sb.Append($"{memberName}, ");
                }
            }
            else
            {
                // Handle other expression types (constants, method calls, etc.)
                var aliasToUse = alias ?? $"expr{i}";
                Visit(arg);
                if (!string.IsNullOrEmpty(aliasToUse))
                {
                    _sb.Append($" AS {aliasToUse}");
                }
                _sb.Append(", ");
            }
        }
        return node;
    }

    /// <summary>
    /// Handles direct member access: o => o.PropertyName
    /// </summary>
    /// <param name="node">The MemberExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitMember(MemberExpression node)
    {
        _sb.Append(node.Member.Name);
        return node;
    }

    /// <summary>
    /// Handles parameter expressions: o => *
    /// </summary>
    /// <param name="node">The ParameterExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitParameter(ParameterExpression node)
    {
        _sb.Append("*");
        return node;
    }

    /// <summary>
    /// Handles UnaryExpression nodes, particularly Convert operations that occur
    /// when anonymous types are cast to object in LINQ expressions.
    /// </summary>
    /// <param name="node">The UnaryExpression to process</param>
    /// <returns>The result of visiting the operand</returns>
    protected override Expression VisitUnary(UnaryExpression node)
    {
        // Skip Convert operations and process the inner operand directly
        return Visit(node.Operand);
    }

    /// <summary>
    /// Handles constant expressions
    /// </summary>
    /// <param name="node">The ConstantExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(string))
            _sb.Append($"'{node.Value}'");
        else if (node.Type == typeof(bool))
            _sb.Append(node.Value.ToString()?.ToLower());
        else
            _sb.Append(node.Value);
        return node;
    }

    /// <summary>
    /// Handles method call expressions (for functions in projections)
    /// </summary>
    /// <param name="node">The MethodCallExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var methodName = node.Method.Name.ToUpper();

        // Handle common KSQL functions that might appear in projections
        switch (methodName)
        {
            case "TOSTRING":
                _sb.Append("CAST(");
                Visit(node.Object ?? node.Arguments[0]);
                _sb.Append(" AS VARCHAR)");
                break;
            case "TOLOWER":
                _sb.Append("LCASE(");
                Visit(node.Object ?? node.Arguments[0]);
                _sb.Append(")");
                break;
            case "TOUPPER":
                _sb.Append("UCASE(");
                Visit(node.Object ?? node.Arguments[0]);
                _sb.Append(")");
                break;
            case "SUBSTRING":
                _sb.Append("SUBSTRING(");
                Visit(node.Object ?? node.Arguments[0]);
                _sb.Append(", ");
                Visit(node.Arguments[node.Object != null ? 0 : 1]);
                if (node.Arguments.Count > (node.Object != null ? 1 : 2))
                {
                    _sb.Append(", ");
                    Visit(node.Arguments[node.Object != null ? 1 : 2]);
                }
                _sb.Append(")");
                break;
            default:
                // For unknown methods, just use the method name as a function
                _sb.Append($"{methodName}(");
                if (node.Object != null)
                {
                    Visit(node.Object);
                    if (node.Arguments.Count > 0)
                        _sb.Append(", ");
                }
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    Visit(node.Arguments[i]);
                    if (i < node.Arguments.Count - 1)
                        _sb.Append(", ");
                }
                _sb.Append(")");
                break;
        }
        return node;
    }

    /// <summary>
    /// Handles binary expressions (for calculations in projections)
    /// </summary>
    /// <param name="node">The BinaryExpression to process</param>
    /// <returns>The processed expression</returns>
    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sb.Append("(");
        Visit(node.Left);
        _sb.Append(" " + GetSqlOperator(node.NodeType) + " ");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

    /// <summary>
    /// Maps .NET binary operators to KSQL operators
    /// </summary>
    /// <param name="nodeType">The expression type</param>
    /// <returns>KSQL operator string</returns>
    private static string GetSqlOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Add => "+",
        ExpressionType.Subtract => "-",
        ExpressionType.Multiply => "*",
        ExpressionType.Divide => "/",
        ExpressionType.Modulo => "%",
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "<>",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.AndAlso => "AND",
        ExpressionType.OrElse => "OR",
        _ => throw new NotSupportedException($"Unsupported operator: {nodeType}")
    };
}