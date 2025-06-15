using Ksql.EntityFrameworkCore;
using KsqlDsl.Ksql;
using KsqlDsl.Modeling;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

        ValidateEntity(entity);

        var producerService = _context.GetProducerService();
        await producerService.SendAsync(entity, _entityModel, cancellationToken);

        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] EventSet.AddAsync: {typeof(T).Name} → Topic: {topicName} (送信完了)");
        }
    }

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return;

        // Validate all entities first
        foreach (var entity in entityList)
        {
            ValidateEntity(entity);
        }

        var producerService = _context.GetProducerService();
        await producerService.SendRangeAsync(entityList, _entityModel, cancellationToken);

        if (_context.Options.EnableDebugLogging)
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] EventSet.AddRangeAsync: {entityList.Count}件の{typeof(T).Name} → Topic: {topicName} (送信完了)");
        }
    }

    // EventSet.cs ToList/ToListAsync部分の改善版
    // 修正理由：Phase3-3でToList/ToListAsyncの本実装化

    public List<T> ToList()
    {
        var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;

        // 修正理由：Phase3-3でKSQL生成前バリデーション追加
        ValidateQueryBeforeExecution();

        // 修正理由：Pull Queryとして実行（isPullQuery: true）
        var ksqlQuery = ToKsql(isPullQuery: true);

        if (_context.Options.EnableDebugLogging)
        {
            Console.WriteLine($"[DEBUG] EventSet.ToList: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] Generated KSQL: {ksqlQuery}");

            // 修正理由：Phase3-3で診断情報追加（フラグ制御版）
            var translator = new LinqToKsqlTranslator();
            translator.Translate(_expression, topicName, isPullQuery: true);
            Console.WriteLine($"[DEBUG] Query Diagnostics:");
            Console.WriteLine(translator.GetDiagnostics());
        }

        var consumerService = _context.GetConsumerService();

        try
        {
            // 修正理由：Phase3-3で前処理バリデーション
            if (string.IsNullOrEmpty(ksqlQuery) || ksqlQuery.Contains("/* KSQL変換エラー"))
            {
                throw new InvalidOperationException($"Failed to generate valid KSQL query for {typeof(T).Name}");
            }

            var results = consumerService.Query<T>(ksqlQuery, _entityModel);

            // 修正理由：Phase3-3で後処理バリデーション
            ValidateQueryResults(results);

            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Query completed successfully. Results: {results.Count} items");
            }

            return results;
        }
        catch (KafkaConsumerException ex)
        {
            // 修正理由：task_eventset.txt「例外設計厳守」に準拠
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Consumer query error: {ex.Message}");
            }

            // 修正理由：設計ドキュメント準拠 - Consumer例外は上位に伝播
            throw new InvalidOperationException(
                $"Failed to query topic '{topicName}' for {typeof(T).Name}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Unexpected query error: {ex.Message}");
            }

            // 修正理由：設計ドキュメント準拠 - 予期しない例外も適切にラップ
            throw new InvalidOperationException(
                $"Unexpected error querying {typeof(T).Name} from topic '{topicName}': {ex.Message}", ex);
        }
    }

    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;

        // 修正理由：Phase3-3でKSQL生成前バリデーション追加
        ValidateQueryBeforeExecution();

        // 修正理由：Pull Queryとして実行（isPullQuery: true）
        var ksqlQueryAsync = ToKsql(isPullQuery: true);

        if (_context.Options.EnableDebugLogging)
        {
            Console.WriteLine($"[DEBUG] EventSet.ToListAsync: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] Generated KSQL: {ksqlQueryAsync}");

            // 修正理由：Phase3-3で診断情報追加（フラグ制御版）
            var translator = new LinqToKsqlTranslator();
            translator.Translate(_expression, topicName, isPullQuery: true);
            Console.WriteLine($"[DEBUG] Query Diagnostics:");
            Console.WriteLine(translator.GetDiagnostics());
        }

        var consumerService = _context.GetConsumerService();

        try
        {
            // 修正理由：Phase3-3で前処理バリデーション
            if (string.IsNullOrEmpty(ksqlQueryAsync) || ksqlQueryAsync.Contains("/* KSQL変換エラー"))
            {
                throw new InvalidOperationException($"Failed to generate valid KSQL query for {typeof(T).Name}");
            }

            var results = await consumerService.QueryAsync<T>(ksqlQueryAsync, _entityModel, cancellationToken);

            // 修正理由：Phase3-3で後処理バリデーション
            ValidateQueryResults(results);

            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Async query completed successfully. Results: {results.Count} items");
            }

            return results;
        }
        catch (KafkaConsumerException ex)
        {
            // 修正理由：task_eventset.txt「例外設計厳守」に準拠
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Consumer query error: {ex.Message}");
            }

            // 修正理由：設計ドキュメント準拠 - Consumer例外は上位に伝播
            throw new InvalidOperationException(
                $"Failed to query topic '{topicName}' for {typeof(T).Name}: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            // 修正理由：Phase3-3でCancellationToken対応
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Query cancelled by CancellationToken");
            }
            throw; // キャンセレーション例外はそのまま再スロー
        }
        catch (Exception ex)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Unexpected query error: {ex.Message}");
            }

            // 修正理由：設計ドキュメント準拠 - 予期しない例外も適切にラップ
            throw new InvalidOperationException(
                $"Unexpected error querying {typeof(T).Name} from topic '{topicName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// クエリ実行前バリデーション
    /// 修正理由：Phase3-3でバリデーション強化
    /// </summary>
    private void ValidateQueryBeforeExecution()
    {
        // EntityModelバリデーション
        if (_entityModel == null)
        {
            throw new InvalidOperationException($"EntityModel is not configured for {typeof(T).Name}");
        }

        if (!_entityModel.IsValid)
        {
            var errors = _entityModel.ValidationResult?.Errors ?? new List<string> { "Unknown validation error" };
            throw new InvalidOperationException(
                $"EntityModel validation failed for {typeof(T).Name}: {string.Join("; ", errors)}");
        }

        // Expression バリデーション
        if (_expression == null)
        {
            throw new InvalidOperationException($"Query expression is null for {typeof(T).Name}");
        }

        // 修正理由：Phase3-3で未サポート操作チェック強化
        try
        {
            CheckForUnsupportedOperations(_expression);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(
                $"Unsupported LINQ operation detected in query for {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// クエリ結果後処理バリデーション
    /// 修正理由：Phase3-3でバリデーション強化（型エラー修正）
    /// </summary>
    private void ValidateQueryResults(List<T> results)
    {
        if (results == null)
        {
            throw new InvalidOperationException($"Query returned null results for {typeof(T).Name}");
        }

        // 修正理由：Phase3-3でStrictモード時の追加バリデーション
        if (_context.Options.ValidationMode == ValidationMode.Strict)
        {
            foreach (var result in results)
            {
                if (result == null)
                {
                    throw new InvalidOperationException(
                        $"Query returned null entity in results for {typeof(T).Name}");
                }

                // 修正理由：Strictモードでは必須プロパティチェック（型一致により修正）
                ValidateEntityStrict(result);
            }
        }
    }

    /// <summary>
    /// 未サポート操作チェック
    /// 修正理由：Phase3-3で未サポート操作の事前検出
    /// </summary>
    private void CheckForUnsupportedOperations(Expression expression)
    {
        var visitor = new UnsupportedOperationVisitor();
        visitor.Visit(expression);
    }

    /// <summary>
    /// 未サポート操作検出Visitor
    /// 修正理由：Phase3-3で未サポート操作の事前検出
    /// </summary>
    private class UnsupportedOperationVisitor : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name;

            switch (methodName)
            {
                case "OrderBy":
                case "OrderByDescending":
                case "ThenBy":
                case "ThenByDescending":
                    throw new NotSupportedException($"ORDER BY operations are not supported in ksqlDB: {methodName}");

                case "Distinct":
                    throw new NotSupportedException("DISTINCT operations are not supported in ksqlDB");

                case "Union":
                case "Intersect":
                case "Except":
                    throw new NotSupportedException($"Set operations are not supported in ksqlDB: {methodName}");
            }

            return base.VisitMethodCall(node);
        }
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

        var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;

        // 修正理由：ForEachAsyncはPush Query（ストリーミング取得）専用として修正
        var ksqlQuery = ToKsql(isPullQuery: false); // Push Query（EMIT CHANGES付き）

        if (_context.Options.EnableDebugLogging)
        {
            Console.WriteLine($"[DEBUG] EventSet.ForEachAsync: {typeof(T).Name} ← Topic: {topicName} (Push型ストリーミング開始)");
            Console.WriteLine($"[DEBUG] Generated KSQL: {ksqlQuery}");
        }

        // 修正理由：バリデーション実行
        ValidateQueryBeforeExecution();

        var consumerService = _context.GetConsumerService();

        try
        {
            // 修正理由：Kafka Consumer のストリームAPIを使用して逐次処理
            await consumerService.SubscribeStreamAsync<T>(
                ksqlQuery,
                _entityModel,
                async (item) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await action(item);
                },
                cancellationToken);

            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] ForEachAsync streaming completed for {typeof(T).Name}");
            }
        }
        catch (KafkaConsumerException ex)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] ForEachAsync streaming error: {ex.Message}");
            }

            throw new InvalidOperationException(
                $"Failed to stream from topic '{topicName}' for {typeof(T).Name}: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] ForEachAsync streaming cancelled for {typeof(T).Name}");
            }
            throw; // キャンセレーション例外はそのまま再スロー
        }
        catch (Exception ex)
        {
            if (_context.Options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Unexpected ForEachAsync error: {ex.Message}");
            }

            throw new InvalidOperationException(
                $"Unexpected error streaming {typeof(T).Name} from topic '{topicName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// LINQ式をKSQLクエリに変換（フラグ制御版）
    /// 修正理由：event_set_interface_design.mdに準拠、Pull/Push判定フラグ追加
    /// </summary>
    /// <param name="isPullQuery">Pull Queryフラグ（true: Pull Query, false: Push Query）</param>
    /// <returns>KSQLクエリ文字列</returns>
    public string ToKsql(bool isPullQuery = false)
    {
        try
        {
            var topicName = _entityModel.TopicAttribute?.TopicName ?? _entityModel.EntityType.Name;
            var translator = new LinqToKsqlTranslator();
            // 修正理由：新しいフラグ制御版Translateメソッドを使用
            return translator.Translate(_expression, topicName, isPullQuery);
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

    private void ValidateEntity(T entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));

        // Required key properties validation
        if (_entityModel.KeyProperties.Length > 0)
        {
            foreach (var keyProperty in _entityModel.KeyProperties)
            {
                var keyValue = keyProperty.GetValue(entity);
                if (keyValue == null)
                {
                    throw new InvalidOperationException(
                        $"Key property '{keyProperty.Name}' cannot be null for entity type '{typeof(T).Name}'");
                }

                // For string keys, check for empty values
                if (keyProperty.PropertyType == typeof(string) && string.IsNullOrEmpty((string)keyValue))
                {
                    throw new InvalidOperationException(
                        $"Key property '{keyProperty.Name}' cannot be empty for entity type '{typeof(T).Name}'");
                }
            }
        }

        // Additional validation based on validation mode
        if (_context.Options.ValidationMode == ValidationMode.Strict)
        {
            ValidateEntityStrict(entity);
        }
    }

    private void ValidateEntityStrict(T entity)
    {
        // Strict validation: Check for required properties, MaxLength constraints, etc.
        var entityType = typeof(T);
        var properties = entityType.GetProperties();

        foreach (var property in properties)
        {
            var value = property.GetValue(entity);

            // MaxLength validation for string properties
            var maxLengthAttr = property.GetCustomAttribute<KsqlDsl.Attributes.MaxLengthAttribute>();
            if (maxLengthAttr != null && value is string stringValue)
            {
                if (stringValue.Length > maxLengthAttr.Length)
                {
                    throw new InvalidOperationException(
                        $"Property '{property.Name}' exceeds maximum length of {maxLengthAttr.Length}. Current length: {stringValue.Length}");
                }
            }
        }
    }

    public override string ToString()
    {
        var topicName = GetTopicName();
        var entityName = typeof(T).Name;
        return $"EventSet<{entityName}> → Topic: {topicName}";
    }
}