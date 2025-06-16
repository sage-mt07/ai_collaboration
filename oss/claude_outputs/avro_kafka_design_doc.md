# Avro × Kafka × 複合PK 設計ドキュメント

## 1. 全体構成概要

### 1.1 設計原則
- 各POCOはKafka Topicに対応するEntityである
- KeyとValueは別スキーマとしてSchema Registryに登録される
- Producer/Consumerで型整合性が重要（KafkaMessage<Key, Value>）
- 複数の型にわたるキャッシュ設計が必要
- 将来的にスキーマ進化やバージョン切り替えを想定

### 1.2 責務分離
```
EntityModel ── 1:1 ── TopicMapping ── 1:2 ── AvroSchemaInfo (Key/Value)
     │                     │                      │
     │                     │                      └── SchemaRegistry
     │                     │
     └── PropertyInfo[] ──── KeyExtractor ─────── AvroKey/AvroValue Conversion
```

## 2. POCO → AvroKey / AvroValue の変換規則

### 2.1 変換方針
```csharp
// 基本原則
public class Order
{
    [Key(Order = 0)] public string CustomerId { get; set; }
    [Key(Order = 1)] public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTime OrderDate { get; set; }
}

// 変換結果
AvroKey = { CustomerId: string, OrderId: int }
AvroValue = { CustomerId: string, OrderId: int, Amount: decimal, OrderDate: timestamp-millis }
```

### 2.2 変換ルール

#### 2.2.1 単一キー変換
```csharp
// 単一キー
[Key] public string Id { get; set; }
→ AvroKey = "string" (primitive schema)
```

#### 2.2.2 複合キー変換
```csharp
// 複合キー
[Key(Order = 0)] public string CustomerId { get; set; }
[Key(Order = 1)] public int OrderId { get; set; }
→ AvroKey = {
    "type": "record",
    "name": "OrderKey",
    "fields": [
        {"name": "CustomerId", "type": "string"},
        {"name": "OrderId", "type": "int"}
    ]
}
```

#### 2.2.3 Value変換
```csharp
// Value は全プロパティを含む（キープロパティも重複して含む）
→ AvroValue = {
    "type": "record", 
    "name": "OrderValue",
    "fields": [
        {"name": "CustomerId", "type": "string"},
        {"name": "OrderId", "type": "int"},
        {"name": "Amount", "type": ["null", {"type": "bytes", "logicalType": "decimal"}]},
        {"name": "OrderDate", "type": {"type": "long", "logicalType": "timestamp-millis"}}
    ]
}
```

### 2.3 型マッピング規則
| C# Type | Avro Type | 備考 |
|---------|-----------|------|
| `string` | `"string"` | |
| `int` | `"int"` | |
| `long` | `"long"` | |
| `decimal` | `{"type": "bytes", "logicalType": "decimal", "precision": 18, "scale": 4}` | デフォルト精度 |
| `DateTime` | `{"type": "long", "logicalType": "timestamp-millis"}` | |
| `bool` | `"boolean"` | |
| `Guid` | `{"type": "string", "logicalType": "uuid"}` | |
| `T?` | `["null", T]` | Union with null |

## 3. AvroSerializer/Deserializerの生成およびキャッシュ構造

### 3.1 キャッシュ設計

#### 3.1.1 キャッシュキー構造
```csharp
public class AvroSerializerCacheKey
{
    public Type EntityType { get; set; }      // Order
    public SerializerType Type { get; set; }  // Key or Value
    public int SchemaId { get; set; }         // Schema Registry ID
    
    public override int GetHashCode() => 
        HashCode.Combine(EntityType, Type, SchemaId);
}

public enum SerializerType { Key, Value }
```

#### 3.1.2 キャッシュ実装
```csharp
public class AvroSerializerCache
{
    private readonly ConcurrentDictionary<AvroSerializerCacheKey, ISerializer<object>> _serializers;
    private readonly ConcurrentDictionary<AvroSerializerCacheKey, IDeserializer<object>> _deserializers;
    
    public ISerializer<object> GetOrCreateSerializer<T>(SerializerType type, int schemaId);
    public IDeserializer<object> GetOrCreateDeserializer<T>(SerializerType type, int schemaId);
}
```

### 3.2 Serializer/Deserializer生成フロー

#### 3.2.1 生成フロー
```
1. EntityModel解析 → KeyProperties抽出
2. Schema Registry確認 → 既存スキーマID取得 or 新規登録
3. AvroSerializer<T>生成 → Confluent.SchemaRegistry.Serdes使用
4. キャッシュ登録 → ConcurrentDictionary保存
5. Producer/Consumer設定 → SetKeySerializer/SetValueSerializer
```

#### 3.2.2 実装クラス構造
```csharp
public class AvroSerializerManager
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroSerializerCache _cache;
    
    public async Task<(ISerializer<object>, ISerializer<object>)> 
        CreateSerializersAsync<T>(EntityModel entityModel);
        
    public async Task<(IDeserializer<object>, IDeserializer<object>)> 
        CreateDeserializersAsync<T>(EntityModel entityModel);
        
    private async Task<int> RegisterOrGetKeySchemaIdAsync<T>(EntityModel entityModel);
    private async Task<int> RegisterOrGetValueSchemaIdAsync<T>(EntityModel entityModel);
}
```

