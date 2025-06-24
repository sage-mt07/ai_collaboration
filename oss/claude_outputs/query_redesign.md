# Query層 再設計案（全体アーキテクチャ統合版）

## 1. 全体アーキテクチャでの位置づけ

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Application Layer                              │
├─────────────────────────────────────────────────────────────────────────┤
│  KafkaDbContext                                                         │
│  ├─ OnModelCreating() → SchemaRegistry.RegisterSchema()                 │
│  ├─ OnConfiguring() → ConnectionSetup                                   │
│  └─ EventSet<T> Properties → Query Layer                                │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
┌─────────────────────────────────────────────────────────────────────────┐
│                         Schema Layer                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  SchemaRegistry                                                         │
│  ├─ RegisterSchema<T>() → CREATE STREAM/TABLE generation               │
│  ├─ LINQ Expression → DDL Query mapping                                │
│  └─ StreamTableAnalyzer → Stream/Table type inference                  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
┌─────────────────────────────────────────────────────────────────────────┐
│                         Query Layer (This Design)                      │
├─────────────────────────────────────────────────────────────────────────┤
│  EventSet<T>                                                            │
│  ├─ ToList() → SELECT * FROM <derived_stream_name>                      │
│  ├─ Where() → CREATE STREAM AS SELECT ... WHERE                        │
│  ├─ GroupBy() → CREATE TABLE AS SELECT ... GROUP BY                    │
│  └─ ForEachAsync() → Push Query on derived objects                     │
│                                                                         │
│  QueryExecutionPipeline                                                 │
│  ├─ DerivedObjectManager → Manages temporary streams/tables            │
│  ├─ DDLQueryGenerator → CREATE STREAM/TABLE AS SELECT                  │
│  └─ DMLQueryGenerator → SELECT * FROM operations                       │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
┌─────────────────────────────────────────────────────────────────────────┐
│                       Infrastructure Layer                             │
├─────────────────────────────────────────────────────────────────────────┤
│  KsqlDbExecutor                                                         │
│  ├─ ExecuteDDL() → CREATE STREAM/TABLE execution                       │
│  ├─ ExecutePullQuery() → SELECT for ToList()                           │
│  └─ ExecutePushQuery() → SELECT for ForEachAsync()                     │
└─────────────────────────────────────────────────────────────────────────┘
```

## 2. LINQ式からksqlDBオブジェクト生成フロー

### 2.1 OnModelCreating時のスキーマ登録
```csharp
// Application code
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Event<TradeEvent>(e => {
        e.HasKey(t => t.TradeId);
        e.WithKafkaTopic("trade-events");
        e.AsStream(); // または e.AsTable()
    });
}

// 内部処理フロー
OnModelCreating() 
→ modelBuilder.Event<T>() 
→ SchemaRegistry.RegisterSchema<T>()
→ DDLQueryGenerator.GenerateCreateStatement()
→ KsqlDbExecutor.ExecuteDDL()
```

### 2.2 LINQ式実行時の派生オブジェクト生成
```csharp
// Application code
var filteredTrades = db.TradeEvents
    .Where(t => t.Amount > 1000000)
    .GroupBy(t => t.Symbol);

var result = filteredTrades.ToList();

// 内部処理フロー
.Where() 
→ DerivedObjectManager.CreateDerivedStream()
→ DDLQueryGenerator.GenerateCreateStreamAs()
→ "CREATE STREAM filtered_trades_xxx AS SELECT * FROM trade_events WHERE amount > 1000000"

.GroupBy() 
→ DerivedObjectManager.CreateDerivedTable()
→ DDLQueryGenerator.GenerateCreateTableAs()
→ "CREATE TABLE grouped_trades_yyy AS SELECT symbol, COUNT(*) FROM filtered_trades_xxx GROUP BY symbol"

.ToList() 
→ DMLQueryGenerator.GenerateSelectAll()
→ "SELECT * FROM grouped_trades_yyy"
```

## 3. 新しいアーキテクチャ設計

### 3.1 派生オブジェクト管理
```csharp
public interface IDerivedObjectManager
{
    string CreateDerivedStream(string baseName, Expression linqExpression);
    string CreateDerivedTable(string baseName, Expression linqExpression);
    Task<string> CreateDerivedStreamAsync(string baseName, Expression linqExpression);
    Task<string> CreateDerivedTableAsync(string baseName, Expression linqExpression);
    void CleanupDerivedObjects();
    Task CleanupDerivedObjectsAsync();
}

public class DerivedObjectManager : IDerivedObjectManager
{
    private readonly KsqlDbExecutor _executor;
    private readonly DDLQueryGenerator _ddlGenerator;
    private readonly StreamTableAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, DerivedObjectInfo> _derivedObjects;
    private readonly ILogger _logger;

    public DerivedObjectManager(
        KsqlDbExecutor executor, 
        DDLQueryGenerator ddlGenerator,
        StreamTableAnalyzer analyzer,
        ILogger logger)
    {
        _executor = executor;
        _ddlGenerator = ddlGenerator;
        _analyzer = analyzer;
        _derivedObjects = new ConcurrentDictionary<string, DerivedObjectInfo>();
        _logger = logger;
    }

