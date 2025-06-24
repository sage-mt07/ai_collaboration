# Serialization設計ガイドライン

## 設計原則

### 1. 単一責任原則 (SRP)
各クラスは一つの責務のみを持つ

**良い例:**
```csharp
// ✅ シリアライザー生成のみに専念
public class AvroSerializerFactory
{
    public async Task<SerializerPair<T>> CreateSerializersAsync<T>(EntityModel entityModel)
    // キャッシュ・統計・バージョン管理は他クラスに委譲
}
```

**悪い例:**
```csharp
// ❌ 複数責務が混在
public class AvroManager
{
    public ISerializer CreateSerializer() { } // 生成
    public void CacheSerializer() { }         // キャッシュ
    public bool CheckVersion() { }            // バージョン管理
    public Statistics GetStats() { }          // 統計
}
```

### 2. 依存関係逆転原則 (DIP)
抽象に依存し、具象に依存しない

**良い例:**
```csharp
// ✅ インターフェースに依存
public class OrderService
{
    private readonly ISerializationManager<OrderEntity> _serializer;
    
    public OrderService(ISerializationManager<OrderEntity> serializer)
    {
        _serializer = serializer; // Avro/JSON/Protobuf どれでも対応
    }
}
```

**悪い例:**
```csharp
// ❌ 具象クラスに直接依存
public class OrderService
{
    private readonly AvroSerializationManager<OrderEntity> _serializer;
    // Avroに強く結合、他形式への切り替えが困難
}
```

### 3. オープン・クローズド原則 (OCP)
拡張に開いて、修正に閉じる

**良い例:**
```csharp
// ✅ 新しい形式追加時も既存コードは無修正
public interface ISerializationManager<T> { }
public class AvroSerializationManager<T> : ISerializationManager<T> { }
public class JsonSerializationManager<T> : ISerializationManager<T> { } // 新規追加
```

## アーキテクチャ設計

### 層構造設計

```
┌─────────────────────────────────────┐
│        Application Layer            │ ← ビジネスロジック
├─────────────────────────────────────┤
│        Abstraction Layer            │ ← インターフェース定義
├─────────────────────────────────────┤
│ Management │  Cache  │    Core     │ ← 機能別実装層
├─────────────────────────────────────┤
│         Internal Layer              │ ← 共通ユーティリティ
└─────────────────────────────────────┘
```

### 依存関係ルール

1. **上位層から下位層**: OK
2. **下位層から上位層**: NG
3. **同一層内**: 最小限
4. **Internal層**: 全層からアクセス可能

```csharp
// ✅ 正しい依存方向
Management → Core → Internal
Cache → Core → Internal
Application → Abstraction → Management/Cache

// ❌ 禁止される依存方向
Core → Management
Internal → Core
Abstraction → Management (具象への依存)
```

## 命名規約

### 名前空間

```csharp
// 基本構造
KsqlDsl.Serialization                    // 基底名前空間
KsqlDsl.Serialization.Abstractions       // 抽象層
KsqlDsl.Serialization.Avro              // Avro実装
KsqlDsl.Serialization.Avro.Core         // Core層
KsqlDsl.Serialization.Avro.Cache        // Cache層
KsqlDsl.Serialization.Avro.Management   // Management層
KsqlDsl.Serialization.Avro.Internal     // Internal層

// 将来拡張
KsqlDsl.Serialization.Json              // JSON実装
KsqlDsl.Serialization.Protobuf          // Protobuf実装
```

### クラス名規約

| 種別 | 命名パターン | 例 |
|------|-------------|-----|
| インターフェース | I + 責務 + Manager/Provider/Resolver | ISerializationManager |
| ファクトリ | 形式 + 対象 + Factory | AvroSerializerFactory |
| キャッシュ | 形式 + 対象 + Cache | AvroSerializerCache |
| 管理クラス | 形式 + 対象 + Manager | AvroSchemaVersionManager |
| ビルダー | 形式 + 対象 + Builder | AvroSchemaBuilder |
| ユーティリティ | 形式 + Utils | AvroUtils |

### メソッド名規約

```csharp
// 非同期メソッド: Async接尾辞
Task<T> GetSerializersAsync()
Task<bool> ValidateRoundTripAsync()

// 同期メソッド: 簡潔な動詞
SerializationStatistics GetStatistics()
void ClearCache()

// 判定メソッド: Can/Is/Has接頭辞
bool CanUpgrade()
bool IsCompositeKey()
bool HasCircularReference()

// 生成メソッド: Create/Generate/Build
SerializerPair<T> CreateSerializers()
string GenerateSchema()
EntityModel BuildModel()
```

## コード品質ガイドライン

### 1. エラーハンドリング

```csharp
// ✅ 適切なエラーハンドリング
public async Task<SerializerPair<T>> GetSerializersAsync<T>() where T : class
{
    try
    {
        // メイン処理
        return await CreateSerializersAsync<T>();
    }
    catch (SchemaRegistryException ex)
    {
        _logger?.LogError(ex, "Schema Registry operation failed for {EntityType}", typeof(T).Name);
        throw; // 再スロー
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Unexpected error in serializer creation for {EntityType}", typeof(T).Name);
        throw new SerializationException($"Failed to create serializer for {typeof(T).Name}", ex);
    }
}
```

### 2. ログ出力

```csharp
// ✅ 構造化ログ
_logger?.LogInformation("Schema upgraded successfully for {EntityType}: Key={KeySchemaId}, Value={ValueSchemaId}",
    typeof(T).Name, keySchemaId, valueSchemaId);

// ❌ 文字列結合
_logger?.LogInformation($"Schema upgraded for {typeof(T).Name}: Key={keySchemaId}");
```

