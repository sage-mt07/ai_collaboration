
using KsqlDsl.Modeling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl;

internal class EventQueryProvider<T> : IQueryProvider
{
    private readonly KafkaContext _context;
    private readonly EntityModel _entityModel;

    public EventQueryProvider(KafkaContext context, EntityModel entityModel)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
    }

    public IQueryable CreateQuery(Expression expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var elementType = expression.Type.GetGenericArguments().FirstOrDefault() ?? typeof(object);

        var queryableType = typeof(EventSet<>).MakeGenericType(elementType);
        return (IQueryable)Activator.CreateInstance(queryableType, _context, _entityModel, expression)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        return new EventSet<TElement>(_context, _entityModel, expression);
    }

    public object? Execute(Expression expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        // クエリ実行時の処理（ToList等）
        // TODO: 実際のKafka Consumer実装
        return new List<T>();
    }

    public TResult Execute<TResult>(Expression expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        var result = Execute(expression);
        return (TResult)result!;
    }
}