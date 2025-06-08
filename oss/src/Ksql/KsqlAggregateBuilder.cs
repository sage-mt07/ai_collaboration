using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Ksql;
internal static class KsqlAggregateBuilder
{
    public static string Build(Expression expression)
    {
        var visitor = new AggregateVisitor();
        visitor.Visit(expression);
        return "SELECT " + visitor.ToString();
    }

    private class AggregateVisitor : ExpressionVisitor
    {
        private MemberExpression? ExtractMember(Expression body)
        {
            return body switch
            {
                MemberExpression member => member,
                UnaryExpression unary => ExtractMember(unary.Operand),
                _ => null
            };
        }

        private readonly StringBuilder _sb = new();

        private LambdaExpression? ExtractLambda(Expression expr)
        {
            return expr switch
            {
                LambdaExpression lambda => lambda,
                UnaryExpression { Operand: LambdaExpression lambda } => lambda,
                _ => null
            };
        }

        public override Expression Visit(Expression node)
        {
            if (node is NewExpression newExpr)
            {
                for (int i = 0; i < newExpr.Arguments.Count; i++)
                {
                    var arg = newExpr.Arguments[i];
                    var alias = newExpr.Members[i].Name;

                    if (arg is MethodCallExpression m)
                    {
                        var methodName = m.Method.Name.ToUpper();
                        if (methodName.EndsWith("BYOFFSET"))
                            methodName = methodName.Replace("BYOFFSET", "_BY_OFFSET");

                        // Case: instance method with lambda
                        if (m.Arguments.Count == 1 && m.Arguments[0] is LambdaExpression lambda && lambda.Body is MemberExpression member)
                        {
                            _sb.Append($"{methodName}({member.Member.Name}) AS {alias}, ");
                            continue;
                        }

                        // Case: static method (extension) with lambda in argument[1]
                        if (m.Method.IsStatic && m.Arguments.Count == 2)
                        {
                            var staticLambda = ExtractLambda(m.Arguments[1]);
                            if (staticLambda != null)
                            {
                                var memb = ExtractMember(staticLambda.Body);
                                if (memb != null)
                                    _sb.Append($"{methodName}({memb.Member.Name}) AS {alias}, ");
                                else
                                    _sb.Append($"{methodName}(UNKNOWN) AS {alias}, ");
                                continue;
                            }
                        }

                        // Fallback: use method object
                        if (m.Object is MemberExpression objMember)
                        {
                            _sb.Append($"{methodName}({objMember.Member.Name}) AS {alias}, ");
                            continue;
                        }

                        _sb.Append($"{methodName}(UNKNOWN) AS {alias}, ");
                    }
                }
            }

            return base.Visit(node);
        }

        public override string ToString()
        {
            return _sb.ToString().TrimEnd(',', ' ');
        }
    }
}