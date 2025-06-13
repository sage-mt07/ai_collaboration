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
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var visitor = new AggregateVisitor();
        visitor.Visit(expression);
        return "SELECT " + visitor.ToString();
    }

    private class AggregateVisitor : ExpressionVisitor
    {
        private readonly StringBuilder _sb = new();

        private MemberExpression? ExtractMember(Expression body)
        {
            return body switch
            {
                MemberExpression member => member,
                UnaryExpression unary => ExtractMember(unary.Operand),
                _ => null
            };
        }

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
                    var alias = newExpr.Members?[i]?.Name ?? $"expr{i}";

                    // Handle g.Key (GroupBy key)
                    if (arg is MemberExpression memberExpr && memberExpr.Member.Name == "Key")
                    {
                        _sb.Append($"{alias}, ");
                        continue;
                    }

                    // Handle method calls (aggregate functions)
                    if (arg is MethodCallExpression m)
                    {
                        var methodName = m.Method.Name.ToUpper();
                        
                        // Transform specific method names
                        methodName = TransformMethodName(methodName);

                        // Special case: Count() without selector should be COUNT(*)
                        if (methodName == "COUNT")
                        {
                            // Case 1: g.Count() - no lambda selector (extension method with 1 arg)
                            if (m.Arguments.Count == 1 && !(m.Arguments[0] is LambdaExpression))
                            {
                                _sb.Append($"COUNT(*) AS {alias}, ");
                                continue;
                            }
                            // Case 2: parameterless Count (unlikely but handle it)
                            if (m.Arguments.Count == 0)
                            {
                                _sb.Append($"COUNT(*) AS {alias}, ");
                                continue;
                            }
                        }

                        // Case: instance method with lambda (g.Sum(x => x.Amount))
                        if (m.Arguments.Count == 1 && m.Arguments[0] is LambdaExpression lambda)
                        {
                            var memb = ExtractMember(lambda.Body);
                            if (memb != null)
                            {
                                _sb.Append($"{methodName}({memb.Member.Name}) AS {alias}, ");
                                continue;
                            }
                        }

                        // Case: static method (extension) with lambda in argument[1]
                        if (m.Method.IsStatic && m.Arguments.Count == 2)
                        {
                            var staticLambda = ExtractLambda(m.Arguments[1]);
                            if (staticLambda != null)
                            {
                                var memb = ExtractMember(staticLambda.Body);
                                if (memb != null)
                                {
                                    _sb.Append($"{methodName}({memb.Member.Name}) AS {alias}, ");
                                    continue;
                                }
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
                    else
                    {
                        // Handle other expression types (constants, etc.)
                        _sb.Append($"UNKNOWN AS {alias}, ");
                    }
                }
            }

            return base.Visit(node);
        }

        private static string TransformMethodName(string methodName)
        {
            return methodName switch
            {
                "LATESTBYOFFSET" => "LATEST_BY_OFFSET",
                "EARLIESTBYOFFSET" => "EARLIEST_BY_OFFSET",
                "COLLECTLIST" => "COLLECT_LIST",
                "COLLECTSET" => "COLLECT_SET",
                "AVERAGE" => "AVG", // KSQL uses AVG instead of AVERAGE
                _ => methodName
            };
        }

        public override string ToString()
        {
            return _sb.ToString().TrimEnd(',', ' ');
        }
    }
}