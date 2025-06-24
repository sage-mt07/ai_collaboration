# Configuration層ドキュメント

## docs/Configuration/options_structure.md

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
services.AddSingleton<IConfigurationManager, ConfigurationManager>();
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

    public MyKafkaService(IConfigurationManager configManager)
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

---

## docs/Configuration/override_priority.md

# Configuration Override Priority システム

## 概要

KsqlDslの設定システムは、複数のソースからの設定値を優先順位に基づいて統合する仕組みを提供します。
これにより、開発・テスト・本番環境での柔軟な設定管理が可能になります。

## 優先順位ルール

設定値は以下の優先順位で上書きされます（番号が小さいほど高優先度）：

### 1. 環境変数 (Priority: 1)
- 最高優先度
- `KafkaBus__Section__Property` 形式
- 実行時の動的変更は不可

### 2. コマンドライン引数 (Priority: 2)
- 実行時指定
- `--KafkaBus:Section:Property=value` 形式

### 3. 設定ファイル (Priority: 3)
- `appsettings.json`, `appsettings.{Environment}.json`
- アプリケーション起動時に読み込み

### 4. プログラムによる設定 (Priority: 4)
- 最低優先度
- DI登録時の `Configure<T>()` による設定

## 環境変数オーバーライド

### 命名規則
```bash
# 基本形式
KafkaBus__<Section>__<Property>

# 例
KafkaBus__BootstrapServers=prod-kafka:9092
KafkaBus__Producer__Acks=All
KafkaBus__Consumer__GroupId=prod-consumer-group
KafkaBus__Retry__MaxRetryAttempts=10
KafkaBus__AvroSchemaRegistry__Url=https://schema-registry-prod:8081
```

### Docker環境での例
```yaml
# docker-compose.yml
services:
  ksql-app:
    environment:
      - KafkaBus__BootstrapServers=kafka1:9092,kafka2:9092
      - KafkaBus__Producer__Acks=All
      - KafkaBus__Consumer__GroupId=docker-consumer
      - KafkaBus__AvroSchemaRegistry__Url=http://schema-registry:8081
```

### Kubernetes環境での例
```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
spec:
  template:
    spec:
      containers:
      - name: ksql-app
        env:
        - name: KafkaBus__BootstrapServers
          value: "kafka-service:9092"
        - name: KafkaBus__Producer__Acks
          value: "All"
        - name: KafkaBus__Consumer__AutoOffsetReset
          value: "Earliest"
```

## 設定ファイルオーバーライド

### 環境別設定ファイル
```json
// appsettings.Development.json
{
  "KafkaBus": {
    "BootstrapServers": "localhost:9092",
    "ClientId": "dev-ksql-client"
  },
  "KafkaConsumer": {
    "GroupId": "dev-consumer-group",
    "AutoOffsetReset": "Earliest"
  }
}

// appsettings.Production.json
{
  "KafkaBus": {
    "BootstrapServers": "prod-kafka1:9092,prod-kafka2:9092,prod-kafka3:9092",
    "SecurityProtocol": "SaslSsl",
    "ClientId": "prod-ksql-client"
  },
  "KafkaConsumer": {
    "GroupId": "prod-consumer-group",
    "AutoOffsetReset": "Latest"
  }
}
```

## プログラムによるオーバーライド

### 実行時設定変更
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 基本設定
    services.Configure<KafkaBusOptions>(configuration.GetSection("KafkaBus"));
    
    // 条件付きオーバーライド
    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
    {
        services.Configure<KafkaBusOptions>(options =>
        {
            options.BootstrapServers = "localhost:9092";
            options.ClientId = "debug-client";
        });
    }
    
    // 実行時値による動的設定
    services.Configure<KafkaConsumerOptions>(options =>
    {
        options.GroupId = $"app-{Environment.MachineName}-{Guid.NewGuid():N[..8]}";
    });
}
```

## カスタムオーバーライドソース

### 独自ソースの実装
```csharp
public class DatabaseConfigurationOverrideSource : IConfigurationOverrideSource
{
    public int Priority => 1; // 環境変数と同等の優先度

    public string? GetValue(string key)
    {
        // データベースから設定値を取得
        return _dbContext.Configurations
            .FirstOrDefault(c => c.Key == key)?.Value;
    }

    public Dictionary<string, string> GetValues(string prefix)
    {
        return _dbContext.Configurations
            .Where(c => c.Key.StartsWith(prefix))
            .ToDictionary(c => c.Key, c => c.Value);
    }

