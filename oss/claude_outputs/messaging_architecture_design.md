# KsqlDsl Messaging Architecture 設計書

## 1. 概要

本設計書は、KsqlDsl OSSのMessaging層アーキテクチャの全面的な再設計について記述します。EntityFramework風のAPIエクスペリエンスを実現するため、従来のPool設計やメトリクス実装を削除し、シンプルで保守性の高い設計に変更します。

## 2. 設計方針

### 2.1 基本原則

| 原則 | 説明 | 理由 |
|------|------|------|
| **EF風API優先** | DbContext→EventSet→Entity操作の直感的体験 | 開発者の学習コストを最小化 |
| **Confluent.Kafka委譲** | 低レベル統計・メトリクスは標準ライブラリに委譲 | 重複実装を排除、保守性向上 |
| **Pool設計削除** | 動的なProducer/Consumer管理を廃止 | EF風では事前確定が適切 |
| **メトリクスレス** | 独自メトリクス実装を最小化 | 複雑性削除、実用価値重視 |
| **直接管理** | EventSet→Producer/Consumerの直接保持 | シンプルなライフサイクル管理 |

### 2.2 アーキテクチャ目標

- **クラス数70%削減**: 約45クラス → 約15クラス
- **複雑性排除**: Pool管理、メトリクス収集、重複実装の削除
- **保守性向上**: シンプルな依存関係、明確な責務分離
- **パフォーマンス**: 不要なオーバーヘッド削除

## 3. 従来設計の問題点

### 3.1 Pool設計の問題

#### 設計意図との乖離
```csharp
// ❌ Pool設計の前提（動的リクエスト処理）
producer = pool.RentProducer(key);
await producer.SendAsync(message);
pool.ReturnProducer(key, producer);
```

```csharp
// ✅ EF風の実際（静的Context管理）
var db = new MyKafkaDbContext(); // OnModelCreating実行
await db.TradeEvents.AddAsync(event); // 事前確定されたProducer使用
```

#### 根本的な不整合
- **Pool前提**: 短期間利用・動的管理・リソース効率化
- **EF風実際**: Context生存期間保持・事前確定・型安全性

### 3.2 メトリクス実装の問題

#### Confluent.Kafkaとの重複
```csharp
// ❌ 重複実装
KafkaMetrics.RecordMessageSent(topic, entityType, success, duration);
// Confluent.Kafkaが既に同等機能を提供
```

#### 実用価値の不足
```csharp
// ❌ 1回のみの統計（価値なし）
ModelCreationDuration = 2.5秒; // OnModelCreating時のみ
EntitiesConfigured = 10;       // アプリ起動時のみ

// ✅ 代替手段の方が有効
_logger.LogDebug("TradeEvent configuration completed in {Duration}ms", elapsed);
```

### 3.3 重複実装の問題

#### Producer重複
- `KafkaProducer<T>` vs `TypedKafkaProducer<T>`
- `KafkaProducerManager` vs `EnhancedKafkaProducerManager`

#### Consumer重複
- `KafkaConsumer<T>` vs `TypedKafkaConsumer<T>`
- `KafkaConsumerManager` vs 複数のManager実装

## 4. 新設計アーキテクチャ

### 4.1 全体構造

```
KafkaDbContext (EF風エントリポイント)
  ↓ OnModelCreating時初期化
EventSet<TradeEvent>
EventSet<OrderEvent>
  ↓ 直接保持
KafkaProducer<TradeEvent>    KafkaConsumer<TradeEvent>
KafkaProducer<OrderEvent>    KafkaConsumer<OrderEvent>
  ↓ Confluent.Kafka委譲
IProducer<object, object>    IConsumer<object, object>
```

### 4.2 namespace構造

#### 削除対象
```
❌ Messaging.Producers.Pool/          (Pool設計削除)
❌ Messaging.Consumers.Pool/          (Pool設計削除)
❌ Messaging.Core/PoolMetrics.cs      (Pool統計削除)
❌ Messaging.Abstractions/KafkaMetrics.cs (メトリクス削除)
❌ Messaging.Bus/BusDiagnostics.cs    (診断機能削除)
❌ Messaging/Serializeration.cs       (無意味ファイル)
```

#### 残存・統合後
```
✅ Messaging.Abstractions/
   - IKafkaProducer<T>
   - IKafkaConsumer<T>

✅ Messaging.Configuration/
   - CommonSection, ProducerSection, ConsumerSection
   - TopicSection, SchemaRegistrySection

✅ Messaging.Producers/
   - KafkaProducer<T> (統合済み)
   - KafkaDeliveryResult, KafkaBatchDeliveryResult
   - 基本例外のみ

✅ Messaging.Consumers/
   - KafkaConsumer<T> (統合済み)
   - Subscription関連クラス
   - 基本例外のみ
```

## 5. 主要クラス設計

### 5.1 KafkaDbContext

