# Kafka通信層 設計ドキュメント

## 🎯 設計方針

### 基本方針
1. **既存Avro実装の最大活用**: 95%実装済みのAvro機能を通信層で統合
2. **Producer/Consumer分離**: 責任分離による保守性向上
3. **型安全性確保**: コンパイル時型チェックによるランタイムエラー防止
4. **可観測性重視**: OpenTelemetry統合による運用監視
5. **耐障害性**: リトライ・フォールバック・DLQ対応

## 🏗️ アーキテクチャ設計

### 全体構成図
```
┌─────────────────────────────────────────────────────┐
│                Application Layer                    │
├─────────────────────────────────────────────────────┤
│              KafkaMessageBus (Facade)               │
├─────────────────────────────────────────────────────┤
│  KafkaProducerManager  │  KafkaConsumerManager      │
├──────────────────────┬─┴──────────────────────────────┤
│   ProducerPool       │      ConsumerPool             │
├──────────────────────┼───────────────────────────────┤
│     AvroSerializer/DeserializerManager (既存)        │
├─────────────────────────────────────────────────────┤
│           Confluent.Kafka + Schema Registry          │
└─────────────────────────────────────────────────────┘
```

## 📦 クラス設計詳細

### 1. 統合Facadeレイヤー

#### KafkaMessageBus
```csharp
/// <summary>
/// Kafka通信の統合ファサード
/// 設計理由：アプリケーション層に単一のエントリポイントを提供
/// </summary>
public class KafkaMessageBus : IKafkaMessageBus, IDisposable
{
    // 依存関係
    private readonly KafkaProducerManager _producerManager;
    private readonly KafkaConsumerManager _consumerManager;
    private readonly PerformanceMonitoringAvroCache _avroCache;
    private readonly ILogger<KafkaMessageBus> _logger;
    
    // 統合API
    Task SendAsync<T>(T message, KafkaMessageContext? context = null);
    Task SendBatchAsync<T>(IEnumerable<T> messages, KafkaMessageContext? context = null);
    
    IAsyncEnumerable<T> ConsumeAsync<T>(CancellationToken cancellationToken);
    Task<List<T>> FetchAsync<T>(KafkaFetchOptions options);
    
    // ヘルス・診断
    Task<KafkaHealthReport> GetHealthReportAsync();
    KafkaDiagnostics GetDiagnostics();
}
```

#### KafkaMessageContext
```csharp
/// <summary>
/// メッセージ送信時のコンテキスト情報
/// 設計理由：横断的関心事（トレーシング、パーティション指定等）の管理
/// </summary>
public class KafkaMessageContext
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public int? TargetPartition { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
    public RetryPolicy? CustomRetryPolicy { get; set; }
    
    // OpenTelemetry連携
    public ActivityContext? ActivityContext { get; set; }
    public Dictionary<string, object> Tags { get; set; } = new();
}
```

### 2. Producer管理レイヤー

#### KafkaProducerManager
```csharp
/// <summary>
/// Producer管理・ライフサイクル制御
/// 設計理由：複数トピック・エンティティへの効率的Producer配布
/// </summary>
public class KafkaProducerManager : IDisposable
{
    // 既存実装活用
    private readonly EnhancedAvroSerializerManager _serializerManager;
    private readonly ProducerPool _producerPool;
    private readonly KafkaProducerConfig _config;
    private readonly ILogger<KafkaProducerManager> _logger;
    
    // Producer管理
    Task<IKafkaProducer<T>> GetProducerAsync<T>() where T : class;
    void ReturnProducer<T>(IKafkaProducer<T> producer) where T : class;
    
    // バッチ送信最適化
    Task SendBatchOptimizedAsync<T>(IEnumerable<T> messages, KafkaMessageContext? context);
    
    // パフォーマンス統計
    ProducerPerformanceStats GetPerformanceStats();
}
```