    public void StartWatching(Action<string, string?> onChanged)
    {
        // データベース変更の監視を開始
        _dbContext.Configurations.OnChanged += (key, value) => onChanged(key, value);
    }
}
```

### カスタムソースの登録
```csharp
services.AddSingleton<IConfigurationOverrideSource, DatabaseConfigurationOverrideSource>();
```

## 実行時監視・変更

### 設定変更の監視
```csharp
public class ConfigurationWatcher
{
    public ConfigurationWatcher(IConfigurationManager configManager)
    {
        configManager.ConfigurationChanged += OnConfigurationChanged;
    }

    private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
    {
        _logger.LogInformation("Configuration changed: {OptionsType}", e.OptionsType);
        
        // 必要に応じて関連サービスの再初期化
        if (e.OptionsType == nameof(KafkaBusOptions))
        {
            await RestartKafkaConnections();
        }
    }
}
```

### 動的リロード
```csharp
// 設定の再読み込み
await configurationManager.ReloadAsync();

// 特定設定の取得（最新値）
var currentOptions = configurationManager.GetOptions<KafkaBusOptions>();
```

## トラブルシューティング

### 優先順位の確認
```csharp
// 現在の設定値とソースを確認
var debugInfo = configurationManager.GetDebugInfo<KafkaBusOptions>();
Console.WriteLine($"BootstrapServers: {debugInfo.Value.BootstrapServers}");
Console.WriteLine($"Source: {debugInfo.Source}"); // Environment | File | Program
```

### 環境変数の検証
```bash
# 現在の環境変数を確認
env | grep KafkaBus__

# 特定の設定値を確認
echo $KafkaBus__BootstrapServers
```

---

## docs/Configuration/validation_policy.md

# Configuration Validation Policy

## 概要

KsqlDslの設定バリデーションシステムは、実行時エラーを防ぎ、設定ミスによる問題を早期発見するための包括的な検証機能を提供します。

## バリデーション戦略

### 1. 段階的バリデーション
- **構文バリデーション**: 型変換・フォーマット検証
- **セマンティックバリデーション**: 値の妥当性・関連性検証
- **業務ロジックバリデーション**: アプリケーション固有のルール検証

### 2. エラーハンドリング方針
- **Fatal Error**: 即座に例外をスロー（アプリケーション停止）
- **Warning**: ログ出力のみ（アプリケーション継続）
- **Info**: 情報レベルのログ出力

## バリデーションルール

### KafkaBusOptions

#### Fatal Errors
```csharp
// BootstrapServers
- 値が null または空文字列
- 不正なホスト:ポート形式
- 到達不可能なホスト（オプション）

// Timeout値
- RequestTimeoutMs <= 0
- MetadataMaxAgeMs <= 0

// セキュリティ設定
- SecurityProtocol が SaslPlaintext または SaslSsl の場合、SaslMechanism が必須
- SASL認証時に Username または Password が未設定
```

#### Warnings
```csharp
// パフォーマンス警告
- RequestTimeoutMs > 60000 (1分超過)
- MetadataMaxAgeMs < 30000 (30秒未満)

// セキュリティ警告
- SecurityProtocol が Plaintext（本番環境で非推奨）
- SaslPassword がハードコーディング
```

### KafkaProducerOptions

#### Fatal Errors
```csharp
// バッチ設定
- BatchSize <= 0
- BufferMemory <= 0
- MaxRequestSize <= 0

// タイムアウト設定
- MessageTimeoutMs <= 0
- LingerMs < 0

// トランザクション設定
- EnableIdempotence が true で TransactionalId が null
- TransactionTimeoutMs <= 0
```

#### Warnings
```csharp
// パフォーマンス警告
- BatchSize > 1048576 (1MB超過)
- LingerMs > 1000 (1秒超過)
- MaxInFlightRequestsPerConnection > 5 (順序保証リスク)

// 信頼性警告
- Acks が None（データ損失リスク）
- EnableIdempotence が false（重複配信リスク）
```

### KafkaConsumerOptions

#### Fatal Errors
```csharp
// グループ設定
- GroupId が null または空文字列
- SessionTimeoutMs <= 0
- HeartbeatIntervalMs <= 0
- HeartbeatIntervalMs >= SessionTimeoutMs

// フェッチ設定
- FetchMinBytes <= 0
- MaxPartitionFetchBytes <= 0
- MaxPollIntervalMs <= SessionTimeoutMs
```

#### Warnings
```csharp
// パフォーマンス警告
- FetchMaxWaitMs > 5000 (5秒超過)
- MaxPartitionFetchBytes > 10485760 (10MB超過)

// 運用警告
- EnableAutoCommit が false で手動コミット実装が未確認
- AutoOffsetReset が None（オフセット未存在時の動作不明）
```

### RetryOptions

#### Fatal Errors
```csharp
// リトライ回数
- MaxRetryAttempts < 0

