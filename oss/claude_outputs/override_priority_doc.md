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

### ConfigMap/Secretとの統合
```yaml
# configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: ksql-config
data:
  KafkaBus__BootstrapServers: "kafka-cluster:9092"
  KafkaBus__Consumer__GroupId: "production-group"

---
# secret.yaml
apiVersion: v1
kind: Secret
metadata:
  name: ksql-secrets
type: Opaque
stringData:
  KafkaBus__SaslUsername: "kafka-user"
  KafkaBus__SaslPassword: "secure-password"

---
# deployment.yaml
spec:
  template:
    spec:
      containers:
      - name: ksql-app
        envFrom:
        - configMapRef:
            name: ksql-config
        - secretRef:
            name: ksql-secrets
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

### 外部設定ファイル
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 外部設定ファイルの追加
builder.Configuration.AddJsonFile("/etc/ksql/config.json", optional: true);
builder.Configuration.AddJsonFile($"/etc/ksql/config.{builder.Environment.EnvironmentName}.json", optional: true);

// 環境変数の追加（最後に追加して最高優先度にする）
builder.Configuration.AddEnvironmentVariables();
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
            options = options with 
            { 
                BootstrapServers = "localhost:9092",
                ClientId = "debug-client"
            };
        });
    }
    
    // 実行時値による動的設定
    services.Configure<KafkaConsumerOptions>(options =>
    {
        options = options with
        {
            GroupId = $"app-{Environment.MachineName}-{Guid.NewGuid():N[..8]}"
        };
    });
}
```

### 後バインディング設定
```csharp
public class KafkaServiceSetup : IHostedService
{
    private readonly IOptionsMonitor<KafkaBusOptions> _optionsMonitor;
    
    public KafkaServiceSetup(IOptionsMonitor<KafkaBusOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 設定変更の監視
        _optionsMonitor.OnChange(OnOptionsChanged);
    }
    
    private void OnOptionsChanged(KafkaBusOptions options)
    {
        // 設定変更時の処理
        Console.WriteLine($"Configuration changed: {options.BootstrapServers}");
    }
}
```

## カスタムオーバーライドソース

### データベース設定ソース
```csharp
public class DatabaseConfigurationOverrideSource : IConfigurationOverrideSource
{
    private readonly DbContext _dbContext;
    
    public int Priority => 0; // 環境変数より高い優先度

    public DatabaseConfigurationOverrideSource(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string? GetValue(string key)
    {
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
        // データベース変更の監視実装
        _dbContext.Configurations.OnChanged += (key, value) => onChanged(key, value);
    }

    public void StopWatching()
    {
        // 監視停止処理
    }
}
```

### Azure Key Vault連携
```csharp
public class KeyVaultConfigurationOverrideSource : IConfigurationOverrideSource
{
    private readonly SecretClient _secretClient;
    
    public int Priority => 0; // 最高優先度

    public KeyVaultConfigurationOverrideSource(SecretClient secretClient)
    {
        _secretClient = secretClient;
    }

    public string? GetValue(string key)
    {
        try
        {
            var secret = _secretClient.GetSecret(ConvertToSecretName(key));
            return secret.Value.Value;
        }
        catch (RequestFailedException)
        {
            return null;
        }
    }

    private string ConvertToSecretName(string configKey)
    {
        // KafkaBus__Producer__Acks -> kafkabus-producer-acks
        return configKey.Replace("__", "-").ToLowerInvariant();
    }
}
```

### カスタムソースの登録
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // データベース設定ソース
    services.AddScoped<IConfigurationOverrideSource, DatabaseConfigurationOverrideSource>();
    
    // Key Vault設定ソース
    services.AddSingleton<SecretClient>(provider =>
        new SecretClient(new Uri("https://myvault.vault.azure.net/"), new DefaultAzureCredential()));
    services.AddSingleton<IConfigurationOverrideSource, KeyVaultConfigurationOverrideSource>();
    
    services.AddSingleton<IKsqlConfigurationManager, KsqlConfigurationManager>();
}
```

## 実行時監視・変更

### 設定変更の監視
```csharp
public class ConfigurationWatcher : IHostedService
{
    private readonly IKsqlConfigurationManager _configManager;
    private readonly ILogger<ConfigurationWatcher> _logger;

