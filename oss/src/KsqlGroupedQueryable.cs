
using KsqlDsl.Modeling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl;

public class KsqlGroupedQueryable<T, TKey> : IQueryable<IGrouping<TKey, T>>, IAsyncEnumerable<IGrouping<TKey, T>>
{
    private readonly KafkaContext _context;
    private readonly EntityModel _entityModel;
    private readonly Expression _expression;
    private readonly IQueryProvider _queryProvider;

    internal KsqlGroupedQueryable(KafkaContext context, EntityModel entityModel, Expression expression)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _expression = expression ?? throw new ArgumentNullException(nameof(expression));
        _queryProvider = new EventQueryProvider<T>(context, entityModel);
    }

    public Type ElementType => typeof(IGrouping<TKey, T>);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _queryProvider;

    public IEnumerator<IGrouping<TKey, T>> GetEnumerator()
    {
        return new List<IGrouping<TKey, T>>().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public async IAsyncEnumerator<IGrouping<TKey, T>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        yield break;
    }

    public EventSet<TResult> Select<TResult>(Expression<Func<IGrouping<TKey, T>, TResult>> selector)
    {
        if (selector == null)
            throw new ArgumentNullException(nameof(selector));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            new[] { typeof(IGrouping<TKey, T>), typeof(TResult) },
            _expression,
            Expression.Quote(selector));

        return new EventSet<TResult>(_context, _entityModel, methodCall);
    }

    public KsqlGroupedQueryable<T, TKey> Where(Expression<Func<IGrouping<TKey, T>, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            new[] { typeof(IGrouping<TKey, T>) },
            _expression,
            Expression.Quote(predicate));

        return new KsqlGroupedQueryable<T, TKey>(_context, _entityModel, methodCall);
    }

    public string ToKsql()
    {
        try
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            var translator = new LinqToKsqlTranslator();
            return translator.Translate(_expression, topicName);
        }
        catch (Exception ex)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] KSQL変換エラー: {ex.Message}");
                Console.WriteLine($"[DEBUG] Expression: {_expression}");
            }
            return $"/* KSQL変換エラー: {ex.Message} */";
        }
    }

    public List<IGrouping<TKey, T>> ToList()
    {
        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            var ksqlQuery = ToKsql();
            Console.WriteLine($"[DEBUG] KsqlGroupedQueryable.ToList: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] Generated KSQL: {ksqlQuery}");
        }

        return new List<IGrouping<TKey, T>>();
    }

    public async Task<List<IGrouping<TKey, T>>> ToListAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return ToList();
    }
}