#### ProducerPool
```csharp
/// <summary>
/// Producer インスタンスプール
/// 設計理由：Producer作成コスト削減、リソース効率化
/// </summary>
public class ProducerPool : IDisposable
{
    private readonly ConcurrentDictionary<ProducerKey, ConcurrentQueue<IProducer<object, object>>> _pools;
    private readonly ProducerPoolConfig _config;
    private readonly ILogger<ProducerPool> _logger;
    
    // プール管理
    IProducer<object, object> RentProducer(ProducerKey key);
    void ReturnProducer(ProducerKey key, IProducer<object, object> producer);
    
    // ヘルス管理
    Task<PoolHealthStatus> GetHealthStatusAsync();
    void TrimExcessProducers();
    
    // 設定可能項目
    // - MinPoolSize: 最小プールサイズ
    // - MaxPoolSize: 最大プールサイズ  
    // - ProducerIdleTimeout: アイドル時間上限
    // - HealthCheckInterval: ヘルスチェック間隔
}
```

#### IKafkaProducer<T>
```csharp
/// <summary>
/// 型安全Producer インターフェース
/// 設計理由：型安全性確保、テスタビリティ向上
/// </summary>
public interface IKafkaProducer<T> : IDisposable where T : class
{
    Task<KafkaDeliveryResult> SendAsync(T message, KafkaMessageContext? context = null);
    Task<KafkaBatchDeliveryResult> SendBatchAsync(IEnumerable<T> messages, KafkaMessageContext? context = null);
    
    // 設定・統計
    KafkaProducerStats GetStats();
    Task FlushAsync(TimeSpan timeout);
}
```

### 3. Consumer管理レイヤー

#### KafkaConsumerManager
```csharp
/// <summary>
/// Consumer管理・購読制御
/// 設計理由：複数購読の効率管理、オフセット管理の統合
/// </summary>
public class KafkaConsumerManager : IDisposable
{
    // 既存実装活用
    private readonly EnhancedAvroSerializerManager _serializerManager;
    private readonly ConsumerPool _consumerPool;
    private readonly KafkaConsumerConfig _config;
    private readonly ILogger<KafkaConsumerManager> _logger;
    
    // Consumer管理
    Task<IKafkaConsumer<T>> CreateConsumerAsync<T>(KafkaSubscriptionOptions options) where T : class;
    
    // 購読管理
    Task SubscribeAsync<T>(Func<T, KafkaMessageContext, Task> handler, KafkaSubscriptionOptions options);
    Task UnsubscribeAsync<T>();
    
    // バッチ処理
    IAsyncEnumerable<KafkaBatch<T>> ConsumeBatchesAsync<T>(KafkaBatchOptions options);
    
    // オフセット管理
    Task CommitAsync<T>();
    Task SeekAsync<T>(TopicPartitionOffset offset);
    
    // 統計
    ConsumerPerformanceStats GetPerformanceStats();
}
```

#### ConsumerPool
```csharp
/// <summary>
/// Consumer インスタンスプール
/// 設計理由：Consumer作成コスト削減、購読状態管理
/// </summary>
public class ConsumerPool : IDisposable
{
    private readonly ConcurrentDictionary<ConsumerKey, ConsumerInstance> _consumers;
    private readonly ConsumerPoolConfig _config;
    private readonly ILogger<ConsumerPool> _logger;
    
    // プール管理
    ConsumerInstance RentConsumer(ConsumerKey key);
    void ReturnConsumer(ConsumerKey key, ConsumerInstance consumer);
    
    // 購読状態管理
    Task<List<TopicPartition>> GetAssignedPartitionsAsync(ConsumerKey key);
    Task<Dictionary<TopicPartition, Offset>> GetOffsetsAsync(ConsumerKey key);
    
    // ヘルス管理
    Task<ConsumerPoolHealthStatus> GetHealthStatusAsync();
    void RebalanceConsumers();
}
```

#### IKafkaConsumer<T>
```csharp
/// <summary>
/// 型安全Consumer インターフェース
/// 設計理由：型安全性確保、購読パターンの統一
/// </summary>
public interface IKafkaConsumer<T> : IDisposable where T : class
{
    IAsyncEnumerable<KafkaMessage<T>> ConsumeAsync(CancellationToken cancellationToken);
    Task<KafkaBatch<T>> ConsumeBatchAsync(KafkaBatchOptions options, CancellationToken cancellationToken);
    
    // オフセット制御
    Task CommitAsync();
    Task SeekAsync(TopicPartitionOffset offset);
    
    // 統計・状態
    KafkaConsumerStats GetStats();
    List<TopicPartition> GetAssignedPartitions();
}
```

