using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl;


public class KsqlGroupByBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    public static string Build(Expression body)
    {
        if (body is NewExpression newExpr)
        {
            var keys = newExpr.Arguments
                .OfType<MemberExpression>()
                .Select(m => m.Member.Name)
                .ToList();

            // fallback: Try deeper extract if not direct MemberExpression
            if (!keys.Any())
            {
                foreach (var expr in newExpr.Arguments)
                {
                    if (expr is MemberExpression member)
                        keys.Add(member.Member.Name);
                    else if (expr is UnaryExpression unary && unary.Operand is MemberExpression member2)
                        keys.Add(member2.Member.Name);
                }
            }

            return $"GROUP BY {string.Join(", ", keys)}";
        }
        else if (body is MemberExpression member)
        {
            return $"GROUP BY {member.Member.Name}";
        }
        return "GROUP BY UNKNOWN";
    }


    protected override Expression VisitMember(MemberExpression node)
    {
        _sb.Append(node.Member.Name + ", ");
        return node;
    }

    protected override Expression VisitNew(NewExpression node)
    {
        foreach (var arg in node.Arguments)
        {
            Visit(arg);
        }
        return node;
    }
}
public static class KsqlExtensions
{
    // 修正版: プロパティの型を返すように変更
    public static TProperty LatestByOffset<T, TKey, TProperty>(
        this IGrouping<TKey, T> grouping,
        Expression<Func<T, TProperty>> selector)
    {
        throw new NotSupportedException("This method is intended only for LINQ expression analysis.");
    }

    // 既存の互換性維持用（必要に応じて）
    public static T LatestByOffset<T, TKey>(
        this IGrouping<TKey, T> grouping,
        Expression<Func<T, object>> selector)
    {
        throw new NotSupportedException("This method is intended only for LINQ expression analysis.");
    }

    // 他の拡張メソッドも同様に修正
    public static TProperty EarliestByOffset<T, TKey, TProperty>(
        this IGrouping<TKey, T> grouping,
        Expression<Func<T, TProperty>> selector)
    {
        throw new NotSupportedException("This method is intended only for LINQ expression analysis.");
    }
}