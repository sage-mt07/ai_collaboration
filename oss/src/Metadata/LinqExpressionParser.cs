namespace KsqlDsl.Metadata;

using System;
using System.Linq.Expressions;

internal static class LinqExpressionParser
{
    public static Expression<Func<T, TResult>> ParseLambda<T, TResult>(string expr)
    {
        throw new NotImplementedException("Expression parser is a placeholder.");
    }

    public static Expression<Func<T, object>> ParseLambda<T>(string expr)
    {
        throw new NotImplementedException("Expression parser is a placeholder.");
    }
}