## 🔧 設定・構成クラス

### KafkaMessageBusOptions
```csharp
/// <summary>
/// MessageBus全体設定
/// 設計理由：統合設定による一元管理
/// </summary>
public class KafkaMessageBusOptions
{
    // 接続設定
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string? SchemaRegistryUrl { get; set; }
    
    // Producer設定
    public KafkaProducerConfig ProducerConfig { get; set; } = new();
    public ProducerPoolConfig ProducerPoolConfig { get; set; } = new();
    
    // Consumer設定
    public KafkaConsumerConfig ConsumerConfig { get; set; } = new();
    public ConsumerPoolConfig ConsumerPoolConfig { get; set; } = new();
    
    // Avro設定（既存活用）
    public AvroOperationRetrySettings AvroRetrySettings { get; set; } = new();
    public PerformanceThresholds PerformanceThresholds { get; set; } = new();
    
    // 監視・診断
    public bool EnableDetailedMetrics { get; set; } = true;
    public bool EnableDistributedTracing { get; set; } = true;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}
```

## 📊 監視・診断設計

### KafkaHealthReport
```csharp
/// <summary>
/// Kafka通信層ヘルス報告
/// 設計理由：運用監視、障害検出の統合ビュー
/// </summary>
public class KafkaHealthReport
{
    public DateTime GeneratedAt { get; set; }
    public KafkaHealthLevel HealthLevel { get; set; }
    
    // コンポーネント別ヘルス
    public ProducerHealthStatus ProducerHealth { get; set; }
    public ConsumerHealthStatus ConsumerHealth { get; set; }
    public CacheHealthReport AvroHealth { get; set; } // 既存活用
    
    // 統計情報
    public KafkaPerformanceStats PerformanceStats { get; set; }
    public List<KafkaHealthIssue> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}
```

### KafkaMetrics（OpenTelemetry統合）
```csharp
/// <summary>
/// Kafka通信メトリクス
/// 設計理由：既存AvroMetricsとの統合、標準メトリクス提供
/// </summary>
public static class KafkaMetrics
{
    private static readonly Meter _meter = new("KsqlDsl.Kafka", "1.0.0");
    
    // Producer メトリクス
    public static void RecordMessageSent(string topic, string entityType, bool success, TimeSpan duration);
    public static void RecordBatchSent(string topic, int messageCount, bool success, TimeSpan duration);
    
    // Consumer メトリクス  
    public static void RecordMessageReceived(string topic, string entityType, TimeSpan processingTime);
    public static void RecordConsumerLag(string topic, int partition, long lag);
    
    // エラー・例外
    public static void RecordSerializationError(string entityType, string errorType);
    public static void RecordConnectionError(string brokerHost, string errorType);
    
    // スループット
    public static void RecordThroughput(string direction, string topic, long bytesPerSecond);
}
```

## 🚀 使用例・シナリオ設計

### 基本的な送信パターン
```csharp
// 単一メッセージ送信
await messageBus.SendAsync(new OrderEvent 
{ 
    OrderId = 123, 
    CustomerId = "CUST001" 
});

// バッチ送信（最適化）
var orders = GenerateOrders(1000);
await messageBus.SendBatchAsync(orders, new KafkaMessageContext
{
    CorrelationId = Guid.NewGuid().ToString(),
    Timeout = TimeSpan.FromSeconds(30)
});

// パーティション指定送信
await messageBus.SendAsync(orderEvent, new KafkaMessageContext
{
    TargetPartition = CalculatePartition(orderEvent.CustomerId)
});
```

### 購読・消費パターン
```csharp
// リアルタイム購読
await foreach (var order in messageBus.ConsumeAsync<OrderEvent>(cancellationToken))
{
    await ProcessOrderAsync(order);
}

// バッチ処理
await foreach (var batch in messageBus.ConsumeBatchesAsync<OrderEvent>(new KafkaBatchOptions
{
    MaxBatchSize = 100,
    MaxWaitTime = TimeSpan.FromSeconds(5)
}))
{
    await ProcessOrderBatchAsync(batch.Messages);
    await batch.CommitAsync();
}

// 履歴データ取得
var recentOrders = await messageBus.FetchAsync<OrderEvent>(new KafkaFetchOptions
{
    FromOffset = Offset.Beginning,
    MaxMessages = 1000,
    Timeout = TimeSpan.FromMinutes(1)
});
```

