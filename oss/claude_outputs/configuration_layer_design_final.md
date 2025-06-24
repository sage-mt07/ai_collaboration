# Configuration層 設計書（最終版）

## 📋 **概要**

KsqlDsl Configuration層の設計・実装方針を定義する。
Configuration層は「**各namespaceからの要求に応える窓口**」として、統合設定+自動分散方式により、ユーザーには最高にシンプルな体験を提供し、内部では各namespace層に適切な設定を分散する。

---

## 🎯 **設計方針**

### **1. 統合設定+自動分散方式**
- **ユーザー向け**: 1つの`KsqlDslOptions`で全設定を統合管理
- **内部処理**: 各namespace層のInterfaceに自動分散
- **シンプルAPI**: `services.AddKsqlDsl(config)`で完了

### **2. Interface実装による型安全性**
- 各Sectionクラスが対応するInterfaceを直接実装
- 汎用Converterでリフレクションベース自動変換
- 新namespace追加時の拡張が容易

### **3. 単一例外による統一エラーハンドリング**
- `KsqlDslConfigurationException`1つで全設定エラーを処理
- ファクトリメソッドで用途別エラーメッセージ生成
- 環境別設定ファイルでデバッグ対応（Debug用例外不要）

---

## 📊 **削除・整理結果**

### **削除前後の比較**
| 項目 | 削除前 | 削除後 | 削減率 |
|------|-------|-------|-------|
| **総ファイル数** | 38ファイル | 8ファイル | **79%削減** |
| **Abstractionsファイル** | 21ファイル | 0ファイル | **100%削除** |
| **責務** | 複雑・多岐 | 窓口のみ | **明確化** |

### **削除対象 (35ファイル)**

#### **Validation フォルダ全体（4ファイル）**
- `IOptionValidator.cs`, `DefaultOptionValidator.cs`, `ValidationResult.cs`, `ValidationService.cs`
- **削除理由**: Confluent.Kafkaの自動検証で十分

#### **Extensions フォルダ全体（2ファイル）**
- `KafkaConfigurationExtensions.cs`, `KafkaContextOptionsBuilderExtensions.cs`
- **削除理由**: Confluent.Kafka直接使用により変換不要

#### **Overrides フォルダ全体（2ファイル）**
- `IConfigurationOverrideSource.cs`, `EnvironmentOverrideProvider.cs`
- **削除理由**: Microsoft.Extensions.Configurationと100%重複

#### **Options フォルダ全体（2ファイル）**
- `AvroHealthCheckOptions.cs`, `AvroRetryPolicy.cs`
- **削除理由**: 他層の責務

#### **Builders フォルダ全体（1ファイル）**
- `KafkaContextOptionsBuilder.cs`
- **削除理由**: 削除対象クラスとの連動

#### **Abstractions 内削除（18ファイル）**
- **Producer/Consumer詳細設定**: `KafkaProducerOptions.cs`, `KafkaConsumerOptions.cs`, `KafkaBatchOptions.cs`, `KafkaFetchOptions.cs`, `KafkaSubscriptionOptions.cs`
- **Pool関連**: `ProducerPoolConfig.cs`, `ConsumerPoolConfig.cs`
- **Health関連**: `ProducerHealthThresholds.cs`, `ConsumerHealthThresholds.cs`
- **重複Enum**: `AutoOffsetReset.cs`, `SecurityProtocol.cs`, `IsolationLevel.cs`
- **他層責務**: `SchemaGenerationOptions.cs`, `RetryOptions.cs`, `IOptionsBuilder.cs`, `IKsqlConfigurationManager.cs`
- **複雑設定**: `KafkaContextOptions.cs`

#### **ルートファイル削除（6ファイル）**
- `KsqlConfigurationManager.cs`, `MergedTopicConfig.cs`, `ModelBindingService.cs`, `TopicOverride.cs`, `TopicOverrideService.cs`
- **削除理由**: 削除対象機能との連動

---

## 📁 **最終ファイル構成**

```
src/Configuration/
├── KsqlDslOptions.cs                      - 統合設定メインクラス
├── ValidationMode.cs                      - 共通enum（厳格/緩いモード）
├── Sections/
│   ├── KafkaSection.cs                   - IKafkaBusConfiguration実装
│   ├── SchemaRegistrySection.cs          - IAvroSchemaRegistryConfiguration実装
│   └── MetricsSection.cs                 - IBasicMetricsConfiguration実装
├── Common/
│   └── OptionsConverter.cs               - 汎用Interface変換器
├── Exceptions/
│   └── KsqlDslConfigurationException.cs  - 統一設定例外
└── Extensions/
    └── ServiceCollectionExtensions.cs    - DI登録拡張メソッド
```

