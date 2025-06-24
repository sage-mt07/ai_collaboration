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

### 3. バリデーション実行タイミング
- **起動時**: アプリケーション開始前の全設定検証
- **設定取得時**: `GetOptions<T>()`呼び出し時の個別検証
- **変更時**: 設定変更検知時の再検証
- **定期実行**: ヘルスチェック時の継続的検証

## バリデーションルール

### KafkaBusOptions

#### Fatal Errors
```csharp
// BootstrapServers
- 値が null または空文字列
- 不正なホスト:ポート形式（例: "invalid:port"）
- ポート番号が範囲外（1-65535以外）

// Timeout値
- RequestTimeoutMs <= 0
- MetadataMaxAgeMs <= 0

// セキュリティ設定
- SecurityProtocol が SaslPlaintext または SaslSsl の場合、SaslMechanism が必須
- SASL認証時に Username または Password が未設定
- SSL設定時に必要な証明書パスが未設定または不正
```

#### Warnings
```csharp
// パフォーマンス警告
- RequestTimeoutMs > 60000 (1分超過)
- MetadataMaxAgeMs < 30000 (30秒未満)

// セキュリティ警告
- SecurityProtocol が Plaintext（本番環境で非推奨）
- SaslPassword がハードコーディング（環境変数推奨）
- ClientId にデフォルト値を使用（本番環境で非推奨）
```

### KafkaProducerOptions

#### Fatal Errors
```csharp
// バッチ設定
- BatchSize <= 0 または > 16777216 (16MB)
- BufferMemory <= 0
- MaxRequestSize <= 0 または > BatchSize

// タイムアウト設定
- MessageTimeoutMs <= 0
- LingerMs < 0 または > MessageTimeoutMs

// トランザクション設定
- EnableIdempotence が true で TransactionalId が null
- TransactionTimeoutMs <= 0 または > MessageTimeoutMs
- MaxInFlightRequestsPerConnection > 5 かつ EnableIdempotence が true
```

#### Warnings
```csharp
// パフォーマンス警告
- BatchSize < 1024 (1KB未満、効率低下)
- BatchSize > 1048576 (1MB超過、メモリ使用量増加)
- LingerMs > 1000 (1秒超過、遅延増加)
- MaxInFlightRequestsPerConnection > 5 (順序保証リスク)

// 信頼性警告
- Acks が None（データ損失リスク）
- EnableIdempotence が false（重複配信リスク）
- CompressionType が None（帯域使用量増加）
```

### KafkaConsumerOptions

#### Fatal Errors
```csharp
// グループ設定
- GroupId が null または空文字列
- SessionTimeoutMs <= 0 または > 300000 (5分)
- HeartbeatIntervalMs <= 0
- HeartbeatIntervalMs >= SessionTimeoutMs / 3

// フェッチ設定
- FetchMinBytes <= 0
- MaxPartitionFetchBytes <= 0
- MaxPollIntervalMs <= SessionTimeoutMs
- FetchMaxWaitMs < 0
```

#### Warnings
```csharp
// パフォーマンス警告
- FetchMaxWaitMs > 5000 (5秒超過)
- MaxPartitionFetchBytes > 10485760 (10MB超過)
- FetchMinBytes > MaxPartitionFetchBytes

// 運用警告
- EnableAutoCommit が false で手動コミット実装が未確認
- AutoOffsetReset が None（オフセット未存在時の動作不明）
- SessionTimeoutMs < 6000 (6秒未満、接続不安定リスク)
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
- RetriableExceptions が null
- NonRetriableExceptions が null
```

#### Warnings
```csharp
// パフォーマンス警告
- MaxRetryAttempts > 10（過度なリトライ）
- MaxDelay > TimeSpan.FromMinutes(5)（長時間ブロック）
- BackoffMultiplier > 5.0（急激な遅延増加）

// 設定警告
- RetriableExceptions が空（すべての例外でリトライ）
- EnableJitter が false（Thundering Herd問題）
- InitialDelay > TimeSpan.FromSeconds(1)（初回遅延が長い）
```

### AvroSchemaRegistryOptions

#### Fatal Errors
```csharp
// URL設定
- Url が null または空文字列
- Url が不正なURI形式
- Url がHTTPスキーム以外

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
- RequestTimeoutMs > 60000（長時間待機）

// 運用警告
- AutoRegisterSchemas が true（スキーマ汚染リスク）
- UseLatestVersion が true（バージョン互換性リスク）
- BasicAuth認証情報がハードコーディング
```

## カスタムバリデーション

