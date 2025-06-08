using System;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl
{
    public class KsqlHavingBuilder : ExpressionVisitor
    {
        private readonly StringBuilder _sb = new();

        public string Build(Expression expression)
        {
            Visit(expression);
            return "HAVING " + _sb.ToString();
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

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name.ToUpper();

            // Handle aggregate functions like Sum, Count, Max, Min, etc.
            if (IsAggregateFunction(methodName))
            {
                // Transform method names for KSQL compatibility
                if (methodName.EndsWith("BYOFFSET"))
                    methodName = methodName.Replace("BYOFFSET", "_BY_OFFSET");

                // Special case: Count() without selector should be COUNT(*)
                // This handles both instance method Count() and static extension Count(source)
                if (methodName == "COUNT")
                {
                    // Case 1: g.Count() - no lambda selector (extension method with 1 arg)
                    if (node.Arguments.Count == 1 && !(node.Arguments[0] is LambdaExpression))
                    {
                        _sb.Append("COUNT(*)");
                        return node;
                    }
                    // Case 2: parameterless Count (unlikely but handle it)
                    if (node.Arguments.Count == 0)
                    {
                        _sb.Append("COUNT(*)");
                        return node;
                    }
                }

                // Case: instance method with lambda (g.Sum(x => x.Amount))
                if (node.Arguments.Count == 1 && node.Arguments[0] is LambdaExpression lambda)
                {
                    var memberExpr = ExtractMember(lambda.Body);
                    if (memberExpr != null)
                    {
                        _sb.Append($"{methodName}({memberExpr.Member.Name})");
                        return node;
                    }
                }

                // Case: static method (extension) with lambda in argument[1]
                if (node.Method.IsStatic && node.Arguments.Count == 2)
                {
                    var staticLambda = ExtractLambda(node.Arguments[1]);
                    if (staticLambda != null)
                    {
                        var memberExpr = ExtractMember(staticLambda.Body);
                        if (memberExpr != null)
                        {
                            _sb.Append($"{methodName}({memberExpr.Member.Name})");
                            return node;
                        }
                    }
                }

                // Fallback: use method object
                if (node.Object is MemberExpression objMember)
                {
                    _sb.Append($"{methodName}({objMember.Member.Name})");
                    return node;
                }

                _sb.Append($"{methodName}(UNKNOWN)");
                return node;
            }

            return base.VisitMethodCall(node);
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

        private static bool IsAggregateFunction(string methodName)
        {
            return methodName switch
            {
                "SUM" or "COUNT" or "MAX" or "MIN" or "AVG" or
                "LATESTBYOFFSET" or "EARLIESTBYOFFSET" or
                "COLLECT_LIST" or "COLLECT_SET" => true,
                _ => false
            };
        }

        private static MemberExpression ExtractMember(Expression body)
        {
            return body switch
            {
                MemberExpression member => member,
                UnaryExpression unary => ExtractMember(unary.Operand),
                _ => null
            };
        }

        private static LambdaExpression ExtractLambda(Expression expr)
        {
            return expr switch
            {
                LambdaExpression lambda => lambda,
                UnaryExpression { Operand: LambdaExpression lambda } => lambda,
                _ => null
            };
        }

        private static string GetSqlOperator(ExpressionType nodeType) => nodeType switch
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
}