    public string CreateDerivedStream(string baseName, Expression linqExpression)
    {
        var derivedName = GenerateDerivedName(baseName, "STREAM");
        var createQuery = _ddlGenerator.GenerateCreateStreamAs(derivedName, baseName, linqExpression);
        
        _executor.ExecuteDDL(createQuery);
        
        var derivedInfo = new DerivedObjectInfo
        {
            Name = derivedName,
            Type = DerivedObjectType.Stream,
            BaseObject = baseName,
            Expression = linqExpression,
            CreatedAt = DateTime.UtcNow
        };
        
        _derivedObjects.TryAdd(derivedName, derivedInfo);
        
        _logger.LogDebug("Created derived stream: {DerivedName} from {BaseName}", derivedName, baseName);
        
        return derivedName;
    }

    public string CreateDerivedTable(string baseName, Expression linqExpression)
    {
        var derivedName = GenerateDerivedName(baseName, "TABLE");
        var createQuery = _ddlGenerator.GenerateCreateTableAs(derivedName, baseName, linqExpression);
        
        _executor.ExecuteDDL(createQuery);
        
        var derivedInfo = new DerivedObjectInfo
        {
            Name = derivedName,
            Type = DerivedObjectType.Table,
            BaseObject = baseName,
            Expression = linqExpression,
            CreatedAt = DateTime.UtcNow
        };
        
        _derivedObjects.TryAdd(derivedName, derivedInfo);
        
        _logger.LogDebug("Created derived table: {DerivedName} from {BaseName}", derivedName, baseName);
        
        return derivedName;
    }

    public void CleanupDerivedObjects()
    {
        foreach (var derivedObject in _derivedObjects.Values)
        {
            try
            {
                var dropQuery = $"DROP {derivedObject.Type.ToString().ToUpper()} IF EXISTS {derivedObject.Name}";
                _executor.ExecuteDDL(dropQuery);
                _logger.LogDebug("Dropped derived object: {Name}", derivedObject.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop derived object: {Name}", derivedObject.Name);
            }
        }
        
        _derivedObjects.Clear();
    }

    private string GenerateDerivedName(string baseName, string objectType)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hash = Math.Abs(baseName.GetHashCode()) % 10000;
        return $"{baseName.ToLower()}_{objectType.ToLower()}_{hash}_{timestamp}";
    }
}

public class DerivedObjectInfo
{
    public string Name { get; set; } = string.Empty;
    public DerivedObjectType Type { get; set; }
    public string BaseObject { get; set; } = string.Empty;
    public Expression Expression { get; set; } = Expression.Empty();
    public DateTime CreatedAt { get; set; }
}

public enum DerivedObjectType
{
    Stream,
    Table
}
```

### 3.2 DDLクエリ生成器
```csharp
public interface IDDLQueryGenerator
{
    string GenerateCreateStream(string streamName, string topicName, EntityModel entityModel);
    string GenerateCreateTable(string tableName, string topicName, EntityModel entityModel);
    string GenerateCreateStreamAs(string streamName, string baseObject, Expression linqExpression);
    string GenerateCreateTableAs(string tableName, string baseObject, Expression linqExpression);
}

public class DDLQueryGenerator : IDDLQueryGenerator
{
    private readonly IKsqlBuilder _whereBuilder;
    private readonly IKsqlBuilder _projectionBuilder;
    private readonly IKsqlBuilder _groupByBuilder;
    private readonly StreamTableAnalyzer _analyzer;

    public DDLQueryGenerator()
    {
        _whereBuilder = new SelectBuilder();
        _projectionBuilder = new ProjectionBuilder();
        _groupByBuilder = new GroupByBuilder();
        _analyzer = new StreamTableAnalyzer();
    }

    public string GenerateCreateStream(string streamName, string topicName, EntityModel entityModel)
    {
        var columns = GenerateColumnDefinitions(entityModel);
        return $"CREATE STREAM {streamName} ({columns}) WITH (KAFKA_TOPIC='{topicName}', VALUE_FORMAT='AVRO')";
    }

    public string GenerateCreateTable(string tableName, string topicName, EntityModel entityModel)
    {
        var columns = GenerateColumnDefinitions(entityModel);
        var keyColumns = string.Join(", ", entityModel.KeyProperties.Select(p => p.Name.ToUpper()));
        return $"CREATE TABLE {tableName} ({columns}) WITH (KAFKA_TOPIC='{topicName}', VALUE_FORMAT='AVRO', KEY='{keyColumns}')";
    }

    public string GenerateCreateStreamAs(string streamName, string baseObject, Expression linqExpression)
    {
        var analysis = _analyzer.AnalyzeExpression(linqExpression);
        var selectClause = GenerateSelectClause(linqExpression, analysis);
        var whereClause = GenerateWhereClause(linqExpression, analysis);
        
        var query = new StringBuilder($"CREATE STREAM {streamName} AS SELECT {selectClause} FROM {baseObject}");
        
        if (!string.IsNullOrEmpty(whereClause))
        {
            query.Append($" {whereClause}");
        }
        
        return query.ToString();
    }

    public string GenerateCreateTableAs(string tableName, string baseObject, Expression linqExpression)
    {
        var analysis = _analyzer.AnalyzeExpression(linqExpression);
        var selectClause = GenerateSelectClause(linqExpression, analysis);
        var whereClause = GenerateWhereClause(linqExpression, analysis);
        var groupByClause = GenerateGroupByClause(linqExpression, analysis);
        
        var query = new StringBuilder($"CREATE TABLE {tableName} AS SELECT {selectClause} FROM {baseObject}");
        
        if (!string.IsNullOrEmpty(whereClause))
        {
            query.Append($" {whereClause}");
        }
        
        if (!string.IsNullOrEmpty(groupByClause))
        {
            query.Append($" {groupByClause}");
        }
        
        return query.ToString();
    }

