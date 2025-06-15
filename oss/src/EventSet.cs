using Ksql.EntityFrameworkCore;
using KsqlDsl.Ksql;
using KsqlDsl.Modeling;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl;

public class EventSet<T> : IQueryable<T>, IAsyncEnumerable<T>
{
    private readonly KafkaContext _context;
    private readonly EntityModel _entityModel;
    private readonly IQueryProvider _queryProvider;
    private readonly Expression _expression;

    internal EventSet(KafkaContext context, EntityModel entityModel)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _queryProvider = new EventQueryProvider<T>(context, entityModel);
        _expression = Expression.Constant(this);
    }

    internal EventSet(KafkaContext context, EntityModel entityModel, Expression expression)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _queryProvider = new EventQueryProvider<T>(context, entityModel);
        _expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _queryProvider;

    public IEnumerator<T> GetEnumerator()
    {
        return ToList().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        var items = ToList();
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return item;
        }
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        await Task.Delay(1, cancellationToken);

        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] EventSet.AddAsync: {typeof(T).Name} → Topic: {topicName}");
        }
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));

        var entityList = entities.ToList();

        foreach (var entity in entityList)
        {
            await AddAsync(entity, cancellationToken);
        }
    }

    public List<T> ToList()
    {
        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            var ksqlQuery = ToKsql();
            Console.WriteLine($"[DEBUG] EventSet.ToList: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] Generated KSQL: {ksqlQuery}");
        }

        return new List<T>();
    }

    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return ToList();
    }

    public void Subscribe(Action<T> onNext, CancellationToken cancellationToken = default)
    {
        if (onNext == null)
            throw new ArgumentNullException(nameof(onNext));

        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] EventSet.Subscribe: {typeof(T).Name} ← Topic: {topicName} (Push型購読開始)");
        }
    }

    public async Task SubscribeAsync(Func<T, Task> onNext, CancellationToken cancellationToken = default)
    {
        if (onNext == null)
            throw new ArgumentNullException(nameof(onNext));

        await Task.Delay(1, cancellationToken);

        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] EventSet.SubscribeAsync: {typeof(T).Name} ← Topic: {topicName} (非同期Push型購読開始)");
        }
    }

    public async Task ForEachAsync(Func<T, Task> action, CancellationToken cancellationToken = default)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var items = await ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await action(item);
        }
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

    public EntityModel GetEntityModel()
    {
        return _entityModel;
    }

    public KafkaContext GetContext()
    {
        return _context;
    }

    public string GetTopicName()
    {
        return _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
    }

    public EventSet<T> Where(Expression<Func<T, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            new[] { typeof(T) },
            _expression,
            Expression.Quote(predicate));

        return new EventSet<T>(_context, _entityModel, methodCall);
    }

    public EventSet<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        if (selector == null)
            throw new ArgumentNullException(nameof(selector));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            new[] { typeof(T), typeof(TResult) },
            _expression,
            Expression.Quote(selector));

        return new EventSet<TResult>(_context, _entityModel, methodCall);
    }

    public EventSet<IGrouping<TKey, T>> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector == null)
            throw new ArgumentNullException(nameof(keySelector));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.GroupBy),
            new[] { typeof(T), typeof(TKey) },
            _expression,
            Expression.Quote(keySelector));

        return new EventSet<IGrouping<TKey, T>>(_context, _entityModel, methodCall);
    }

    public EventSet<T> Take(int count)
    {
        if (count <= 0)
            throw new ArgumentException("Count must be positive", nameof(count));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Take),
            new[] { typeof(T) },
            _expression,
            Expression.Constant(count));

        return new EventSet<T>(_context, _entityModel, methodCall);
    }

    public EventSet<T> Skip(int count)
    {
        if (count < 0)
            throw new ArgumentException("Count cannot be negative", nameof(count));

        var methodCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Skip),
            new[] { typeof(T) },
            _expression,
            Expression.Constant(count));

        return new EventSet<T>(_context, _entityModel, methodCall);
    }

    public EventSet<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        throw new NotSupportedException("ORDER BY operations are not supported in ksqlDB. Use windowed aggregations for time-based ordering.");
    }

    public EventSet<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        throw new NotSupportedException("ORDER BY operations are not supported in ksqlDB. Use windowed aggregations for time-based ordering.");
    }

    public override string ToString()
    {
        var topicName = GetTopicName();
        var entityName = typeof(T).Name;
        return $"EventSet<{entityName}> → Topic: {topicName}";
    }
}