### 3. リソース管理

```csharp
// ✅ 適切なDispose実装
public class AvroSerializationManager<T> : ISerializationManager<T>
{
    private bool _disposed = false;

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache?.Dispose();
            _disposed = true;
        }
    }
}
```

### 4. スレッドセーフティ

```csharp
// ✅ Thread-safe実装
private readonly ConcurrentDictionary<Type, object> _managers = new();
private long _totalOperations;

public void RecordOperation()
{
    Interlocked.Increment(ref _totalOperations);
}
```

## パフォーマンスガイドライン

### 1. キャッシュ戦略

```csharp
// ✅ 効率的なキャッシュキー生成
private string GenerateCacheKey<T>() where T : class
{
    return $"{typeof(T).FullName}:avro"; // 型名ベース
}

// ✅ キャッシュヒット率監視
public SerializationStatistics GetStatistics()
{
    return new SerializationStatistics
    {
        HitRate = _totalRequests > 0 ? (double)_cacheHits / _totalRequests : 0.0
    };
}
```

### 2. 非同期処理

```csharp
// ✅ ConfigureAwait(false)
public async Task<SerializerPair<T>> GetSerializersAsync()
{
    var result = await _factory.CreateSerializersAsync<T>().ConfigureAwait(false);
    return result;
}

// ✅ CancellationToken対応
public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    // 処理継続
}
```

### 3. メモリ使用量最適化

```csharp
// ✅ 適切なコレクション選択
private readonly ConcurrentDictionary<string, SerializerPair<T>> _cache = new();

// ✅ 不要なオブジェクト生成回避
public string GetTopicName<T>() where T : class
{
    return _topicNameCache.GetOrAdd(typeof(T), type => 
        type.GetCustomAttribute<TopicAttribute>()?.TopicName ?? type.Name);
}
```

## テスト設計ガイドライン

### 1. 単体テスト

```csharp
// ✅ 各層独立テスト
[Test]
public async Task AvroSerializerFactory_CreateSerializers_Success()
{
    // Arrange
    var factory = new AvroSerializerFactory(mockSchemaRegistryClient);
    var entityModel = CreateTestEntityModel<OrderEntity>();

    // Act
    var result = await factory.CreateSerializersAsync<OrderEntity>(entityModel);

    // Assert
    Assert.NotNull(result.KeySerializer);
    Assert.NotNull(result.ValueSerializer);
}
```

### 2. 統合テスト

```csharp
// ✅ 層間連携テスト
[Test]
public async Task AvroSerializationManager_EndToEnd_Success()
{
    // Arrange
    var manager = new AvroSerializationManager<OrderEntity>(schemaRegistryClient);
    var order = new OrderEntity { OrderId = 123, CustomerName = "Test" };

    // Act
    var isValid = await manager.ValidateRoundTripAsync(order);

    // Assert
    Assert.True(isValid);
}
```

### 3. パフォーマンステスト

```csharp
// ✅ キャッシュ効果測定
[Test]
public async Task Cache_Performance_HitRate()
{
    // 100回同一操作でキャッシュヒット率確認
    for (int i = 0; i < 100; i++)
    {
        await manager.GetSerializersAsync<OrderEntity>();
    }
    
    var stats = manager.GetStatistics();
    Assert.Greater(stats.HitRate, 0.9); // 90%以上のヒット率期待
}
```

## 拡張ガイドライン

### 1. 新形式追加手順

1. **抽象層確認**: 既存インターフェースで対応可能か確認
2. **名前空間作成**: `KsqlDsl.Serialization.{Format}/`
3. **Core実装**: `{Format}SerializerFactory`
4. **Cache実装**: `{Format}SerializerCache` 
5. **Management実装**: `{Format}SchemaVersionManager`
6. **統合実装**: `{Format}SerializationManager<T>`

### 2. 新機能追加手順

1. **責務分析**: どの層に属するか判定
2. **インターフェース拡張**: 必要に応じて抽象層更新
3. **実装追加**: 該当層に機能実装
4. **テスト追加**: 単体・統合テスト作成
5. **文書更新**: 設計文書・API文書更新

## 禁止事項

### ❌ やってはいけないこと

1. **層を跨いだ直接参照**
   ```csharp
   // NG: Management → Cache直接参照
   public class AvroSchemaVersionManager
   {
       private readonly AvroSerializerCache _cache; // 禁止
   }
   ```

2. **Internal層の公開**
   ```csharp
   // NG: Internal層のpublic公開
   public static class AvroUtils // internal必須
   ```

3. **複数責務の混在**
   ```csharp
   // NG: 1クラスで複数責務
   public class AvroManager
   {
       public ISerializer CreateSerializer() { }  // 生成責務
       public void CacheSerializer() { }          // キャッシュ責務
       public bool CheckCompatibility() { }       // バージョン責務
   }
   ```

4. **具象クラスへの直接依存**
   ```csharp
   // NG: 具象クラス直接注入
   public OrderService(AvroSerializationManager<OrderEntity> manager)
   
   // OK: インターフェース注入
   public OrderService(ISerializationManager<OrderEntity> manager)
   ```

5. **同期処理でのブロッキング**
   ```csharp
   // NG: 非同期の同期的実行
   var result = GetSerializersAsync().Result; // デッドロック危険

   // OK: 適切な非同期処理
   var result = await GetSerializersAsync();
   ```