// 遅延設定
- InitialDelay <= TimeSpan.Zero
- MaxDelay <= InitialDelay
- BackoffMultiplier <= 1.0

// 例外設定
- RetriableExceptions と NonRetriableExceptions に重複
```

#### Warnings
```csharp
// パフォーマンス警告
- MaxRetryAttempts > 10（過度なリトライ）
- MaxDelay > TimeSpan.FromMinutes(5)（長時間ブロック）

// 設定警告
- RetriableExceptions が空（すべての例外でリトライ）
- EnableJitter が false（Thundering Herd問題）
```

### AvroSchemaRegistryOptions

#### Fatal Errors
```csharp
// URL設定
- Url が null または空文字列
- Url が不正なURI形式

// キャッシュ設定
- MaxCachedSchemas <= 0
- CacheExpirationTime <= TimeSpan.Zero
- RequestTimeoutMs <= 0
```

#### Warnings
```csharp
// パフォーマンス警告
- MaxCachedSchemas > 10000（メモリ使用量）
- CacheExpirationTime < TimeSpan.FromMinutes(1)（頻繁な再取得）

// 運用警告
- AutoRegisterSchemas が true（スキーマ汚染リスク）
- UseLatestVersion が true（バージョン互換性リスク）
```

## カスタムバリデーション

### バリデーターの実装
```csharp
public class BusinessRuleValidator : IOptionValidator<KafkaBusOptions>
{
    public ValidationResult Validate(KafkaBusOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 業務固有のルール
        if (options.ClientId?.StartsWith("prod-") == true && 
            options.SecurityProtocol == SecurityProtocol.Plaintext)
        {
            errors.Add("Production clients must use secure protocol");
        }

        if (options.BootstrapServers?.Contains("localhost") == true &&
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
        {
            warnings.Add("Localhost servers detected in production environment");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}
```

### バリデーターの登録
```csharp
services.AddSingleton<IOptionValidator<KafkaBusOptions>, BusinessRuleValidator>();
```

## 実行時バリデーション

### 自動バリデーション
```csharp
// 設定取得時に自動実行
var options = configurationManager.GetOptions<KafkaBusOptions>(); // バリデーション実行

// 手動バリデーション
var result = configurationManager.ValidateAll();
if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        _logger.LogError("Configuration error: {Error}", error);
    }
    throw new ConfigurationValidationException("Invalid configuration detected");
}
```

### 起動時検証
```csharp
public class Startup
{
    public void Configure(IApplicationBuilder app, IConfigurationManager configManager)
    {
        // アプリケーション起動前に全設定を検証
        var validationResult = configManager.ValidateAll();
        
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors);
            throw new InvalidOperationException($"Configuration validation failed: {errors}");
        }

        // 警告をログ出力
        foreach (var warning in validationResult.Warnings)
        {
            _logger.LogWarning("Configuration warning: {Warning}", warning);
        }

        app.UseRouting();
        // ... その他の設定
    }
}
```

## デバッグとトラブルシューティング

### バリデーション詳細の取得
```csharp
public class ConfigurationDiagnostics
{
    public void DiagnoseConfiguration(IConfigurationManager configManager)
    {
        var optionsTypes = new[]
        {
            typeof(KafkaBusOptions),
            typeof(KafkaProducerOptions),
            typeof(KafkaConsumerOptions),
            typeof(RetryOptions),
            typeof(AvroSchemaRegistryOptions)
        };

        foreach (var type in optionsTypes)
        {
            try
            {
                var options = configManager.GetType()
                    .GetMethod(nameof(IConfigurationManager.GetOptions))!
                    .MakeGenericMethod(type)
                    .Invoke(configManager, null);

                _logger.LogInformation("✅ {OptionsType}: Valid", type.Name);
            }
            catch (ConfigurationValidationException ex)
            {
                _logger.LogError("❌ {OptionsType}: {Error}", type.Name, ex.Message);
            }
        }
    }
}
```

### バリデーション無効化（テスト用）
```csharp
// テスト環境でのバリデーション無効化
public class TestConfigurationManager : ConfigurationManager
{
    protected override bool ShouldValidate => false;
    
    public TestConfigurationManager(IConfiguration configuration) 
        : base(configuration, NullLogger<ConfigurationManager>.Instance)
    {
    }
}
```

## ベストプラクティス

### 1. 段階的導入
- 既存システムでは Warning レベルから開始
- 徐々に Fatal Error レベルを追加

### 2. 環境別ルール
- 開発環境では緩い検証
- 本番環境では厳格な検証

### 3. ドキュメント化
- バリデーションルールの理由を明文化
- 設定例とエラーメッセージの対応表を作成

### 4. 継続的改善
- 運用中に発見された問題をバリデーションルールに追加
- 定期的なルール見直し