    private string GenerateColumnDefinitions(EntityModel entityModel)
    {
        var columns = new List<string>();
        
        foreach (var property in entityModel.EntityType.GetProperties())
        {
            if (property.GetCustomAttribute<KafkaIgnoreAttribute>() != null)
                continue;
                
            var columnName = property.Name.ToUpper();
            var ksqlType = MapToKsqlType(property.PropertyType);
            columns.Add($"{columnName} {ksqlType}");
        }
        
        return string.Join(", ", columns);
    }

    private string MapToKsqlType(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        
        return underlyingType switch
        {
            Type t when t == typeof(int) => "INTEGER",
            Type t when t == typeof(long) => "BIGINT",
            Type t when t == typeof(double) => "DOUBLE",
            Type t when t == typeof(decimal) => "DECIMAL",
            Type t when t == typeof(string) => "VARCHAR",
            Type t when t == typeof(bool) => "BOOLEAN",
            Type t when t == typeof(DateTime) => "TIMESTAMP",
            Type t when t == typeof(DateTimeOffset) => "TIMESTAMP",
            Type t when t == typeof(Guid) => "VARCHAR",
            Type t when t == typeof(byte[]) => "BYTES",
            _ => "VARCHAR"
        };
    }

    private string GenerateSelectClause(Expression expression, ExpressionAnalysisResult analysis)
    {
        var selectCall = analysis.MethodCalls.LastOrDefault(mc => mc.Method.Name == "Select");
        
        if (selectCall != null)
        {
            var selectExpression = UnwrapLambda(selectCall.Arguments[1]);
            if (selectExpression != null)
            {
                return _projectionBuilder.Build(selectExpression).Replace("SELECT ", "");
            }
        }
        
        return "*";
    }

    private string GenerateWhereClause(Expression expression, ExpressionAnalysisResult analysis)
    {
        var whereCall = analysis.MethodCalls.FirstOrDefault(mc => mc.Method.Name == "Where");
        
        if (whereCall != null)
        {
            var whereExpression = UnwrapLambda(whereCall.Arguments[1]);
            if (whereExpression != null)
            {
                return _whereBuilder.Build(whereExpression);
            }
        }
        
        return string.Empty;
    }

    private string GenerateGroupByClause(Expression expression, ExpressionAnalysisResult analysis)
    {
        var groupByCall = analysis.MethodCalls.FirstOrDefault(mc => mc.Method.Name == "GroupBy");
        
        if (groupByCall != null)
        {
            var groupByExpression = UnwrapLambda(groupByCall.Arguments[1]);
            if (groupByExpression != null)
            {
                return _groupByBuilder.Build(groupByExpression);
            }
        }
        
        return string.Empty;
    }

    private Expression? UnwrapLambda(Expression expression)
    {
        return expression switch
        {
            LambdaExpression lambda => lambda.Body,
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } => lambda.Body,
            _ => null
        };
    }
}
```

### 3.3 DMLクエリ生成器
```csharp
public interface IDMLQueryGenerator
{
    string GenerateSelectAll(string objectName, bool isPullQuery = true);
    string GenerateSelectWithCondition(string objectName, Expression whereExpression, bool isPullQuery = true);
    string GenerateCountQuery(string objectName);
    string GenerateAggregateQuery(string objectName, Expression aggregateExpression);
}

public class DMLQueryGenerator : IDMLQueryGenerator
{
    private readonly IKsqlBuilder _whereBuilder;
    private readonly IKsqlBuilder _projectionBuilder;

    public DMLQueryGenerator()
    {
        _whereBuilder = new SelectBuilder();
        _projectionBuilder = new ProjectionBuilder();
    }

    public string GenerateSelectAll(string objectName, bool isPullQuery = true)
    {
        var query = $"SELECT * FROM {objectName}";
        
        if (!isPullQuery)
        {
            query += " EMIT CHANGES";
        }
        
        return query;
    }

    public string GenerateSelectWithCondition(string objectName, Expression whereExpression, bool isPullQuery = true)
    {
        var whereClause = _whereBuilder.Build(whereExpression);
        var query = $"SELECT * FROM {objectName} {whereClause}";
        
        if (!isPullQuery)
        {
            query += " EMIT CHANGES";
        }
        
        return query;
    }

    public string GenerateCountQuery(string objectName)
    {
        return $"SELECT COUNT(*) FROM {objectName}";
    }

    public string GenerateAggregateQuery(string objectName, Expression aggregateExpression)
    {
        var selectClause = _projectionBuilder.Build(aggregateExpression);
        return $"{selectClause} FROM {objectName}";
    }
}
```

### 3.4 クエリ実行パイプライン
```csharp
public class QueryExecutionPipeline
{
    private readonly DerivedObjectManager _derivedObjectManager;
    private readonly DDLQueryGenerator _ddlGenerator;
    private readonly DMLQueryGenerator _dmlGenerator;
    private readonly KsqlDbExecutor _executor;
    private readonly StreamTableAnalyzer _analyzer;
    private readonly ILogger _logger;

