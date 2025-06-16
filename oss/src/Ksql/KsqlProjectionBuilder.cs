using System;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

internal class KsqlProjectionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    public string Build(Expression expression)
    {
        _sb.Clear();
        Visit(expression);
        return _sb.Length > 0 ? "SELECT " + _sb.ToString().TrimEnd(',', ' ') : "SELECT *";
    }

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
    protected override Expression VisitMember(MemberExpression node)
    {
        _sb.Append(node.Member.Name);
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _sb.Append("*");
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        // Skip Convert operations and process the inner operand directly
        return Visit(node.Operand);
    }

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

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sb.Append("(");
        Visit(node.Left);
        _sb.Append(" " + GetSqlOperator(node.NodeType) + " ");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

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