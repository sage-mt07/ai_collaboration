# 統合・削除実行計画

## 1. 重複機能の統合・削除（最優先）

### 🔥 **即時削除対象**

#### **1.1 スキーマ生成 - 2つ削除、1つ残存**
```
❌ 削除: src/Serialization/Avro/Core/SchemaGenerator.cs
❌ 削除: src/Serialization/Avro/Core/AvroSchemaGenerator.cs
✅ 残存: src/Serialization/Avro/Core/UnifiedSchemaGenerator.cs（統一実装）
```

#### **1.2 Serializer管理 - 2つ削除、1つ残存**
```
❌ 削除: src/Serialization/Avro/AvroSerializerManager.cs
❌ 削除: src/Serialization/Avro/EnhancedAvroSerializerManager.cs  
✅ 残存: src/Serialization/Avro/Core/AvroSerializerFactory.cs（コア実装）
```

#### **1.3 EventSet実装 - 1つ削除、1つ残存**
```
❌ 削除: src/KafkaContext.cs内のEventSetWithServices<T>クラス
✅ 残存: src/EventSet.cs（Core統合版）
```

#### **1.4 SerializationManager重複 - 1つ削除、1つ統合**
```
❌ 削除: src/Serialization/Avro/AvroSerializationManager.cs
✅ 統合: src/Serialization/Abstractions/AvroSerializationManager.cs → GlobalAvroSerializationManager
```

---

## 2. 未実装参照の最小補完

### 🔧 **必要最小限の実装**

#### **2.1 GlobalAvroSerializationManager実装**
```csharp
// src/Serialization/Avro/Management/GlobalAvroSerializationManager.cs
public class GlobalAvroSerializationManager : IDisposable
{
    private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroSerializerCache _cache;
    private readonly IAvroSchemaRepository _schemaRepository;

    public GlobalAvroSerializationManager(
        ConfluentSchemaRegistry.ISchemaRegistryClient schemaRegistryClient,
        AvroSerializerCache cache,
        IAvroSchemaRepository schemaRepository,
        ILoggerFactory? loggerFactory)
    {
        _schemaRegistryClient = schemaRegistryClient;
        _cache = cache;
        _schemaRepository = schemaRepository;
    }

    public IAvroSerializer<T> GetSerializer<T>() where T : class
    {
        return _cache.GetOrCreateSerializer<T>();
    }

    public IAvroDeserializer<T> GetDeserializer<T>() where T : class
    {
        return _cache.GetOrCreateDeserializer<T>();
    }

    public async Task PreWarmAllCachesAsync()
    {
        var allSchemas = _schemaRepository.GetAllSchemas();
        await _cache.PreWarmAsync(allSchemas);
    }

    public void Dispose() => _cache?.Dispose();
}
```

#### **2.2 Health/Statistics最小実装**
```csharp
// src/Application/KsqlContextHealthReport.cs
public class KsqlContextHealthReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public KsqlContextHealthStatus ContextStatus { get; set; }
    public Dictionary<string, string> ComponentStatus { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public enum KsqlContextHealthStatus { Healthy, Degraded, Unhealthy }

// src/Application/KsqlContextStatistics.cs  
public class KsqlContextStatistics
{
    public int TotalEntities { get; set; }
    public int RegisteredSchemas { get; set; }
    public int StreamEntities { get; set; }
    public int TableEntities { get; set; }
    public int CompositeKeyEntities { get; set; }
    public DateTime? LastInitialized { get; set; }
}
```

---

## 3. Monitoring/Tracingの運用限定化

### 🚫 **初期化パスからMonitoring完全除去**

#### **3.1 Schema登録での修正**
```csharp
// src/Serialization/Avro/ResilientAvroSerializerManager.cs
public async Task<int> RegisterSchemaWithRetryAsync(string subject, string schema)
{
    // ❌ 削除: using var activity = AvroActivitySource.StartSchemaRegistration(subject);
    
    var policy = _retrySettings.SchemaRegistration;
    var attempt = 1;

    while (attempt <= policy.MaxAttempts)
    {
        try
        {
            // ❌ 削除: using var operation = AvroActivitySource.StartCacheOperation("register", subject);
            
            var stopwatch = Stopwatch.StartNew();
            var schemaObj = new Schema(schema, SchemaType.Avro);
            var schemaId = await _schemaRegistryClient.RegisterSchemaAsync(subject, schemaObj);
            stopwatch.Stop();

            // 成功ログ：回数明記
            _logger.LogInformation(
                "Schema registration SUCCESS on attempt {Attempt}: {Subject} (ID: {SchemaId}, Duration: {Duration}ms)",
                attempt, subject, schemaId, stopwatch.ElapsedMilliseconds);

            return schemaId;
        }
        catch (Exception ex) when (ShouldRetry(ex, policy, attempt))
        {
            var delay = CalculateDelay(policy, attempt);

            // 警告ログ：例外種別・タイミング・詳細明記
            _logger.LogWarning(ex,
                "RETRY [{ExceptionType}] at {Timestamp}: Schema registration failed for {Subject} " +
                "(Attempt: {Attempt}/{MaxAttempts}, Next retry in: {Delay}ms) - Exception: {ExceptionMessage}",
                ex.GetType().Name, DateTime.UtcNow, subject, 
                attempt, policy.MaxAttempts, delay.TotalMilliseconds, ex.Message);

            await Task.Delay(delay);
            attempt++;
        }
        catch (Exception ex)
        {
            // 致命的エラー：人間介入必要を明記
            _logger.LogCritical(ex,
                "FATAL: Schema registration failed permanently after {MaxAttempts} attempts for {Subject}. " +
                "HUMAN INTERVENTION REQUIRED. Application will terminate. Exception: {ExceptionMessage}",
                policy.MaxAttempts, subject, ex.Message);

            throw; // 即例外throw = アプリ停止
        }
    }

    // 最大試行到達時の致命的エラー
    var fatalMessage = $"Schema registration exhausted all {policy.MaxAttempts} attempts: {subject}. HUMAN INTERVENTION REQUIRED.";
    _logger.LogCritical(fatalMessage);
    throw new InvalidOperationException(fatalMessage);
}
```

