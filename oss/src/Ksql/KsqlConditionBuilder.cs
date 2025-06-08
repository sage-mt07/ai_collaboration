using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Ksql;

public class KsqlConditionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    public string Build(Expression expression)
    {
        Visit(expression);
        return "WHERE " + _sb.ToString();
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

    protected override Expression VisitMember(MemberExpression node)
    {
        _sb.Append(node.Member.Name);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(string))
            _sb.Append($"'{node.Value}'");
        else if (node.Type == typeof(bool))
            _sb.Append(node.Value.ToString().ToLower());
        else
            _sb.Append(node.Value);
        return node;
    }

    private string GetSqlOperator(ExpressionType nodeType) => nodeType switch
    {
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