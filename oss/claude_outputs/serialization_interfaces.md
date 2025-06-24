# Serialization インターフェース仕様

## 抽象層インターフェース

### ISerializationManager<T>

型安全なシリアライゼーション管理の統一インターフェース

```csharp
public interface ISerializationManager<T> : IDisposable where T : class
{
    Task<SerializerPair<T>> GetSerializersAsync(CancellationToken cancellationToken = default);
    Task<DeserializerPair<T>> GetDeserializersAsync(CancellationToken cancellationToken = default);
    Task<bool> ValidateRoundTripAsync(T entity, CancellationToken cancellationToken = default);
    SerializationStatistics GetStatistics();
    Type EntityType { get; }
    SerializationFormat Format { get; }
}
```

**使用例:**
```csharp
var manager = new AvroSerializationManager<OrderEntity>(schemaRegistryClient);
var serializers = await manager.GetSerializersAsync();
var isValid = await manager.ValidateRoundTripAsync(order);
```

### IAvroSchemaProvider

スキーマ生成・提供の抽象化

```csharp
public interface IAvroSchemaProvider
{
    Task<string> GetKeySchemaAsync<T>() where T : class;
    Task<string> GetValueSchemaAsync<T>() where T : class;
    Task<(string keySchema, string valueSchema)> GetSchemasAsync<T>() where T : class;
    Task<bool> ValidateSchemaAsync(string schema);
}
```

**使用例:**
```csharp
var provider = new AvroSchemaBuilder();
var (keySchema, valueSchema) = await provider.GetSchemasAsync<OrderEntity>();
var isValid = await provider.ValidateSchemaAsync(schema);
```

### ISchemaVersionResolver

スキーマバージョン管理の抽象化

```csharp
public interface ISchemaVersionResolver
{
    Task<int> ResolveKeySchemaVersionAsync<T>() where T : class;
    Task<int> ResolveValueSchemaVersionAsync<T>() where T : class;
    Task<bool> CanUpgradeAsync<T>(string newSchema) where T : class;
    Task<SchemaUpgradeResult> UpgradeAsync<T>() where T : class;
}
```

**使用例:**
```csharp
var resolver = new AvroSchemaVersionManager(schemaRegistryClient);
var canUpgrade = await resolver.CanUpgradeAsync<OrderEntity>(newSchema);
var result = await resolver.UpgradeAsync<OrderEntity>();
```

## データ転送オブジェクト

### SerializerPair<T>

```csharp
public class SerializerPair<T> where T : class
{
    public ISerializer<object> KeySerializer { get; set; }
    public ISerializer<object> ValueSerializer { get; set; }
    public int KeySchemaId { get; set; }
    public int ValueSchemaId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### DeserializerPair<T>

```csharp
public class DeserializerPair<T> where T : class
{
    public IDeserializer<object> KeyDeserializer { get; set; }
    public IDeserializer<object> ValueDeserializer { get; set; }
    public int KeySchemaId { get; set; }
    public int ValueSchemaId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### SerializationStatistics

```csharp
public class SerializationStatistics
{
    public long TotalSerializations { get; set; }
    public long TotalDeserializations { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRate => TotalSerializations > 0 ? (double)CacheHits / TotalSerializations : 0.0;
    public TimeSpan AverageLatency { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

### SchemaUpgradeResult

```csharp
public class SchemaUpgradeResult
{
    public bool Success { get; set; }
    public string? Reason { get; set; }
    public int? NewKeySchemaId { get; set; }
    public int? NewValueSchemaId { get; set; }
    public DateTime UpgradedAt { get; set; }
}
```

## 列挙型

### SerializationFormat

```csharp
public enum SerializationFormat
{
    Avro,
    Json,
    Protobuf
}
```

## 実装クラス対応表

| インターフェース | Avro実装 | 将来のJSON実装 |
|------------------|----------|----------------|
| ISerializationManager<T> | AvroSerializationManager<T> | JsonSerializationManager<T> |
| IAvroSchemaProvider | AvroSchemaBuilder | JsonSchemaBuilder |
| ISchemaVersionResolver | AvroSchemaVersionManager | JsonSchemaVersionManager |

## 使用パターン

### 基本的な使用

```csharp
// 1. マネージャー作成
var manager = new AvroSerializationManager<OrderEntity>(schemaRegistryClient);

// 2. シリアライザー取得
var serializers = await manager.GetSerializersAsync();

// 3. シリアライゼーション
var context = new SerializationContext(MessageComponentType.Value, "orders");
var data = serializers.ValueSerializer.Serialize(order, context);

// 4. デシリアライゼーション
var deserializers = await manager.GetDeserializersAsync();
var deserializedOrder = deserializers.ValueDeserializer.Deserialize(data, false, context);
```

### キャッシュ付き使用

```csharp
// キャッシュマネージャー経由
var cache = new AvroSerializerCache(factory);
var manager = cache.GetManager<OrderEntity>();

// キャッシュヒット率確認
var stats = manager.GetStatistics();
Console.WriteLine($"Cache Hit Rate: {stats.HitRate:P2}");
```

### スキーマ進化対応

```csharp
// 1. アップグレード可能性確認
var canUpgrade = await manager.CanUpgradeSchemaAsync();

// 2. アップグレード実行
if (canUpgrade)
{
    var result = await manager.UpgradeSchemaAsync();
    if (result.Success)
    {
        Console.WriteLine($"Schema upgraded: Key={result.NewKeySchemaId}, Value={result.NewValueSchemaId}");
    }
}
```

### Round-trip検証

```csharp
// データ整合性確認
var order = new OrderEntity { OrderId = 123, CustomerName = "John Doe" };
var isValid = await manager.ValidateRoundTripAsync(order);

if (!isValid)
{
    throw new InvalidOperationException("Serialization round-trip failed");
}
```

## エラーハンドリング

### 一般的な例外

- `ArgumentNullException`: null引数
- `InvalidOperationException`: 不正な状態・操作
- `SchemaRegistryException`: Schema Registry関連エラー
- `SerializationException`: シリアライゼーション失敗

### エラー処理パターン

```csharp
try
{
    var serializers = await manager.GetSerializersAsync();
}
catch (SchemaRegistryException ex)
{
    // Schema Registry接続エラー
    logger.LogError(ex, "Schema Registry unavailable");
    // フォールバック処理
}
catch (InvalidOperationException ex)
{
    // 設定・状態エラー
    logger.LogError(ex, "Invalid serialization state");
    throw;
}
```

## パフォーマンス考慮事項

### キャッシュ戦略
- シリアライザーは型別にキャッシュ
- 統計情報はthread-safe
- 定期的なキャッシュクリア推奨

### 最適化ポイント
- 同一型の繰り返し使用でキャッシュ効果最大
- Round-trip検証は開発時のみ推奨
- 統計情報は監視用途で活用