    public QueryExecutionPipeline(
        DerivedObjectManager derivedObjectManager,
        DDLQueryGenerator ddlGenerator,
        DMLQueryGenerator dmlGenerator,
        KsqlDbExecutor executor,
        StreamTableAnalyzer analyzer,
        ILogger logger)
    {
        _derivedObjectManager = derivedObjectManager;
        _ddlGenerator = ddlGenerator;
        _dmlGenerator = dmlGenerator;
        _executor = executor;
        _analyzer = analyzer;
        _logger = logger;
    }

    public async Task<QueryExecutionResult> ExecuteQueryAsync<T>(
        string baseObjectName, 
        Expression linqExpression, 
        QueryExecutionMode mode) where T : class
    {
        _logger.LogDebug("Starting query execution for {BaseObject}", baseObjectName);

        try
        {
            // 1. LINQ式を解析
            var analysis = _analyzer.AnalyzeExpression(linqExpression);
            
            // 2. 必要に応じて派生オブジェクトを作成
            var targetObjectName = await CreateDerivedObjectsIfNeeded(baseObjectName, linqExpression, analysis);
            
            // 3. 最終的なクエリを実行
            var result = await ExecuteFinalQuery<T>(targetObjectName, mode);
            
            return new QueryExecutionResult
            {
                Success = true,
                TargetObject = targetObjectName,
                Data = result,
                ExecutedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query execution failed for {BaseObject}", baseObjectName);
            throw;
        }
    }

    private async Task<string> CreateDerivedObjectsIfNeeded(
        string baseObjectName, 
        Expression linqExpression, 
        ExpressionAnalysisResult analysis)
    {
        var currentObjectName = baseObjectName;
        
        foreach (var methodCall in analysis.MethodCalls)
        {
            switch (methodCall.Method.Name)
            {
                case "Where":
                    currentObjectName = await _derivedObjectManager.CreateDerivedStreamAsync(
                        currentObjectName, methodCall);
                    break;
                    
                case "Select":
                    if (analysis.HasAggregation)
                    {
                        currentObjectName = await _derivedObjectManager.CreateDerivedTableAsync(
                            currentObjectName, methodCall);
                    }
                    else
                    {
                        currentObjectName = await _derivedObjectManager.CreateDerivedStreamAsync(
                            currentObjectName, methodCall);
                    }
                    break;
                    
                case "GroupBy":
                    currentObjectName = await _derivedObjectManager.CreateDerivedTableAsync(
                        currentObjectName, methodCall);
                    break;
            }
        }
        
        return currentObjectName;
    }

    private async Task<List<T>> ExecuteFinalQuery<T>(string objectName, QueryExecutionMode mode) where T : class
    {
        // 最終クエリは常にSELECT * FROM のみ
        // すべての条件は派生オブジェクト作成時に適用済み
        var query = _dmlGenerator.GenerateSelectAll(objectName, mode == QueryExecutionMode.PullQuery);
        
        _logger.LogDebug("Executing final query: {Query}", query);
        
        return mode switch
        {
            QueryExecutionMode.PullQuery => await _executor.ExecutePullQueryAsync<T>(query),
            QueryExecutionMode.PushQuery => await _executor.ExecutePushQueryAsync<T>(query),
            _ => throw new ArgumentException($"Unsupported execution mode: {mode}")
        };
    }
}

public class QueryExecutionResult
{
    public bool Success { get; set; }
    public string TargetObject { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum QueryExecutionMode
{
    PullQuery,
    PushQuery
}
```

### 3.5 EventSet再設計
```csharp
public class EventSet<T> : IEventSetRead<T>, IEventSetWrite<T>, IEventSetQuery<T>
    where T : class
{
    private readonly QueryExecutionPipeline _pipeline;
    private readonly EventCommandService<T> _commandService;
    private readonly string _baseObjectName;
    private readonly Expression _expression;
    private readonly IQueryProvider _queryProvider;

    internal EventSet(
        KafkaContext context, 
        EntityModel entityModel, 
        QueryExecutionPipeline pipeline,
        string baseObjectName)
    {
        _pipeline = pipeline;
        _commandService = new EventCommandService<T>(context, entityModel);
        _baseObjectName = baseObjectName;
        _expression = Expression.Constant(this);
        _queryProvider = new EventQueryProvider<T>(context, entityModel);
    }

    internal EventSet(
        QueryExecutionPipeline pipeline,
        EventCommandService<T> commandService,
        string baseObjectName,
        Expression expression,
        IQueryProvider queryProvider)
    {
        _pipeline = pipeline;
        _commandService = commandService;
        _baseObjectName = baseObjectName;
        _expression = expression;
        _queryProvider = queryProvider;
    }

    // IEventSetRead implementation
    public List<T> ToList()
    {
        var result = _pipeline.ExecuteQueryAsync<T>(_baseObjectName, _expression, QueryExecutionMode.PullQuery)
            .GetAwaiter().GetResult();
        return (List<T>)result.Data!;
    }

    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var result = await _pipeline.ExecuteQueryAsync<T>(_baseObjectName, _expression, QueryExecutionMode.PullQuery);
        return (List<T>)result.Data!;
    }

    public T? FirstOrDefault()
    {
        // Take(1)を追加して実行
        var takeExpression = Expression.Call(
            typeof(Queryable), nameof(Queryable.Take),
            new[] { typeof(T) }, _expression, Expression.Constant(1));
            
        var result = _pipeline.ExecuteQueryAsync<T>(_baseObjectName, takeExpression, QueryExecutionMode.PullQuery)
            .GetAwaiter().GetResult();
        var items = (List<T>)result.Data!;
        return items.FirstOrDefault();
    }

    // IEventSetWrite implementation
    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        return _commandService.AddAsync(entity, cancellationToken);
    }

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        return _commandService.AddRangeAsync(entities, cancellationToken);
    }