### 基本的なバリデーター実装
```csharp
public class BusinessRuleValidator : IOptionValidator<KafkaBusOptions>
{
    private readonly ILogger<BusinessRuleValidator> _logger;
    
    public BusinessRuleValidator(ILogger<BusinessRuleValidator> logger)
    {
        _logger = logger;
    }

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

        // ネットワーク接続の検証
        if (!IsServerReachable(options.BootstrapServers))
        {
            warnings.Add($"Cannot reach Kafka servers: {options.BootstrapServers}");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    public void ValidateAndThrow(KafkaBusOptions options)
    {
        var result = Validate(options);
        if (!result.IsValid)
        {
            var message = $"KafkaBusOptions validation failed: {string.Join(", ", result.Errors)}";
            _logger.LogError(message);
            throw new ConfigurationValidationException(message);
        }

        // 警告をログ出力
        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("KafkaBusOptions warning: {Warning}", warning);
        }
    }

    private bool IsServerReachable(string? servers)
    {
        if (string.IsNullOrEmpty(servers)) return false;
        
        try
        {
            foreach (var server in servers.Split(','))
            {
                var parts = server.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                {
                    using var client = new TcpClient();
                    var result = client.BeginConnect(parts[0], port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    if (success) return true;
                }
            }
        }
        catch
        {
            // 接続チェック失敗は警告レベル
        }
        
        return false;
    }
}
```

### 環境依存バリデーター
```csharp
public class EnvironmentSpecificValidator : IOptionValidator<KafkaBusOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<EnvironmentSpecificValidator> _logger;

    public EnvironmentSpecificValidator(
        IHostEnvironment environment,
        ILogger<EnvironmentSpecificValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidationResult Validate(KafkaBusOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (_environment.IsProduction())
        {
            // 本番環境の厳格なルール
            if (options.SecurityProtocol == SecurityProtocol.Plaintext)
            {
                errors.Add("Production environment requires secure protocol");
            }

            if (options.ClientId == "ksql-dsl-client") // デフォルト値
            {
                errors.Add("Production environment requires custom ClientId");
            }

            if (options.BootstrapServers?.Contains("localhost") == true)
            {
                errors.Add("Production environment cannot use localhost");
            }
        }
        else if (_environment.IsDevelopment())
        {
            // 開発環境の緩いルール
            if (options.SecurityProtocol != SecurityProtocol.Plaintext)
            {
                warnings.Add("Development environment typically uses Plaintext protocol");
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    public void ValidateAndThrow(KafkaBusOptions options)
    {
        var result = Validate(options);
        if (!result.IsValid)
        {
            throw new ConfigurationValidationException(
                $"Environment-specific validation failed for {_environment.EnvironmentName}: " +
                string.Join(", ", result.Errors));
        }
    }
}
```

### 複合バリデーター
```csharp
public class CompositeValidator : IOptionValidator<KafkaBusOptions>
{
    private readonly IEnumerable<IOptionValidator<KafkaBusOptions>> _validators;
    private readonly ILogger<CompositeValidator> _logger;

    public CompositeValidator(
        IEnumerable<IOptionValidator<KafkaBusOptions>> validators,
        ILogger<CompositeValidator> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public ValidationResult Validate(KafkaBusOptions options)
    {
        var allErrors = new List<string>();
        var allWarnings = new List<string>();

        foreach (var validator in _validators)
        {
            try
            {
                var result = validator.Validate(options);
                allErrors.AddRange(result.Errors);
                allWarnings.AddRange(result.Warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validator {ValidatorType} failed", validator.GetType().Name);
                allErrors.Add($"Validator {validator.GetType().Name} failed: {ex.Message}");
            }
        }

        return new ValidationResult
        {
            IsValid = allErrors.Count == 0,
            Errors = allErrors,
            Warnings = allWarnings
        };
    }

    public void ValidateAndThrow(KafkaBusOptions options)
    {
        var result = Validate(options);
        if (!result.IsValid)
        {
            throw new ConfigurationValidationException(
                $"Composite validation failed: {string.Join(", ", result.Errors)}");
        }
    }
}
```

## 実行時バリデーション

### 起動時検証
```csharp
public class Startup
{
    public void Configure(IApplicationBuilder app, IKsqlConfigurationManager configManager)
    {
        // アプリケーション起動前に全設定を検証
        var validationResult = configManager.ValidateAll();
        
        if (!validationResult.IsValid)
        {
            var errors = string.Join(Environment.NewLine, validationResult.Errors);
            throw new InvalidOperationException($"Configuration validation failed:{Environment.NewLine}{errors}");
        }

        // 警告をログ出力
        foreach (var warning in validationResult.Warnings)
        {
            app.ApplicationServices.GetRequiredService<ILogger<Startup>>()
                .LogWarning("Configuration warning: {Warning}", warning);
        }

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
```

### 定期バリデーション
```csharp
public class ConfigurationHealthCheckService : BackgroundService
{
    private readonly IKsqlConfigurationManager _configManager;
    private readonly ILogger<ConfigurationHealthCheckService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ConfigurationHealthCheckService(
        IKsqlConfigurationManager configManager,
        ILogger<ConfigurationHealthCheckService> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = _configManager.ValidateAll();
                
                if (!result.IsValid)
                {
                    _logger.LogError("Configuration validation failed: {Errors}", 
                        string.Join(", ", result.Errors));
                }
                else if (result.Warnings.Any())
                {
                    _logger.LogWarning("Configuration warnings: {Warnings}", 
                        string.Join(", ", result.Warnings));
                }
                else
                {
                    _logger.LogDebug("Configuration validation passed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate configuration");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

### ヘルスチェック統合
```csharp
public class ConfigurationHealthCheck : IHealthCheck
{
    private readonly IKsqlConfigurationManager _configManager;

