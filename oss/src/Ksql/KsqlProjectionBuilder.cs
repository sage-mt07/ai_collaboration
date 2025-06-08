using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;



public class KsqlProjectionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    public string Build(Expression expression)
    {
        Visit(expression);
        return _sb.Length > 0 ? "SELECT " + _sb.ToString().TrimEnd(',', ' ') : "SELECT *";
    }

    protected override Expression VisitNew(NewExpression node)
    {
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            if (node.Arguments[i] is MemberExpression member)
            {
                var name = member.Member.Name;
                var alias = node.Members[i].Name;
                _sb.Append(name != alias ? $"{name} AS {alias}, " : $"{name}, ");
            }
        }
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _sb.Append("*");
        return node;
    }
}