    // IEventSetQuery implementation
    public IEventSetRead<T> Where(Expression<Func<T, bool>> predicate)
    {
        var methodCall = Expression.Call(
            typeof(Queryable), nameof(Queryable.Where),
            new[] { typeof(T) }, _expression, Expression.Quote(predicate));

        return new EventSetReadOnly<T>(_pipeline, _commandService, _baseObjectName, methodCall, _queryProvider);
    }

    public IEventSetRead<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class
    {
        var methodCall = Expression.Call(
            typeof(Queryable), nameof(Queryable.Select),
            new[] { typeof(T), typeof(TResult) }, _expression, Expression.Quote(selector));

        return new EventSetReadOnly<TResult>(
            _pipeline, 
            new EventCommandService<TResult>(_commandService.Context, _commandService.EntityModel), 
            _baseObjectName, 
            methodCall, 
            _queryProvider);
    }

    public IGroupedEventSet<TKey, T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var methodCall = Expression.Call(
            typeof(Queryable), nameof(Queryable.GroupBy),
            new[] { typeof(T), typeof(TKey) }, _expression, Expression.Quote(keySelector));

        return new GroupedEventSet<TKey, T>(_pipeline, _baseObjectName, methodCall);
    }

    // Streaming operations
    public void Subscribe(Action<T> onNext, CancellationToken cancellationToken = default)
    {
        Task.Run(async () =>
        {
            try
            {
                await ForEachAsync(item => { onNext(item); return Task.CompletedTask; }, 
                    TimeSpan.Zero, cancellationToken);
            }
            catch (OperationCanceledException) { }
        }, cancellationToken);
    }

    public async Task SubscribeAsync(Func<T, Task> onNext, CancellationToken cancellationToken = default)
    {
        await ForEachAsync(onNext, TimeSpan.Zero, cancellationToken);
    }

    public async Task ForEachAsync(Func<T, Task> action, TimeSpan timeout = default, 
        CancellationToken cancellationToken = default)
    {
        // Push Query mode でストリーミング実行
        var result = await _pipeline.ExecuteQueryAsync<T>(_baseObjectName, _expression, QueryExecutionMode.PushQuery);
        
        // ストリーミングデータの処理
        var streamData = (IAsyncEnumerable<T>)result.Data!;
        
        await foreach (var item in streamData.WithCancellation(cancellationToken))
        {
            await action(item);
        }
    }

    // IQueryable implementation
    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _queryProvider;

    // IEnumerable implementation
    public IEnumerator<T> GetEnumerator() => ToList().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // IAsyncEnumerable implementation
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var items = await ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
            yield return item;
        }
    }

    public string ToKsql(bool isPullQuery = false)
    {
        // 実際のクエリ実行パスと同じロジックでKSQL生成
        return _pipeline.GenerateKsqlQuery(_baseObjectName, _expression, isPullQuery);
    }
}
```

## 4. スキーマ登録との連携

### 4.1 スキーマレジストリ統合
```csharp
public class SchemaRegistry
{
    private readonly DDLQueryGenerator _ddlGenerator;
    private readonly KsqlDbExecutor _executor;
    private readonly ConcurrentDictionary<Type, string> _registeredSchemas;

    public async Task RegisterSchemaAsync<T>(EntityModel entityModel) where T : class
    {
        var entityType = typeof(T);
        
        if (_registeredSchemas.ContainsKey(entityType))
            return; // Already registered

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;
        var objectName = GenerateObjectName<T>();
        
        string createQuery;
        if (entityModel.StreamTableType == StreamTableType.Stream)
        {
            createQuery = _ddlGenerator.GenerateCreateStream(objectName, topicName, entityModel);
        }
        else
        {
            createQuery = _ddlGenerator.GenerateCreateTable(objectName, topicName, entityModel);
        }

        await _executor.ExecuteDDLAsync(createQuery);
        _registeredSchemas.TryAdd(entityType, objectName);
    }

    public string GetObjectName<T>() where T : class
    {
        var entityType = typeof(T);
        return _registeredSchemas.TryGetValue(entityType, out var name) ? name : throw new InvalidOperationException($"Schema not registered for {entityType.Name}");
    }

    private string GenerateObjectName<T>() where T : class
    {
        var entityName = typeof(T).Name;
        return $"{entityName.ToLower()}_base";
    }
}
```

### 4.2 KafkaDbContext統合
```csharp
public abstract class KafkaDbContext : IDisposable
{
    private readonly SchemaRegistry _schemaRegistry;
    private readonly QueryExecutionPipeline _pipeline;
    private readonly Dictionary<Type, object> _eventSets;
    private readonly ModelBuilder _modelBuilder;
    private readonly KafkaContextOptions _options;
    private bool _isInitialized = false;

    protected KafkaDbContext()
    {
        _options = new KafkaContextOptions();
        OnConfiguring(_options);
        
        _schemaRegistry = new SchemaRegistry(CreateKsqlExecutor(), CreateDDLGenerator());
        _pipeline = CreateQueryPipeline();
        _eventSets = new Dictionary<Type, object>();
        _modelBuilder = new ModelBuilder();
        
        // Configure models
        OnModelCreating(_modelBuilder);
        
        // Initialize schemas and EventSet properties
        InitializeAsync().GetAwaiter().GetResult();
    }

