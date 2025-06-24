# KsqlDsl Configuration Options 構造設計

## 概要

KsqlDslの設定管理は、責務分離の原則に基づいて以下の5つの主要Optionsクラスに分割されています。
従来の巨大な`KafkaMessageBusOptions.cs`(800行)を適切な責務単位で分割し、保守性と拡張性を向上させました。

## Options構造

### 1. KafkaBusOptions
**責務**: Kafka統合バス全体の基盤設定

```csharp
public record KafkaBusOptions
{
    public string BootstrapServers { get; init; } = "localhost:9092";
    public string ClientId { get; init; } = "ksql-dsl-client";
    public SecurityProtocol SecurityProtocol { get; init; } = SecurityProtocol.Plaintext;
    public SaslMechanism? SaslMechanism { get; init; }
    // SSL/SASL認証設定など
}
```

**設定例**:
```json
{
  "KafkaBus": {
    "BootstrapServers": "kafka1:9092,kafka2:9092,kafka3:9092",
    "ClientId": "my-ksql-app",
    "SecurityProtocol": "SaslSsl",
    "SaslMechanism": "ScramSha256",
    "SaslUsername": "user",
    "SaslPassword": "password"
  }
}
```

### 2. KafkaProducerOptions
**責務**: Producer固有の動作制御

```csharp
public record KafkaProducerOptions
{
    public Acks Acks { get; init; } = Acks.Leader;
    public CompressionType CompressionType { get; init; } = CompressionType.None;
    public int BatchSize { get; init; } = 16384;
    public bool EnableIdempotence { get; init; } = false;
    // パフォーマンス・信頼性設定
}
```

**設定例**:
```json
{
  "KafkaProducer": {
    "Acks": "All",
    "CompressionType": "Lz4",
    "BatchSize": 32768,
    "EnableIdempotence": true,
    "TransactionalId": "my-producer-tx"
  }
}
```

### 3. KafkaConsumerOptions
**責務**: Consumer固有の動作制御

```csharp
public record KafkaConsumerOptions
{
    public string GroupId { get; init; } = "ksql-dsl-group";
    public AutoOffsetReset AutoOffsetReset { get; init; } = AutoOffsetReset.Latest;
    public bool EnableAutoCommit { get; init; } = true;
    public IsolationLevel IsolationLevel { get; init; } = IsolationLevel.ReadUncommitted;
    // 消費制御設定
}
```

**設定例**:
```json
{
  "KafkaConsumer": {
    "GroupId": "my-consumer-group",
    "AutoOffsetReset": "Earliest",
    "EnableAutoCommit": false,
    "SessionTimeoutMs": 30000,
    "IsolationLevel": "ReadCommitted"
  }
}
```

### 4. RetryOptions
**責務**: リトライ動作・エラーハンドリング

```csharp
public record RetryOptions
{
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    public double BackoffMultiplier { get; init; } = 2.0;
    public List<Type> RetriableExceptions { get; init; } = new();
    // 回復性制御設定
}
```

**設定例**:
```json
{
  "Retry": {
    "MaxRetryAttempts": 5,
    "InitialDelay": "00:00:00.200",
    "MaxDelay": "00:01:00",
    "BackoffMultiplier": 1.5,
    "EnableJitter": true
  }
}
```

### 5. AvroSchemaRegistryOptions
**責務**: Avroスキーマレジストリとの統合

```csharp
public record AvroSchemaRegistryOptions
{
    public string Url { get; init; } = "http://localhost:8081";
    public int MaxCachedSchemas { get; init; } = 1000;
    public bool AutoRegisterSchemas { get; init; } = true;
    public SubjectNameStrategy SubjectNameStrategy { get; init; } = SubjectNameStrategy.Topic;
    // スキーマ管理設定
}
```

**設定例**:
```json
{
  "AvroSchemaRegistry": {
    "Url": "https://schema-registry.company.com:8081",
    "MaxCachedSchemas": 2000,
    "AutoRegisterSchemas": false,
    "SubjectNameStrategy": "TopicRecord",
    "BasicAuthUsername": "registry-user",
    "BasicAuthPassword": "registry-pass"
  }
}
```

## 使用方法