---

## 🔧 **主要クラス設計**

### **1. KsqlDslOptions（統合設定）**
```csharp
namespace KsqlDsl.Configuration;

/// <summary>
/// KsqlDsl統合設定
/// ユーザーがappsettings.jsonで設定する全項目を管理
/// </summary>
public record KsqlDslOptions
{
    /// <summary>
    /// Kafka関連設定（IKafkaBusConfiguration実装）
    /// </summary>
    public KafkaSection Kafka { get; init; } = new();
    
    /// <summary>
    /// Schema Registry関連設定（IAvroSchemaRegistryConfiguration実装）
    /// </summary>
    public SchemaRegistrySection SchemaRegistry { get; init; } = new();
    
    /// <summary>
    /// メトリクス関連設定（IBasicMetricsConfiguration実装）
    /// </summary>
    public MetricsSection Metrics { get; init; } = new();
    
    /// <summary>
    /// 検証モード（全namespace共通）
    /// </summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;
}
```

### **2. Interface実装Section例**
```csharp
namespace KsqlDsl.Configuration.Sections;

/// <summary>
/// Kafka設定セクション
/// IKafkaBusConfigurationを実装
/// </summary>
public record KafkaSection : IKafkaBusConfiguration
{
    private string _bootstrapServers = "localhost:9092";
    
    public string BootstrapServers
    {
        get => _bootstrapServers;
        init
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw KsqlDslConfigurationException.CreateForInvalidValue(
                    "Kafka", nameof(BootstrapServers), value,
                    "A non-empty server address (e.g., 'localhost:9092')"
                );
            }
            _bootstrapServers = value;
        }
    }
    
    public string ClientId { get; init; } = "ksql-dsl-client";
    public string DefaultGroupId { get; init; } = "ksql-dsl-consumer";
    public AutoOffsetReset DefaultAutoOffsetReset { get; init; } = AutoOffsetReset.Latest;
    // ... 他のプロパティ
    
    public ProducerConfig GetProducerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        ClientId = ClientId
    };
}
```

### **3. 汎用Options変換器**
```csharp
namespace KsqlDsl.Configuration.Common;

/// <summary>
/// 汎用Options変換器
/// リフレクションベースで任意のSection → Interface変換を実現
/// </summary>
internal static class OptionsConverter
{
    public static TInterface GetConfiguration<TInterface>(KsqlDslOptions ksqlOptions)
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(ksqlOptions);

        var interfaceType = typeof(TInterface);
        var properties = typeof(KsqlDslOptions).GetProperties();

        foreach (var property in properties)
        {
            var sectionValue = property.GetValue(ksqlOptions);
            if (sectionValue != null && interfaceType.IsAssignableFrom(sectionValue.GetType()))
            {
                return (TInterface)sectionValue;
            }
        }

        throw KsqlDslConfigurationException.CreateForInternalError(interfaceType.Name);
    }
}
```

### **4. 統一例外クラス**
```csharp
namespace KsqlDsl.Configuration.Exceptions;

/// <summary>
/// KsqlDsl設定関連の統一例外
/// 全ての設定エラーを一元管理
/// </summary>
public class KsqlDslConfigurationException : Exception
{
    public string? SectionName { get; }
    public string? PropertyName { get; }
    public object? InvalidValue { get; }

    // 設定値不正用ファクトリメソッド
    public static KsqlDslConfigurationException CreateForInvalidValue(
        string sectionName, string propertyName, 
        object? invalidValue, string expectedDescription)
    {
        var message = $"Invalid configuration in section '{sectionName}'. " +
                     $"Property '{propertyName}' has invalid value '{invalidValue}'. " +
                     $"Expected: {expectedDescription}. " +
                     $"Please check your appsettings.json configuration.";
        return new KsqlDslConfigurationException(message, sectionName, propertyName, invalidValue);
    }

    // URL不正用ファクトリメソッド
    public static KsqlDslConfigurationException CreateForInvalidUrl(
        string sectionName, string propertyName, string invalidUrl) { /* ... */ }

    // 内部エラー用ファクトリメソッド
    public static KsqlDslConfigurationException CreateForInternalError(
        string interfaceTypeName, Exception? innerException = null) { /* ... */ }
}
```