    protected abstract void OnConfiguring(KafkaContextOptions options);
    protected abstract void OnModelCreating(ModelBuilder modelBuilder);

    private async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // 1. Register all schemas first
        await RegisterAllSchemasAsync();
        
        // 2. Initialize EventSet properties
        InitializeEventSetProperties();
        
        _isInitialized = true;
    }

    private async Task RegisterAllSchemasAsync()
    {
        foreach (var entityModel in _modelBuilder.GetEntityModels())
        {
            await _schemaRegistry.RegisterSchemaAsync(entityModel);
        }
    }

    private void InitializeEventSetProperties()
    {
        var properties = GetType().GetProperties()
            .Where(p => p.PropertyType.IsGenericType && 
                       p.PropertyType.GetGenericTypeDefinition() == typeof(EventSet<>));

        foreach (var property in properties)
        {
            var entityType = property.PropertyType.GetGenericArguments()[0];
            var entityModel = _modelBuilder.GetEntityModel(entityType);
            var baseObjectName = _schemaRegistry.GetObjectName(entityType);
            
            var eventSetType = typeof(EventSet<>).MakeGenericType(entityType);
            var eventSet = Activator.CreateInstance(eventSetType, this, entityModel, _pipeline, baseObjectName);
            
            property.SetValue(this, eventSet);
            _eventSets[entityType] = eventSet!;
        }
    }

    public EventSet<T> Set<T>() where T : class
    {
        var entityType = typeof(T);
        if (_eventSets.TryGetValue(entityType, out var eventSet))
        {
            return (EventSet<T>)eventSet;
        }

        var entityModel = _modelBuilder.GetEntityModel(entityType);
        var baseObjectName = _schemaRegistry.GetObjectName(entityType);
        var newEventSet = new EventSet<T>(this, entityModel, _pipeline, baseObjectName);
        
        _eventSets[entityType] = newEventSet;
        return newEventSet;
    }

    private QueryExecutionPipeline CreateQueryPipeline()
    {
        var executor = CreateKsqlExecutor();
        var ddlGenerator = CreateDDLGenerator();
        var dmlGenerator = new DMLQueryGenerator();
        var analyzer = new StreamTableAnalyzer();
        var logger = CreateLogger();
        var derivedObjectManager = new DerivedObjectManager(executor, ddlGenerator, analyzer, logger);

        return new QueryExecutionPipeline(derivedObjectManager, ddlGenerator, dmlGenerator, executor, analyzer, logger);
    }

    private KsqlDbExecutor CreateKsqlExecutor()
    {
        return new KsqlDbExecutor(_options.KsqlDbUrl, CreateLogger());
    }

    private DDLQueryGenerator CreateDDLGenerator()
    {
        return new DDLQueryGenerator();
    }

    private ILogger CreateLogger()
    {
        return _options.EnableDebugLogging ? new ConsoleLogger() : NullLogger.Instance;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _schemaRegistry?.Dispose();
    }
}
```

## 5. 実行フロー詳細

### 5.1 初期化フロー
```
1. KafkaDbContext constructor
   ↓
2. OnConfiguring() - 接続設定
   ↓
3. OnModelCreating() - モデル定義
   ↓
4. RegisterAllSchemasAsync()
   ├─ TradeEvent → CREATE STREAM trade_events_base
   ├─ OrderEvent → CREATE TABLE order_events_base  
   └─ ...
   ↓
5. InitializeEventSetProperties()
   ├─ TradeEvents property → EventSet<TradeEvent>
   └─ OrderEvents property → EventSet<OrderEvent>
```

### 5.2 クエリ実行フロー (ToList)
```
db.TradeEvents.Where(t => t.Amount > 1000).ToList()
   ↓
1. Where() → new EventSetReadOnly with MethodCallExpression
   ↓
2. ToList() → QueryExecutionPipeline.ExecuteQueryAsync()
   ↓
3. AnalyzeExpression() → detect Where operation
   ↓
4. CreateDerivedStreamAsync()
   ├─ Generate: "CREATE STREAM trade_events_stream_1234_567890 AS 
   │            SELECT * FROM trade_events_base WHERE amount > 1000"
   └─ Execute DDL
   ↓
5. ExecuteFinalQuery()
   ├─ Generate: "SELECT * FROM trade_events_stream_1234_567890"
   └─ Execute Pull Query
   ↓
6. Map results to List<TradeEvent>
```

### 5.3 ストリーミング実行フロー (ForEachAsync)
```
db.TradeEvents.Where(t => t.Symbol == "USD/JPY").ForEachAsync(t => Console.WriteLine(t))
   ↓
1. Where() → new EventSetReadOnly with MethodCallExpression
   ↓
2. ForEachAsync() → QueryExecutionPipeline.ExecuteQueryAsync(PushQuery)
   ↓
3. AnalyzeExpression() → detect Where operation
   ↓
4. CreateDerivedStreamAsync()
   ├─ Generate: "CREATE STREAM trade_events_stream_5678_123456 AS 
   │            SELECT * FROM trade_events_base WHERE symbol = 'USD/JPY'"
   └─ Execute DDL
   ↓
5. ExecuteFinalQuery() with Push Query
   ├─ Generate: "SELECT * FROM trade_events_stream_5678_123456 EMIT CHANGES"
   └─ Execute Push Query (streaming)
   ↓