## 🔒 エラーハンドリング・耐障害性

### エラー分類・対応戦略
```csharp
/// <summary>
/// Kafka通信エラー分類
/// 設計理由：エラー種別に応じた適切な対応実施
/// </summary>
public enum KafkaErrorType
{
    // 一時的エラー（リトライ可能）
    NetworkTimeout,
    BrokerUnavailable, 
    ProducerQueueFull,
    
    // 設定エラー（設定見直し必要）
    InvalidConfiguration,
    AuthenticationFailure,
    AuthorizationFailure,
    
    // データエラー（DLQ送信対象）
    SerializationFailure,
    SchemaIncompatible,
    MessageTooLarge,
    
    // 致命的エラー（プロセス停止検討）
    CorruptedMessage,
    UnrecoverableFailure
}
```

### DLQ（Dead Letter Queue）統合
```csharp
/// <summary>
/// DLQ管理サービス
/// 設計理由：処理不能メッセージの自動隔離・分析
/// </summary>
public class KafkaDlqManager
{
    // DLQ送信
    Task SendToDlqAsync<T>(T originalMessage, KafkaErrorContext errorContext);
    
    // DLQ監視・分析
    Task<DlqAnalysisReport> AnalyzeDlqAsync(string sourceTopic, TimeSpan period);
    
    // リプレイ機能
    Task<ReplayResult> ReplayFromDlqAsync(string sourceTopic, ReplayOptions options);
}
```

## 🎯 パフォーマンス最適化設計

### バッチ処理最適化
- **送信側**: 同一トピック・パーティションのメッセージ自動グルーピング
- **受信側**: Configurable バッチサイズ・タイムアウト
- **メモリ効率**: ストリーミング処理、大量データ対応

### 接続プール最適化
- **Producer Pool**: トピック・設定別プール管理
- **Consumer Pool**: コンシューマーグループ・トピック別管理
- **動的スケーリング**: 負荷に応じたプールサイズ調整

### Avro統合最適化
- **既存キャッシュ活用**: PerformanceMonitoringAvroCache連携
- **スキーマ事前読み込み**: アプリケーション起動時の事前準備
- **型別最適化**: エンティティ特性に応じた最適化設定

## 📋 実装優先度

### Phase 1: Core通信機能（必須）
1. `KafkaMessageBus` - 統合ファサード
2. `KafkaProducerManager` + `ProducerPool`
3. `KafkaConsumerManager` + `ConsumerPool`
4. 基本的な型安全Producer/Consumer

### Phase 2: 運用機能（重要）
1. ヘルスチェック・診断機能
2. DLQ統合
3. OpenTelemetryメトリクス統合
4. バッチ処理最適化

### Phase 3: 高度機能（推奨）
1. 動的設定変更
2. A/Bテスト用トラフィック分割
3. 高度なリバランシング制御
4. カスタムパーティショナー

## ✅ 既存実装との統合ポイント

### 既存Avro実装の活用箇所
- **EnhancedAvroSerializerManager**: Producer/Consumer でのシリアライゼーション
- **PerformanceMonitoringAvroCache**: キャッシュ統計の統合監視
- **AvroRetryPolicy**: 通信層リトライとの統合
- **AvroMetrics**: 通信メトリクスとの統合
- **SchemaVersionManager**: スキーマアップグレード連携

### 既存KafkaContext連携
- **EventSet**: 新通信層のConsumer基盤として活用
- **ModelBuilder**: エンティティ定義の統合利用
- **ValidationService**: 通信設定の妥当性検証

## 🎯 設計品質指標

### 可読性・保守性
- ✅ 単一責任原則の徹底
- ✅ 依存性注入による疎結合
- ✅ インターフェース分離による交換可能性

### パフォーマンス
- ✅ 既存高性能Avroキャッシュ活用
- ✅ プール設計による効率化
- ✅ バッチ処理最適化

### 運用性
- ✅ OpenTelemetry標準準拠
- ✅ ヘルスチェック統合
- ✅ 設定外部化・Hot Reload対応

この設計により、95%実装済みのAvro機能を最大活用しつつ、企業級Kafka通信層の完成が可能です。