### 3.3 キー抽出ロジック
```csharp
public class KeyExtractor
{
    public static object ExtractKey<T>(T entity, EntityModel entityModel)
    {
        var keyProperties = entityModel.KeyProperties;
        
        if (keyProperties.Length == 0)
            return null; // No key
            
        if (keyProperties.Length == 1)
            return keyProperties[0].GetValue(entity); // Single key
            
        // Composite key
        var keyRecord = new Dictionary<string, object>();
        foreach (var prop in keyProperties.OrderBy(p => p.GetCustomAttribute<KeyAttribute>().Order))
        {
            keyRecord[prop.Name] = prop.GetValue(entity);
        }
        return keyRecord;
    }
}
```

## 4. テストの構成

### 4.1 テスト対象
- **単体テスト**: スキーマ生成、キー抽出、シリアライゼーション
- **統合テスト**: Round-trip完全一致確認
- **性能テスト**: キャッシュ効率、スループット測定

### 4.2 テスト命名規則
```csharp
namespace KsqlDsl.Tests.Avro
{
    // スキーマ生成テスト
    public class AvroSchemaGeneratorTests
    {
        [Test] public void GenerateKeySchema_SingleKey_ShouldReturnPrimitiveSchema()
        [Test] public void GenerateKeySchema_CompositeKey_ShouldReturnRecordSchema()
        [Test] public void GenerateValueSchema_WithLogicalTypes_ShouldMapCorrectly()
    }
    
    // シリアライゼーションテスト
    public class AvroSerializationTests
    {
        [Test] public void SerializeDeserialize_SingleKey_ShouldRoundTripSuccessfully()
        [Test] public void SerializeDeserialize_CompositeKey_ShouldRoundTripSuccessfully()
        [Test] public void SerializeDeserialize_ComplexValue_ShouldRoundTripSuccessfully()
    }
    
    // キャッシュテスト
    public class AvroSerializerCacheTests
    {
        [Test] public void GetOrCreateSerializer_SameType_ShouldReturnCachedInstance()
        [Test] public void GetOrCreateSerializer_DifferentSchemaId_ShouldCreateNewInstance()
    }
}
```

### 4.3 Round-trip確認テスト
```csharp
[Test]
public async Task RoundTrip_ComplexEntity_ShouldPreserveAllData()
{
    // Arrange
    var original = new OrderEntity 
    { 
        CustomerId = "CUST-001", 
        OrderId = 12345,
        Amount = 999.99m,
        OrderDate = DateTime.UtcNow 
    };
    
    var entityModel = modelBuilder.GetEntityModel<OrderEntity>();
    var serializerManager = new AvroSerializerManager(schemaRegistryClient);
    
    // Act
    var (keySerializer, valueSerializer) = await serializerManager.CreateSerializersAsync<OrderEntity>(entityModel);
    var (keyDeserializer, valueDeserializer) = await serializerManager.CreateDeserializersAsync<OrderEntity>(entityModel);
    
    var key = KeyExtractor.ExtractKey(original, entityModel);
    var keyBytes = keySerializer.Serialize(key, new SerializationContext());
    var valueBytes = valueSerializer.Serialize(original, new SerializationContext());
    
    var deserializedKey = keyDeserializer.Deserialize(keyBytes, false, new SerializationContext());
    var deserializedValue = valueDeserializer.Deserialize(valueBytes, false, new SerializationContext());
    
    // Assert
    Assert.That(deserializedKey, Is.EqualTo(key));
    Assert.That(deserializedValue, Is.EqualTo(original).Using(new OrderEntityComparer()));
}
```

## 5. SchemaRegistry登録構造

### 5.1 スキーマ命名規則
```
Topic名: orders
Key Schema Subject: orders-key
Value Schema Subject: orders-value

Topic名: customer-events  
Key Schema Subject: customer-events-key
Value Schema Subject: customer-events-value
```

### 5.2 登録フロー
```csharp
public class SchemaRegistrationService
{
    public async Task<(int keySchemaId, int valueSchemaId)> 
        RegisterSchemasAsync<T>(string topicName, EntityModel entityModel)
    {
        // 1. スキーマ生成
        var keySchema = GenerateKeySchema<T>(entityModel);
        var valueSchema = GenerateValueSchema<T>();
        
        // 2. 既存スキーマ確認
        var keySubject = $"{topicName}-key";
        var valueSubject = $"{topicName}-value";
        
        // 3. 互換性チェック
        if (await _schemaRegistry.CheckCompatibilityAsync(keySubject, keySchema) &&
            await _schemaRegistry.CheckCompatibilityAsync(valueSubject, valueSchema))
        {
            // 4. スキーマ登録
            var keySchemaId = await _schemaRegistry.RegisterSchemaAsync(keySubject, keySchema);
            var valueSchemaId = await _schemaRegistry.RegisterSchemaAsync(valueSubject, valueSchema);
            
            return (keySchemaId, valueSchemaId);
        }
        
        throw new SchemaCompatibilityException($"Schema compatibility check failed for {topicName}");
    }
}
```