6. Stream each result to action callback
```

### 5.4 集約クエリ実行フロー
```
db.TradeEvents.GroupBy(t => t.Symbol).Select(g => new { Symbol = g.Key, Count = g.Count() }).ToList()
   ↓
1. GroupBy() → new GroupedEventSet with GroupBy expression
   ↓
2. Select() → new EventSetReadOnly with Select expression
   ↓
3. ToList() → QueryExecutionPipeline.ExecuteQueryAsync()
   ↓
4. AnalyzeExpression() → detect GroupBy + Select operations
   ↓
5. CreateDerivedTableAsync() for GroupBy
   ├─ Generate: "CREATE TABLE trade_events_table_9999_111111 AS 
   │            SELECT symbol, COUNT(*) as count FROM trade_events_base GROUP BY symbol"
   └─ Execute DDL
   ↓
6. ExecuteFinalQuery()
   ├─ Generate: "SELECT * FROM trade_events_table_9999_111111"
   └─ Execute Pull Query
   ↓
7. Map results to aggregated objects
```

## 6. StreamTableAnalyzer拡張

```csharp
public class StreamTableAnalyzer
{
    private readonly Dictionary<string, OperationCharacteristics> _operationMap;

    public StreamTableAnalyzer()
    {
        _operationMap = new Dictionary<string, OperationCharacteristics>
        {
            ["Where"] = new OperationCharacteristics 
            { 
                ResultType = StreamTableType.Stream, 
                RequiresDerivedObject = true,
                CanChain = true 
            },
            ["Select"] = new OperationCharacteristics 
            { 
                ResultType = StreamTableType.Stream, 
                RequiresDerivedObject = true,
                CanChain = true,
                DependsOnAggregation = true
            },
            ["GroupBy"] = new OperationCharacteristics 
            { 
                ResultType = StreamTableType.Table, 
                RequiresDerivedObject = true,
                CanChain = false,
                IsAggregation = true
            },
            ["Take"] = new OperationCharacteristics 
            { 
                ResultType = StreamTableType.Stream, 
                RequiresDerivedObject = false,
                CanChain = false
            },
            ["Skip"] = new OperationCharacteristics 
            { 
                ResultType = StreamTableType.Stream, 
                RequiresDerivedObject = false,
                CanChain = false
            }
        };
    }

    public ExpressionAnalysisResult AnalyzeExpression(Expression expression)
    {
        var visitor = new ExpressionAnalysisVisitor(_operationMap);
        visitor.Visit(expression);
        
        return new ExpressionAnalysisResult
        {
            MethodCalls = visitor.MethodCalls,
            HasAggregation = visitor.HasAggregation,
            HasGroupBy = visitor.HasGroupBy,
            RequiresStreamOutput = visitor.RequiresStreamOutput,
            RequiresTableOutput = visitor.RequiresTableOutput,
            OperationChain = visitor.OperationChain
        };
    }

    private class ExpressionAnalysisVisitor : ExpressionVisitor
    {
        private readonly Dictionary<string, OperationCharacteristics> _operationMap;
        
        public List<MethodCallExpression> MethodCalls { get; } = new();
        public List<OperationInfo> OperationChain { get; } = new();
        public bool HasAggregation { get; private set; }
        public bool HasGroupBy { get; private set; }
        public bool RequiresStreamOutput { get; private set; } = true;
        public bool RequiresTableOutput { get; private set; }

        public ExpressionAnalysisVisitor(Dictionary<string, OperationCharacteristics> operationMap)
        {
            _operationMap = operationMap;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name;
            MethodCalls.Add(node);

            if (_operationMap.TryGetValue(methodName, out var characteristics))
            {
                var operationInfo = new OperationInfo
                {
                    MethodName = methodName,
                    Expression = node,
                    Characteristics = characteristics,
                    Order = OperationChain.Count
                };

                OperationChain.Add(operationInfo);

                // Update analysis state
                if (characteristics.IsAggregation)
                {
                    HasAggregation = true;
                }

                if (methodName == "GroupBy")
                {
                    HasGroupBy = true;
                    RequiresTableOutput = true;
                    RequiresStreamOutput = false;
                }

                // Select after GroupBy should produce Table
                if (methodName == "Select" && HasGroupBy)
                {
                    RequiresTableOutput = true;
                    RequiresStreamOutput = false;
                }
            }

            return base.VisitMethodCall(node);
        }
    }
}

public class OperationCharacteristics
{
    public StreamTableType ResultType { get; set; }
    public bool RequiresDerivedObject { get; set; }
    public bool CanChain { get; set; }
    public bool IsAggregation { get; set; }
    public bool DependsOnAggregation { get; set; }
}

public class OperationInfo
{
    public string MethodName { get; set; } = string.Empty;
    public MethodCallExpression Expression { get; set; } = null!;
    public OperationCharacteristics Characteristics { get; set; } = null!;
    public int Order { get; set; }
}
```

## 7. 使用例とクエリ生成

### 7.1 シンプルなフィルタリング
```csharp
// Application Code
var highValueTrades = await db.TradeEvents
    .Where(t => t.Amount > 1000000)
    .ToListAsync();

// Generated Queries
// 1. CREATE STREAM trade_events_stream_1234 AS 
//    SELECT * FROM trade_events_base WHERE amount > 1000000
// 2. SELECT * FROM trade_events_stream_1234
```

### 7.2 複合条件
```csharp
// Application Code
var filteredTrades = await db.TradeEvents
    .Where(t => t.Amount > 1000000)
    .Where(t => t.Symbol == "USD/JPY")
    .Select(t => new { t.TradeId, t.Amount, t.Timestamp })
    .ToListAsync();