### **5. DI登録拡張メソッド**
```csharp
namespace KsqlDsl.Configuration.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsqlDsl(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        // メイン設定の登録
        services.Configure<KsqlDslOptions>(configurationSection);
        
        // 各Interface実装の自動登録
        var interfaceTypes = new[]
        {
            typeof(IKafkaBusConfiguration),
            typeof(IAvroSchemaRegistryConfiguration),
            typeof(IBasicMetricsConfiguration)
        };
        
        foreach (var interfaceType in interfaceTypes)
        {
            services.AddSingleton(interfaceType, provider =>
            {
                var ksqlOptions = provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value;
                return OptionsConverter.GetConfiguration<object>(ksqlOptions, interfaceType);
            });
        }
        
        // ValidationMode登録
        services.AddSingleton<ValidationMode>(provider =>
            provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value.ValidationMode);
        
        return services;
    }
}
```

---

## 📋 **設定ファイル例**

### **appsettings.json（本番用）**
```json
{
  "KsqlDsl": {
    "Kafka": {
      "BootstrapServers": "prod-kafka:9092",
      "ClientId": "trading-system",
      "DefaultGroupId": "trading-consumers",
      "DefaultAutoOffsetReset": "Latest",
      "RequestTimeoutMs": 30000,
      "EnableAutoCommit": true
    },
    "SchemaRegistry": {
      "Url": "https://schema-registry.prod:8081",
      "MaxCachedSchemas": 2000,
      "BasicAuthUsername": "prod-user",
      "BasicAuthPassword": "prod-password",
      "AutoRegisterSchemas": true
    },
    "Metrics": {
      "EnableMetrics": true,
      "CollectionInterval": "00:00:30",
      "MaxHistorySize": 1000
    },
    "ValidationMode": "Strict"
  }
}
```

### **appsettings.Development.json（開発用）**
```json
{
  "KsqlDsl": {
    "Kafka": {
      "BootstrapServers": "localhost:9092",
      "ClientId": "dev-app",
      "DefaultGroupId": "dev-consumers",
      "DefaultAutoOffsetReset": "Earliest"
    },
    "SchemaRegistry": {
      "Url": "http://localhost:8081"
    },
    "Metrics": {
      "EnableMetrics": false
    },
    "ValidationMode": "Relaxed"
  }
}
```

---

## 🚀 **ユーザー使用例**

### **Program.cs（1行で完了）**
```csharp
var builder = WebApplication.CreateBuilder(args);

// ✅ 1行でKsqlDsl全設定完了
builder.Services.AddKsqlDsl(
    builder.Configuration.GetSection("KsqlDsl"));

var app = builder.Build();
```

### **KafkaContext実装（設定不要）**
```csharp
public class TradingKafkaContext : KafkaContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Event<TradeEvent>();
        modelBuilder.Event<OrderEvent>();
        // 設定は自動適用、OnConfiguring不要
    }
}
```

---

## ✅ **設計の利点**

### **1. ユーザーエクスペリエンス**
- ✅ **設定の簡潔性**: 機能ごとに整理、重複なし
- ✅ **実装の簡単さ**: 1行登録、設定コード不要
- ✅ **保守の容易さ**: 設定変更は1箇所のみ

### **2. 内部実装**
- ✅ **自動分散**: 統合設定から各namespace設定に自動変換
- ✅ **型安全性**: Interface実装による保証
- ✅ **拡張性**: 新namespace追加が容易

### **3. エラーハンドリング**
- ✅ **統一処理**: 1つの例外クラスで全対応
- ✅ **明確なメッセージ**: 具体的な対処法を提示
- ✅ **環境対応**: 設定ファイル分離でデバッグ対応

### **4. 保守性**
- ✅ **最大限の簡素化**: 38ファイル → 8ファイル（79%削減）
- ✅ **責務の明確化**: 純粋な「窓口」機能に特化
- ✅ **依存関係の最適化**: 各namespaceへの適切な分散

---

## 🎯 **今後の拡張方法**

### **新namespace追加時の手順**
1. **Interface定義**: 新namespace層でInterface定義
2. **Section作成**: Configuration層でInterface実装Section作成
3. **KsqlDslOptions拡張**: 新Sectionプロパティ追加
4. **DI登録**: ServiceCollectionExtensionsの配列に1行追加

### **新設定項目追加時**
1. **Interface拡張**: 対象namespaceでInterface拡張
2. **Section拡張**: 対応SectionクラスでProperty追加
3. **appsettings.json**: 設定例の更新

この設計により、KsqlDsl Configuration層は「**各namespaceからの要求に応える窓口**」として、最高のユーザーエクスペリエンスと保守性を両立した実装が完成した。