    public ConfigurationHealthCheck(IKsqlConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = _configManager.ValidateAll();
            
            if (!result.IsValid)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Configuration validation failed",
                    data: new Dictionary<string, object>
                    {
                        ["errors"] = result.Errors,
                        ["warnings"] = result.Warnings
                    }));
            }

            if (result.Warnings.Any())
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Configuration has warnings",
                    data: new Dictionary<string, object>
                    {
                        ["warnings"] = result.Warnings
                    }));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Configuration is valid"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Configuration validation failed with exception", ex));
        }
    }
}

// DI登録
services.AddHealthChecks()
    .AddCheck<ConfigurationHealthCheck>("configuration");
```

## デバッグとトラブルシューティング

### バリデーション詳細の取得
```csharp
public class ConfigurationDiagnostics
{
    private readonly IKsqlConfigurationManager _configManager;
    private readonly ILogger<ConfigurationDiagnostics> _logger;

    public ConfigurationDiagnostics(
        IKsqlConfigurationManager configManager,
        ILogger<ConfigurationDiagnostics> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public void DiagnoseConfiguration()
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
                var options = _configManager.GetType()
                    .GetMethod(nameof(IKsqlConfigurationManager.GetOptions))!
                    .MakeGenericMethod(type)
                    .Invoke(_configManager, null);

                _logger.LogInformation("✅ {OptionsType}: Valid", type.Name);
                _logger.LogDebug("{OptionsType} configuration: {@Options}", type.Name, options);
            }
            catch (ConfigurationValidationException ex)
            {
                _logger.LogError("❌ {OptionsType}: {Error}", type.Name, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {OptionsType}: Unexpected error", type.Name);
            }
        }
    }

    public Dictionary<string, object> GetDiagnosticsReport()
    {
        var report = new Dictionary<string, object>();
        
        try
        {
            var validationResult = _configManager.ValidateAll();
            
            report["isValid"] = validationResult.IsValid;
            report["errors"] = validationResult.Errors;
            report["warnings"] = validationResult.Warnings;
            report["timestamp"] = DateTime.UtcNow;
            
            // 各オプションの詳細
            var options = new Dictionary<string, object>();
            options["kafkaBus"] = _configManager.GetOptions<KafkaBusOptions>();
            options["producer"] = _configManager.GetOptions<KafkaProducerOptions>();
            options["consumer"] = _configManager.GetOptions<KafkaConsumerOptions>();
            options["retry"] = _configManager.GetOptions<RetryOptions>();
            options["avroRegistry"] = _configManager.GetOptions<AvroSchemaRegistryOptions>();
            
            report["currentOptions"] = options;
        }
        catch (Exception ex)
        {
            report["error"] = ex.Message;
            report["isValid"] = false;
        }
        
        return report;
    }
}
```

### バリデーション無効化（テスト用）
```csharp
public class TestKsqlConfigurationManager : KsqlConfigurationManager
{
    public bool ValidationEnabled { get; set; } = false;

    public TestKsqlConfigurationManager(IConfiguration configuration, ILoggerFactory loggerFactory) 
        : base(configuration, loggerFactory)
    {
    }

    public override ValidationResult ValidateAll()
    {
        if (!ValidationEnabled)
        {
            return ValidationResult.Success();
        }
        
        return base.ValidateAll();
    }

    protected override void ValidateOptions<T>(T options, Type type)
    {
        if (!ValidationEnabled)
        {
            return;
        }
        
        base.ValidateOptions(options, type);
    }
}

// テストでの使用
services.AddSingleton<IKsqlConfigurationManager>(provider =>
    new TestKsqlConfigurationManager(
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<ILoggerFactory>())
    {
        ValidationEnabled = false // テスト時はバリデーション無効
    });
```

## ベストプラクティス

### 1. 段階的導入
```csharp
// Phase 1: Warningレベルから開始
services.Configure<ValidationOptions>(options =>
{
    options.TreatWarningsAsErrors = false;
    options.EnableNetworkValidation = false;
});

// Phase 2: 徐々に厳格化
services.Configure<ValidationOptions>(options =>
{
    options.TreatWarningsAsErrors = true;
    options.EnableNetworkValidation = true;
});
```

### 2. 環境別ルール
```csharp
public void ConfigureServices(IServiceCollection services)
{
    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
    {
        // 本番環境では厳格なバリデーション
        services.AddSingleton<IOptionValidator<KafkaBusOptions>, StrictValidator>();
    }
    else
    {
        // 開発環境では緩いバリデーション
        services.AddSingleton<IOptionValidator<KafkaBusOptions>, RelaxedValidator>();
    }
}
```

### 3. ドキュメント化
- バリデーションルールの理由を明文化
- 設定例とエラーメッセージの対応表を作成
- 各環境での必須設定項目を明確化

### 4. 継続的改善
- 運用中に発見された問題をバリデーションルールに追加
- 定期的なルール見直しとメンテナンス
- チーム内での設定ベストプラクティス共有