// Generated Queries
// 1. CREATE STREAM trade_events_stream_1234 AS 
//    SELECT * FROM trade_events_base WHERE amount > 1000000
// 2. CREATE STREAM trade_events_stream_5678 AS 
//    SELECT * FROM trade_events_stream_1234 WHERE symbol = 'USD/JPY'
// 3. CREATE STREAM trade_events_stream_9999 AS 
//    SELECT tradeid, amount, timestamp FROM trade_events_stream_5678
// 4. SELECT * FROM trade_events_stream_9999
```

### 7.3 集約処理
```csharp
// Application Code
var symbolVolume = await db.TradeEvents
    .GroupBy(t => t.Symbol)
    .Select(g => new SymbolVolume 
    { 
        Symbol = g.Key, 
        TotalVolume = g.Sum(t => t.Amount),
        TradeCount = g.Count()
    })
    .ToListAsync();

// Generated Queries
// 1. CREATE TABLE trade_events_table_1111 AS 
//    SELECT symbol, SUM(amount) as totalvolume, COUNT(*) as tradecount 
//    FROM trade_events_base 
//    GROUP BY symbol
// 2. SELECT * FROM trade_events_table_1111
```

### 7.4 ストリーミング処理
```csharp
// Application Code
await db.TradeEvents
    .Where(t => t.Symbol == "USD/JPY")
    .ForEachAsync(trade => 
    {
        Console.WriteLine($"New USD/JPY trade: {trade.Amount}");
        return Task.CompletedTask;
    });

// Generated Queries
// 1. CREATE STREAM trade_events_stream_2222 AS 
//    SELECT * FROM trade_events_base WHERE symbol = 'USD/JPY'
// 2. SELECT * FROM trade_events_stream_2222 EMIT CHANGES
```

## 8. クリーンアップとリソース管理

### 8.1 自動クリーンアップ
```csharp
public class DerivedObjectCleanupService : IDisposable
{
    private readonly DerivedObjectManager _derivedObjectManager;
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _cleanupInterval;

    public DerivedObjectCleanupService(DerivedObjectManager derivedObjectManager, TimeSpan? cleanupInterval = null)
    {
        _derivedObjectManager = derivedObjectManager;
        _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(30);
        _cleanupTimer = new Timer(PerformCleanup, null, _cleanupInterval, _cleanupInterval);
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            _derivedObjectManager.CleanupExpiredObjects(TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            // Log cleanup errors
            Console.WriteLine($"Cleanup error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _derivedObjectManager?.CleanupDerivedObjects();
    }
}
```

### 8.2 コンテキスト終了時の処理
```csharp
public abstract class KafkaDbContext : IDisposable
{
    private readonly DerivedObjectCleanupService _cleanupService;

    // ... existing code ...

    public async Task DisposeAsync()
    {
        // Graceful shutdown of streaming queries
        await _pipeline.StopAllStreamingQueriesAsync();
        
        // Cleanup derived objects
        await _derivedObjectManager.CleanupDerivedObjectsAsync();
        
        // Dispose resources
        _cleanupService?.Dispose();
        _pipeline?.Dispose();
        _schemaRegistry?.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}
```

## 9. エラーハンドリングとデバッグ

### 9.1 詳細なエラー情報
```csharp
public class QueryExecutionException : Exception
{
    public string BaseObject { get; }
    public Expression LinqExpression { get; }
    public string GeneratedKsql { get; }
    public QueryExecutionPhase Phase { get; }

    public QueryExecutionException(
        string message, 
        string baseObject, 
        Expression linqExpression, 
        string generatedKsql, 
        QueryExecutionPhase phase, 
        Exception? innerException = null) 
        : base(message, innerException)
    {
        BaseObject = baseObject;
        LinqExpression = linqExpression;
        GeneratedKsql = generatedKsql;
        Phase = phase;
    }
}

public enum QueryExecutionPhase
{
    ExpressionAnalysis,
    DerivedObjectCreation,
    QueryGeneration,
    QueryExecution,
    ResultMapping
}
```

### 9.2 デバッグサポート
```csharp
public static class EventSetDebugExtensions
{
    public static QueryExecutionPlan GetExecutionPlan<T>(this EventSet<T> eventSet) where T : class
    {
        // Return detailed execution plan without executing
        return eventSet.Pipeline.GenerateExecutionPlan(eventSet.BaseObjectName, eventSet.Expression);
    }

    public static async Task<QueryDebugInfo> ExecuteWithDebugAsync<T>(this EventSet<T> eventSet) where T : class
    {
        // Execute with detailed timing and query information
        var stopwatch = Stopwatch.StartNew();
        
        var result = await eventSet.ToListAsync();
        
        return new QueryDebugInfo
        {
            ExecutionTime = stopwatch.Elapsed,
            GeneratedQueries = eventSet.GetGeneratedQueries(),
            IntermediateObjects = eventSet.GetIntermediateObjects(),
            ResultCount = result.Count
        };
    }
}
```

この再設計により、LINQ式から自動的にksqlDBのSTREAM/TABLEオブジェクトが生成され、効率的なクエリ実行が可能になります。スキーマ登録からクエリ実行まで一貫したアーキテクチャで、保守性と拡張性を大幅に向上させています。