    public ConfigurationWatcher(
        IKsqlConfigurationManager configManager,
        ILogger<ConfigurationWatcher> logger)
    {
        _configManager = configManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _configManager.ConfigurationChanged += OnConfigurationChanged;
        return Task.CompletedTask;
    }

    private async void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        _logger.LogInformation("Configuration changed: {OptionsType}", e.OptionsType);
        
        // 必要に応じて関連サービスの再初期化
        if (e.OptionsType.Contains("KafkaBus"))
        {
            await RestartKafkaConnections();
        }
    }

    private async Task RestartKafkaConnections()
    {
        // Kafka接続の再初期化ロジック
        _logger.LogInformation("Restarting Kafka connections due to configuration change");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _configManager.ConfigurationChanged -= OnConfigurationChanged;
        return Task.CompletedTask;
    }
}
```

### 動的リロード
```csharp
public class ConfigurationController : ControllerBase
{
    private readonly IKsqlConfigurationManager _configManager;

    public ConfigurationController(IKsqlConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    [HttpPost("reload")]
    public async Task<IActionResult> ReloadConfiguration()
    {
        try
        {
            await _configManager.ReloadAsync();
            return Ok(new { message = "Configuration reloaded successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("current")]
    public IActionResult GetCurrentConfiguration()
    {
        var config = new
        {
            KafkaBus = _configManager.GetOptions<KafkaBusOptions>(),
            Producer = _configManager.GetOptions<KafkaProducerOptions>(),
            Consumer = _configManager.GetOptions<KafkaConsumerOptions>()
        };
        
        return Ok(config);
    }

    [HttpGet("validate")]
    public IActionResult ValidateConfiguration()
    {
        var result = _configManager.ValidateAll();
        
        if (result.IsValid)
        {
            return Ok(new { valid = true, warnings = result.Warnings });
        }
        
        return BadRequest(new { valid = false, errors = result.Errors, warnings = result.Warnings });
    }
}
```

## トラブルシューティング

### 優先順位の確認
```csharp
public class ConfigurationDebugger
{
    public void DiagnoseConfiguration(IConfiguration configuration)
    {
        // 各プロバイダーの優先順位を確認
        var providers = ((IConfigurationRoot)configuration).Providers.ToList();
        
        for (int i = 0; i < providers.Count; i++)
        {
            Console.WriteLine($"Priority {i}: {providers[i].GetType().Name}");
        }

        // 特定キーの値とソースを確認
        var key = "KafkaBus:BootstrapServers";
        foreach (var provider in providers)
        {
            if (provider.TryGet(key, out var value))
            {
                Console.WriteLine($"{provider.GetType().Name}: {key} = {value}");
            }
        }
    }
}
```

### 環境変数の検証
```bash
# 現在の環境変数を確認
env | grep KafkaBus__

# 特定の設定値を確認
echo $KafkaBus__BootstrapServers

# Docker内での確認
docker exec -it container-name env | grep KafkaBus__
```

### 設定階層の可視化
```csharp
public class ConfigurationVisualizer
{
    public void PrintConfigurationHierarchy(IConfiguration configuration)
    {
        PrintSection(configuration, "", 0);
    }

    private void PrintSection(IConfiguration section, string path, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        
        foreach (var child in section.GetChildren())
        {
            var fullPath = string.IsNullOrEmpty(path) ? child.Key : $"{path}:{child.Key}";
            
            if (child.Value != null)
            {
                Console.WriteLine($"{indentStr}{child.Key}: {child.Value}");
            }
            else
            {
                Console.WriteLine($"{indentStr}{child.Key}:");
                PrintSection(child, fullPath, indent + 1);
            }
        }
    }
}
```

## ベストプラクティス

### 1. セキュリティ考慮
- 機密情報は環境変数またはKey Vaultを使用
- 設定ファイルにパスワードを平文で保存しない
- 本番環境では設定の動的変更を制限

### 2. 環境分離
- 環境ごとに明確な設定ファイルを作成
- 環境変数で環境固有の値をオーバーライド
- 開発環境では緩い設定、本番環境では厳格な設定

### 3. 監視・ログ
- 設定変更をログに記録
- 設定エラーをアラートで通知
- 定期的な設定バリデーション実行

### 4. 文書化
- 環境変数の命名規則を文書化
- 設定変更の手順書を作成
- 各環境の設定差分を管理
