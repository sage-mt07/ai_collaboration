using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

internal static class KsqlGroupByBuilder
{
    public static string Build(Expression expression)
    {
        var visitor = new GroupByVisitor();
        visitor.Visit(expression);
        return "GROUP BY " + visitor.ToString();
    }

    private class GroupByVisitor : ExpressionVisitor
    {
        private readonly StringBuilder _sb = new();

        protected override Expression VisitNew(NewExpression node)
        {
            var keys = new List<string>();
            
            // Extract keys from Arguments with UnaryExpression support
            foreach (var arg in node.Arguments)
            {
                if (arg is MemberExpression member)
                    keys.Add(member.Member.Name);
                else if (arg is UnaryExpression unary && unary.Operand is MemberExpression member2)
                    keys.Add(member2.Member.Name);
            }

            // Fallback: if no keys found, try alternative extraction
            if (!keys.Any())
            {
                foreach (var expr in node.Arguments)
                {
                    if (expr is MemberExpression member)
                        keys.Add(member.Member.Name);
                    else if (expr is UnaryExpression unary && unary.Operand is MemberExpression member2)
                        keys.Add(member2.Member.Name);
                }
            }

            // Build the result string
            for (int i = 0; i < keys.Count; i++)
            {
                _sb.Append(keys[i]);
                if (i < keys.Count - 1)
                    _sb.Append(", ");
            }

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _sb.Append(node.Member.Name);
            return node;
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}
public static class KsqlExtensions
{
    public static TResult LatestByOffset<TSource, TResult>(this IGrouping<string, TSource> grouping, Func<TSource, TResult> selector)
    {
        // This is a placeholder implementation for compilation
        // The actual KSQL translation happens in the expression tree
        throw new NotImplementedException("This method should only be used in expression trees for KSQL translation");
    }

    public static TResult EarliestByOffset<TSource, TResult>(this IGrouping<string, TSource> grouping, Func<TSource, TResult> selector)
    {
        throw new NotImplementedException("This method should only be used in expression trees for KSQL translation");
    }

    public static List<TResult> CollectList<TSource, TResult>(this IGrouping<string, TSource> grouping, Func<TSource, TResult> selector)
    {
        throw new NotImplementedException("This method should only be used in expression trees for KSQL translation");
    }

    public static HashSet<TResult> CollectSet<TSource, TResult>(this IGrouping<string, TSource> grouping, Func<TSource, TResult> selector)
    {
        throw new NotImplementedException("This method should only be used in expression trees for KSQL translation");
    }
}