```csharp
public abstract class KafkaDbContext : IDisposable
{
    private readonly Dictionary<Type, object> _eventSets = new();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity→Topic→Producer/Consumer初期化
        // Schema Registry登録
        // 事前確定・保持
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Kafka接続設定
        // Schema Registry設定
    }
    
    public void Dispose()
    {
        // 全Producer/Consumer破棄
    }
}
```

### 5.2 EventSet<T>

```csharp
public class EventSet<T> where T : class
{
    private readonly IKafkaProducer<T> _producer;
    private readonly IKafkaConsumer<T> _consumer;
    
    // EF風API
    public async Task AddAsync(T entity) => await _producer.SendAsync(entity);
    public List<T> ToList() => _consumer.GetMessages();
    public void Subscribe(Action<T> handler) => _consumer.Subscribe(handler);
    public string ToKsql() => _queryTranslator.TranslateToKsql();
}
```

### 5.3 KafkaProducer<T> (統合版)

```csharp
public class KafkaProducer<T> : IKafkaProducer<T> where T : class
{
    private readonly IProducer<object, object> _producer;
    private readonly ISerializer<object> _serializer;
    
    public async Task<KafkaDeliveryResult> SendAsync(T message)
    {
        // メトリクス処理なし、Confluent.Kafkaに委譲
        var result = await _producer.ProduceAsync(CreateMessage(message));
        return MapResult(result);
    }
    
    public async Task<KafkaBatchDeliveryResult> SendBatchAsync(IEnumerable<T> messages)
    {
        // バッチ送信実装
    }
}
```

### 5.4 KafkaConsumer<T> (統合版)

```csharp
public class KafkaConsumer<T> : IKafkaConsumer<T> where T : class
{
    private readonly IConsumer<object, object> _consumer;
    private readonly IDeserializer<object> _deserializer;
    
    public async IAsyncEnumerable<T> ConsumeAsync(CancellationToken cancellationToken)
    {
        // メトリクス処理なし、Confluent.Kafkaに委譲
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = _consumer.Consume(cancellationToken);
            if (result != null)
                yield return DeserializeMessage(result);
        }
    }
}
```

## 6. Configuration設計

### 6.1 設計原則

messaging_configuration.txtの方針に従い：

- **具象クラス直接参照**: interface削除、POCO/Section直接利用
- **AdditionalProperties**: 各Sectionに拡張プロパティ必須
- **トピック単位管理**: Producer/Consumer設定をトピック別に統合
- **Confluent.Kafka準拠**: 公式仕様に合わせたプロパティ名

### 6.2 Configuration構造

```
src/Configuration/
├── KsqlDslOptions.cs (ルートオプション)
└── ValidationMode.cs

src/Messaging/Configuration/
├── CommonSection.cs (BootstrapServers等共通設定)
├── ProducerSection.cs (Producer固有設定)
├── ConsumerSection.cs (Consumer固有設定)
├── TopicSection.cs (トピック別統合設定)
└── SchemaRegistrySection.cs (Confluent準拠)
```

### 6.3 appsettings.json構造

```json
{
  "KsqlDsl": {
    "ValidationMode": "Strict",
    "Common": {
      "BootstrapServers": "localhost:9092",
      "ClientId": "ksql-dsl-client"
    },
    "Topics": {
      "TradeEvents": {
        "Producer": { "Acks": "All", "CompressionType": "Snappy" },
        "Consumer": { "GroupId": "trade-group", "AutoOffsetReset": "Latest" }
      }
    },
    "SchemaRegistry": {
      "Url": "http://localhost:8081",
      "AutoRegisterSchemas": true
    }
  }
}
```

## 7. 削除されるクラス一覧

### 7.1 完全削除対象

#### Pool関連 (約10クラス)
- ProducerPool.cs / ConsumerPool.cs
- ProducerPoolManager.cs
- PooledProducer.cs / PooledConsumer.cs
- ConsumerInstance.cs
- Pool関連Exception群

#### メトリクス関連 (約15クラス)
- KafkaMetrics.cs
- KafkaProducerStats.cs / KafkaConsumerStats.cs
- PoolMetrics.cs
- BusDiagnostics.cs
- Performance/Health関連全クラス

#### Configuration interface (4クラス + ディレクトリ)
- ICommonConfiguration.cs
- IProducerConfiguration.cs
- IConsumerConfiguration.cs
- ISchemaRegistryConfiguration.cs
- Abstractions/Configuration/ ディレクトリ

#### 無意味ファイル
- Serializeration.cs (typo、空クラス)

### 7.2 統合対象

#### Producer重複統合
```
KafkaProducer.cs + TypedKafkaProducer.cs → KafkaProducer<T> (統合版)
KafkaProducerManager.cs + EnhancedKafkaProducerManager.cs → 削除
```

#### Consumer重複統合
```
KafkaConsumer.cs + TypedKafkaConsumer.cs → KafkaConsumer<T> (統合版)
KafkaConsumerManager.cs → 簡素化
```

## 8. 利用例

### 8.1 基本的な利用パターン