#### **3.2 Cache操作での修正**
```csharp
// src/Serialization/Avro/EnhancedAvroSerializerManager.cs
public async Task<(ISerializer<object>, ISerializer<object>)> CreateSerializersAsync<T>(EntityModel entityModel)
{
    // ❌ 削除: using var activity = AvroActivitySource.StartCacheOperation("create_serializers", typeof(T).Name);
    
    // 初期化フェーズはログのみ、Tracing一切なし
    var (keySchemaId, valueSchemaId) = await RegisterSchemasWithRetryAsync<T>(entityModel);
    // 実装継続...
}
```

### ✅ **運用パスでのみMonitoring有効**
```csharp
// 実運用時のメッセージ送信でのみTracing
public async Task SendAsync<T>(T entity, KafkaMessageContext? context, CancellationToken cancellationToken)
{
    using var activity = AvroActivitySource.StartOperation("send_message", typeof(T).Name); // ← 運用パスのみ
    // 実装...
}

public async Task<KafkaMessage<T>> ConsumeAsync<T>(CancellationToken cancellationToken)
{
    using var activity = AvroActivitySource.StartOperation("consume_message", typeof(T).Name); // ← 運用パスのみ  
    // 実装...
}
```

---

## 4. Namespace/クラス重複の排除

### 🧹 **Namespace統合**

#### **4.1 Configuration統合**
```
❌ 削除: KsqlDsl.Configuration.Abstractions.*
✅ 統合: KsqlDsl.Core.Configuration.*（既存）
```

#### **4.2 Extensions統合**  
```
❌ 削除: KsqlDsl.Core.Extensions.LoggerFactoryExtensions
✅ 移動: KsqlDsl.Infrastructure.Extensions.LoggerFactoryExtensions（新設）
```

#### **4.3 循環参照の解消**
```
修正前:
KsqlDsl.Core.Abstractions.TopicAttribute
     ↓
KsqlDsl.Serialization.Abstractions.AvroEntityConfiguration（TopicAttributeを参照）
     ↓  
KsqlDsl.Core.Models.EntityModel（AvroEntityConfigurationを参照）

修正後:
KsqlDsl.Core.Abstractions.TopicAttribute
     ↓
KsqlDsl.Core.Models.EntityModel（直接TopicAttributeを参照）
     ↓
KsqlDsl.Serialization.Abstractions.AvroEntityConfiguration（EntityModelを参照）
```

---

## 5. 削除対象ファイル一覧

### 📁 **完全削除**
```
src/Serialization/Avro/Core/SchemaGenerator.cs
src/Serialization/Avro/Core/AvroSchemaGenerator.cs
src/Serialization/Avro/AvroSerializerManager.cs
src/Serialization/Avro/EnhancedAvroSerializerManager.cs
src/Serialization/Avro/AvroSerializationManager.cs
src/Configuration/Abstractions/ （フォルダ全体）
```

### 📝 **部分削除（クラス削除）**
```
src/KafkaContext.cs:
  - EventSetWithServices<T>クラス削除
  - 対応するCreateEntitySet実装削除
```

---

## 6. 修正対象ファイル一覧

### 🔧 **機能統合**
```
src/Application/KsqlContext.cs:
  - GlobalAvroSerializationManager参照を正しい実装に修正
  - 削除されたクラスの参照を統合実装に置換

src/EventSet.cs:
  - CreateEntitySet系メソッドをKafkaContextから統合

src/Serialization/Avro/Core/AvroSerializerFactory.cs:
  - 削除された機能を統合・簡素化
```

### 🚫 **Monitoring除去**
```
src/Serialization/Avro/ResilientAvroSerializerManager.cs:
  - 全ActivitySource削除
  - リトライログの詳細化

src/Serialization/Avro/EnhancedAvroSerializerManager.cs:
  - 初期化パスからTracing完全除去
  - または完全削除検討
```

---

## 7. 実行順序

### **Phase 1: 削除（即時）**
1. 重複ファイルの完全削除
2. 重複クラスの部分削除  
3. 不要Namespace削除

### **Phase 2: 統合（即時）**
1. 未実装参照の最小補完
2. 機能統合・置換
3. Namespace整理

### **Phase 3: Monitoring修正（即時）**
1. 初期化パスからTracing除去
2. リトライログ詳細化
3. 運用パス限定の明確化

### **Phase 4: 検証**
1. 循環参照の解消確認
2. 重複排除の完了確認
3. 最小構成での動作確認

---

## 8. 成功基準

### ✅ **達成目標**
- 重複実装 = 0件
- 初期化パスでのMonitoring発動 = 0件  
- 未実装参照 = 0件
- 循環参照 = 0件
- ファイル数 = 20%削減
- クラス数 = 30%削減

### 📊 **測定指標**
- コンパイル成功
- 単体テスト通過  
- 重複検出ツールでの0件確認
- 依存関係解析での循環参照0件確認

**原則**: 一切の機能追加なし。削除・統合・簡素化のみ。