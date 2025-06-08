
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

internal class KsqlJoinBuilder
{
    public string Build(Expression expression)
    {
        var joinCall = FindJoinCall(expression);
        if (joinCall == null)
            return "UNSUPPORTED";

        var outerKeySelector = ExtractLambdaExpression(joinCall.Arguments[2]);
        var innerKeySelector = ExtractLambdaExpression(joinCall.Arguments[3]);
        var resultSelector = ExtractLambdaExpression(joinCall.Arguments[4]);

        var outerKeys = ExtractJoinKeys(outerKeySelector?.Body);
        var innerKeys = ExtractJoinKeys(innerKeySelector?.Body);
        var projections = ExtractProjection(resultSelector?.Body);

        var conditions = new System.Text.StringBuilder();
        for (int i = 0; i < outerKeys.Count; i++)
        {
            if (i > 0) conditions.Append(" AND ");
            var conditionOuterAlias = outerKeySelector?.Parameters.FirstOrDefault()?.Name ?? "o";
            var conditionInnerAlias = innerKeySelector?.Parameters.FirstOrDefault()?.Name ?? "c";
            conditions.Append($"{conditionOuterAlias}.{outerKeys[i]} = {conditionInnerAlias}.{innerKeys[i]}");
        }

        var outerAlias = outerKeySelector?.Parameters.FirstOrDefault()?.Name ?? "o";
        var innerAlias = innerKeySelector?.Parameters.FirstOrDefault()?.Name ?? "c";

        var outerTypeArg = joinCall.Arguments[0].Type.GetGenericArguments().FirstOrDefault();
        if (outerTypeArg == null) throw new InvalidOperationException("Unable to resolve outer type from Join arguments.");
        var outerType = outerTypeArg.Name;

        var innerTypeArg = joinCall.Arguments[1].Type.GetGenericArguments().FirstOrDefault();
        if (innerTypeArg == null) throw new InvalidOperationException("Unable to resolve inner type from Join arguments.");
        var innerType = innerTypeArg.Name;

        return $"SELECT {string.Join(", ", projections)} FROM {outerType} {outerAlias} JOIN {innerType} {innerAlias} ON {conditions}";
    }


    private MethodCallExpression FindJoinCall(Expression expr)
    {
        if (expr is MethodCallExpression mce && mce.Method.Name == "Join")
            return mce;

        if (expr is LambdaExpression le)
            return FindJoinCall(le.Body);

        if (expr is UnaryExpression ue)
            return FindJoinCall(ue.Operand);

        if (expr is InvocationExpression ie)
            return FindJoinCall(ie.Expression);

        if (expr is MemberInitExpression mie)
        {
            foreach (var b in mie.Bindings)
            {
                if (b is MemberAssignment ma)
                {
                    var inner = FindJoinCall(ma.Expression);
                    if (inner != null) return inner;
                }
            }
        }

        if (expr is NewExpression ne)
        {
            foreach (var arg in ne.Arguments)
            {
                var inner = FindJoinCall(arg);
                if (inner != null) return inner;
            }
        }

        return null;
    }

    private List<string> ExtractJoinKeys(Expression expr)
    {
        var keys = new List<string>();
        if (expr is NewExpression newExpr)
        {
            foreach (var arg in newExpr.Arguments)
            {
                var member = ExtractMemberExpression(arg);
                if (member != null) keys.Add(member.Member.Name);
            }
        }
        else if (expr is MemberExpression memberExpr)
        {
            keys.Add(memberExpr.Member.Name);
        }
        return keys;
    }
    private static LambdaExpression ExtractLambdaExpression(Expression expr)
    {
        return expr switch
        {
            UnaryExpression unary when unary.Operand is LambdaExpression lambda => lambda,
            LambdaExpression lambda => lambda,
            _ => null
        };
    }

    private static MemberExpression ExtractMemberExpression(Expression expr)
    {
        return expr switch
        {
            MemberExpression m => m,
            UnaryExpression u when u.Operand is MemberExpression m => m,
            _ => null
        };
    }
    private List<string> ExtractProjection(Expression expr)
    {
        var props = new List<string>();
        if (expr is NewExpression newExpr)
        {
            foreach (var arg in newExpr.Arguments)
            {
                if (arg is MemberExpression memberExpr)
                {
                    string alias = null;
                    if (memberExpr.Expression is ParameterExpression pe)
                    {
                        alias = pe.Name;
                    }
                    else if (memberExpr.Expression is MemberExpression me && me.Expression is ParameterExpression mpe)
                    {
                        alias = mpe.Name;
                    }
                    if (string.IsNullOrEmpty(alias)) throw new InvalidOperationException("Unable to resolve alias for projection.");
                    props.Add($"{alias}.{memberExpr.Member.Name}");
                }
            }
        }
        return props;
    }

}