```csharp
// DbContext定義
public class TradingDbContext : KafkaDbContext
{
    public EventSet<TradeEvent> TradeEvents { get; set; }
    public EventSet<OrderEvent> OrderEvents { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Event<TradeEvent>(e => {
            e.WithKafkaTopic("trade-events");
            e.WithSchemaRegistry(reg => reg.Avro().RegisterOnStartup());
        });
        
        modelBuilder.Event<OrderEvent>(e => {
            e.WithKafkaTopic("order-events");
            e.WithSchemaRegistry(reg => reg.Avro().RegisterOnStartup());
        });
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseKafka("localhost:9092");
        optionsBuilder.UseSchemaRegistry("http://localhost:8081");
    }
}

// 利用
var db = new TradingDbContext();

// 送信 (EF風)
await db.TradeEvents.AddAsync(new TradeEvent { Symbol = "USD/JPY", Amount = 1000000 });

// 取得 (EF風)
var largeTrades = db.TradeEvents.Where(t => t.Amount > 500000).ToList();

// 購読 (Kafka特有)
db.TradeEvents.Subscribe(trade => Console.WriteLine($"Trade: {trade.Symbol}"));

// KSQL出力 (デバッグ用)
Console.WriteLine(db.TradeEvents.Where(t => t.Amount > 500000).ToKsql());
```

### 8.2 DI登録

```csharp
// Program.cs
services.AddKsqlDslConfiguration(configuration);
services.AddScoped<TradingDbContext>();
```

## 9. 移行ガイド

### 9.1 削除コマンド

```bash
# Pool関連削除
rm -rf src/Messaging/Producers/Pool/
rm -rf src/Messaging/Consumers/Pool/

# メトリクス関連削除
rm src/Messaging/Abstractions/KafkaMetrics.cs
rm src/Messaging/Producers/Core/KafkaProducerStats.cs
rm src/Messaging/Consumers/Core/KafkaConsumerStats.cs
rm src/Messaging/Core/PoolMetrics.cs
rm src/Messaging/Bus/BusDiagnostics.cs

# Configuration interface削除
rm -rf src/Messaging/Abstractions/Configuration/

# 旧Configuration削除
rm src/Configuration/KafkaBusOptions.cs
rm src/Configuration/AvroSchemaRegistryOptions.cs
rm src/Messaging/Configuration/KafkaConsumerConfig.cs
rm src/Messaging/Configuration/KafkaProducerConfig.cs

# 無意味ファイル削除
rm src/Messaging/Serializeration.cs
```

### 9.2 既存コード修正要件

#### Manager層の修正
- 設定クラス参照を`KsqlDslOptions`に変更
- メトリクス収集処理を削除
- Pool操作を直接管理に変更

#### Producer/Consumer修正
- 統計更新処理を削除
- Pool返却処理を削除
- Confluent.Kafkaに統計委譲

## 10. 期待効果

### 10.1 定量効果

| 項目 | Before | After | 削減率 |
|------|--------|-------|--------|
| クラス数 | 約45クラス | 約15クラス | 67% |
| namespace数 | 8 namespace | 4 namespace | 50% |
| 設定クラス数 | 8クラス | 6クラス | 25% |
| コード行数 | 推定15,000行 | 推定5,000行 | 67% |

### 10.2 定性効果

#### 開発効率向上
- **学習コスト削減**: EF風API体験でKafka知識不要
- **保守性向上**: シンプルな構造、明確な責務分離
- **デバッグ効率**: 複雑なPool・メトリクス処理削除

#### 運用効率向上
- **パフォーマンス**: 不要なオーバーヘッド削除
- **安定性**: 複雑なPool管理削除で障害要因削除
- **監視**: Confluent.Kafka標準メトリクスで十分

#### コスト削減
- **開発コスト**: 実装・テスト・保守工数大幅削減
- **運用コスト**: 障害対応・チューニング工数削減

## 11. リスク・制約事項

### 11.1 機能制約

#### 削除される機能
- 独自メトリクス収集
- Pool使用率監視
- 動的Producer/Consumer管理
- 詳細パフォーマンス統計

#### 代替手段
- Confluent.Kafka標準メトリクス利用
- APM（Application Performance Monitoring）ツール活用
- ログベース監視

### 11.2 移行リスク

#### 破壊的変更
- 既存API大幅変更
- 設定構造変更
- メトリクス取得方法変更

#### 軽減策
- 段階的移行計画
- 十分なテスト期間
- ドキュメント整備

## 12. 結論

本設計変更により、KsqlDsl OSSは以下を実現します：

1. **EntityFramework風の直感的API体験**
2. **大幅な複雑性削除（67%のクラス削減）**  
3. **Confluent.Kafka標準機能の活用**
4. **高い保守性・拡張性**

EF風のAPIエクスペリエンスを実現する上で、Pool設計やメトリクス実装は不要な複雑性であることが判明しました。シンプルで実用的な設計に変更することで、開発者にとって使いやすく、保守しやすいライブラリを提供できます。