### DI登録
```csharp
services.AddSingleton<IKsqlConfigurationManager, KsqlConfigurationManager>();
services.Configure<KafkaBusOptions>(configuration.GetSection("KafkaBus"));
services.Configure<KafkaProducerOptions>(configuration.GetSection("KafkaProducer"));
services.Configure<KafkaConsumerOptions>(configuration.GetSection("KafkaConsumer"));
services.Configure<RetryOptions>(configuration.GetSection("Retry"));
services.Configure<AvroSchemaRegistryOptions>(configuration.GetSection("AvroSchemaRegistry"));
```

### 設定取得
```csharp
public class MyKafkaService
{
    private readonly KafkaBusOptions _busOptions;
    private readonly KafkaProducerOptions _producerOptions;

    public MyKafkaService(IKsqlConfigurationManager configManager)
    {
        _busOptions = configManager.GetOptions<KafkaBusOptions>();
        _producerOptions = configManager.GetOptions<KafkaProducerOptions>();
    }
}
```

## 設計原則

1. **単一責任**: 各Optionsクラスは1つの領域の設定のみを担当
2. **不変性**: recordによる不変オブジェクト設計
3. **型安全**: enumによる選択肢の制限
4. **デフォルト値**: 適切なデフォルト値による設定簡素化
5. **拡張性**: 新しい設定項目の追加が容易

## 移行ガイド

### 旧KafkaMessageBusOptionsからの移行

```csharp
// 旧方式
var oldOptions = new KafkaMessageBusOptions
{
    BootstrapServers = "kafka:9092",
    ProducerAcks = Acks.All,
    ConsumerGroupId = "my-group"
};

// 新方式
var busOptions = new KafkaBusOptions { BootstrapServers = "kafka:9092" };
var producerOptions = new KafkaProducerOptions { Acks = Acks.All };
var consumerOptions = new KafkaConsumerOptions { GroupId = "my-group" };
```

### 設定ファイルの移行

```json
// 旧構造
{
  "Kafka": {
    "BootstrapServers": "kafka:9092",
    "ProducerAcks": "All",
    "ConsumerGroupId": "my-group",
    "RetryAttempts": 3,
    "SchemaRegistryUrl": "http://registry:8081"
  }
}

// 新構造
{
  "KafkaBus": {
    "BootstrapServers": "kafka:9092"
  },
  "KafkaProducer": {
    "Acks": "All"
  },
  "KafkaConsumer": {
    "GroupId": "my-group"
  },
  "Retry": {
    "MaxRetryAttempts": 3
  },
  "AvroSchemaRegistry": {
    "Url": "http://registry:8081"
  }
}
```

## 拡張方法

### 新しいオプションクラスの追加

1. **Optionsクラス作成**
```csharp
public record MyCustomOptions
{
    public string CustomProperty { get; init; } = "default";
}
```

2. **バリデーション追加**
```csharp
public class MyCustomOptionsValidator : IOptionValidator<MyCustomOptions>
{
    public ValidationResult Validate(MyCustomOptions options)
    {
        // バリデーションロジック
    }
}
```

3. **DI登録**
```csharp
services.Configure<MyCustomOptions>(configuration.GetSection("MyCustom"));
services.AddSingleton<IOptionValidator<MyCustomOptions>, MyCustomOptionsValidator>();
```

## トラブルシューティング

### よくある問題

1. **設定が反映されない**
   - セクション名が正しいか確認（`KafkaBus`、`KafkaProducer`等）
   - JSON構造が正しいか確認

2. **バリデーションエラー**
   - `ValidateAll()`でエラー詳細を確認
   - ログでバリデーション失敗原因を確認

3. **型変換エラー**
   - enum値が正しい文字列か確認
   - TimeSpan形式が正しいか確認（`"00:00:30"`）

### デバッグ方法

```csharp
// 現在の設定値を確認
var busOptions = configManager.GetOptions<KafkaBusOptions>();
Console.WriteLine($"BootstrapServers: {busOptions.BootstrapServers}");

// 全設定のバリデーション
var result = configManager.ValidateAll();
foreach (var error in result.Errors)
{
    Console.WriteLine($"❌ {error}");
}
```