### 5.3 Config連動
```csharp
// KafkaContextOptionsBuilderの拡張
public static KafkaContextOptionsBuilder UseAvroSerialization(
    this KafkaContextOptionsBuilder builder,
    string schemaRegistryUrl,
    Action<AvroSerializationOptions>? configure = null)
{
    var options = new AvroSerializationOptions();
    configure?.Invoke(options);
    
    return builder
        .UseSchemaRegistry(schemaRegistryUrl)
        .EnableAvroSchemaAutoRegistration(options.ForceRegistration)
        .ConfigureSchemaGeneration(options.SchemaGenerationOptions);
}
```

## 6. スキーマ進化時の影響と対応想定

### 6.1 想定される変更パターン

#### 6.1.1 後方互換性のある変更
- **新フィールド追加**（デフォルト値付き）
- **フィールドのデフォルト値変更**
- **エイリアス追加**

#### 6.1.2 後方互換性のない変更
- **必須フィールド削除**
- **フィールド型変更**
- **複合キー構造変更**

### 6.2 対応戦略

#### 6.2.1 スキーマバージョニング
```csharp
public class SchemaVersionManager
{
    public async Task<bool> CanUpgradeSchemaAsync<T>(string topicName)
    {
        var currentSchema = await GetLatestSchemaAsync($"{topicName}-value");
        var newSchema = GenerateValueSchema<T>();
        
        return await _schemaRegistry.CheckCompatibilityAsync($"{topicName}-value", newSchema);
    }
    
    public async Task<SchemaUpgradeResult> UpgradeSchemaAsync<T>(string topicName)
    {
        if (!await CanUpgradeSchemaAsync<T>(topicName))
        {
            return new SchemaUpgradeResult 
            { 
                Success = false, 
                Reason = "Schema is not backward compatible" 
            };
        }
        
        var (keySchemaId, valueSchemaId) = await RegisterSchemasAsync<T>(topicName);
        
        // キャッシュクリア
        _serializerCache.ClearCache<T>();
        
        return new SchemaUpgradeResult 
        { 
            Success = true, 
            NewKeySchemaId = keySchemaId,
            NewValueSchemaId = valueSchemaId 
        };
    }
}
```

#### 6.2.2 移行戦略
```csharp
// V1 → V2 移行例
public class OrderEntityV1
{
    [Key] public string OrderId { get; set; }
    public decimal Amount { get; set; }
}

public class OrderEntityV2  
{
    [Key] public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD"; // デフォルト値付き新フィールド
}

// 移行処理
public class SchemaMigration
{
    public async Task MigrateOrderEntityV1ToV2Async()
    {
        // 1. V2スキーマ互換性確認
        var canUpgrade = await _versionManager.CanUpgradeSchemaAsync<OrderEntityV2>("orders");
        
        if (canUpgrade)
        {
            // 2. 新スキーマ登録
            await _versionManager.UpgradeSchemaAsync<OrderEntityV2>("orders");
            
            // 3. アプリケーション段階的切り替え
            // - Producer: V2スキーマで送信開始
            // - Consumer: V1/V2両方処理可能にする
        }
        else
        {
            throw new SchemaIncompatibilityException("Cannot migrate to V2 due to compatibility issues");
        }
    }
}
```

### 6.3 運用監視
```csharp
public class AvroSchemaMonitoring
{
    public async Task<SchemaHealthReport> GetSchemaHealthAsync()
    {
        var report = new SchemaHealthReport();
        
        foreach (var entityModel in _modelBuilder.GetEntityModels())
        {
            var topicName = entityModel.Value.TopicAttribute?.TopicName ?? entityModel.Key.Name;
            
            // スキーマ互換性確認
            var isCompatible = await CheckSchemaCompatibilityAsync(entityModel.Key, topicName);
            
            // キャッシュ状態確認
            var cacheStatus = _serializerCache.GetCacheStatus(entityModel.Key);
            
            report.EntityReports.Add(new EntitySchemaReport
            {
                EntityType = entityModel.Key,
                TopicName = topicName,
                IsSchemaCompatible = isCompatible,
                CacheHitRate = cacheStatus.HitRate,
                LastSchemaCheck = DateTime.UtcNow
            });
        }
        
        return report;
    }
}
```

## 7. 実装優先順位

### Phase 1: 基本実装
1. ✅ `AvroSerializerCacheKey` 
2. ✅ `AvroSerializerCache`
3. ✅ `KeyExtractor`
4. ✅ `AvroSerializerManager`

### Phase 2: スキーマ管理
1. ✅ `SchemaRegistrationService` 強化
2. ✅ Round-trip テスト実装
3. ✅ エラーハンドリング強化

### Phase 3: 進化対応
1. 🔄 `SchemaVersionManager`
2. 🔄 `SchemaHealthMonitoring`
3. 🔄 互換性チェック自動化

---

この設計により、型安全でパフォーマンスの高いAvroベースのKafka統合が実現され、将来のスキーマ進化にも柔軟に対応できます。