# 最適化されたConfiguration設計

## 🎯 **設計の最適化ポイント**

### **1. Convertersの共通化**
- 各namespace用の個別Converterは不要
- 汎用的なConverter1つで全対応

### **2. SectionsのInterface実装**
- KafkaSection → IKafkaBusConfiguration実装
- SchemaRegistrySection → IAvroSchemaRegistryConfiguration実装
- MetricsSection → IBasicMetricsConfiguration実装

---

## 📁 **最適化されたファイル構成**

```
src/Configuration/
├── KsqlDslOptions.cs              - 統合設定メインクラス
├── ValidationMode.cs              - 共通enum
├── Sections/
│   ├── KafkaSection.cs           - IKafkaBusConfiguration実装
│   ├── SchemaRegistrySection.cs  - IAvroSchemaRegistryConfiguration実装
│   └── MetricsSection.cs         - IBasicMetricsConfiguration実装
├── Common/
│   └── OptionsConverter.cs       - 汎用変換器
└── Extensions/
    └── ServiceCollectionExtensions.cs - DI登録拡張
```

---

## 🔧 **実装詳細**

### **1. 汎用Options変換器**
```csharp
// src/Configuration/Common/OptionsConverter.cs
using System.Reflection;

namespace KsqlDsl.Configuration.Common;

/// <summary>
/// 汎用的なOptions変換器
/// リフレクションベースで任意のSection → Interface変換を実現
/// </summary>
internal static class OptionsConverter
{
    /// <summary>
    /// KsqlDslOptionsから指定されたInterface実装を取得
    /// </summary>
    /// <typeparam name="TInterface">取得したいInterface型</typeparam>
    /// <param name="ksqlOptions">統合設定</param>
    /// <returns>Interface実装インスタンス</returns>
    public static TInterface GetConfiguration<TInterface>(KsqlDslOptions ksqlOptions)
        where TInterface : class
    {
        var interfaceType = typeof(TInterface);
        
        // KsqlDslOptionsのプロパティから対応するSection実装を検索
        var properties = typeof(KsqlDslOptions).GetProperties();
        
        foreach (var property in properties)
        {
            var sectionValue = property.GetValue(ksqlOptions);
            if (sectionValue != null && interfaceType.IsAssignableFrom(sectionValue.GetType()))
            {
                return (TInterface)sectionValue;
            }
        }
        
        throw new InvalidOperationException(
            $"No section implementing {interfaceType.Name} found in KsqlDslOptions");
    }
    
    /// <summary>
    /// 複数のInterface実装を一括取得
    /// </summary>
    public static T GetConfiguration<T>(KsqlDslOptions ksqlOptions, Type interfaceType)
        where T : class
    {
        var method = typeof(OptionsConverter)
            .GetMethod(nameof(GetConfiguration), new[] { typeof(KsqlDslOptions) })!
            .MakeGenericMethod(interfaceType);
            
        return (T)method.Invoke(null, new object[] { ksqlOptions })!;
    }
}
```

### **2. Interface実装Sections**

#### **KafkaSection**
```csharp
// src/Configuration/Sections/KafkaSection.cs
using Confluent.Kafka;
using KsqlDsl.Messaging.Abstractions;

namespace KsqlDsl.Configuration.Sections;

/// <summary>
/// Kafka設定セクション
/// IKafkaBusConfigurationを実装
/// </summary>
public record KafkaSection : IKafkaBusConfiguration
{
    /// <inheritdoc />
    public string BootstrapServers { get; init; } = "localhost:9092";
    
    /// <inheritdoc />
    public string ClientId { get; init; } = "ksql-dsl-client";
    
    /// <inheritdoc />
    public string DefaultGroupId { get; init; } = "ksql-dsl-consumer";
    
    /// <inheritdoc />
    public AutoOffsetReset DefaultAutoOffsetReset { get; init; } = AutoOffsetReset.Latest;
    
    /// <inheritdoc />
    public int RequestTimeoutMs { get; init; } = 30000;
    
    /// <inheritdoc />
    public int MaxMessages { get; init; } = 1000;
    
    /// <inheritdoc />
    public TimeSpan ConsumerTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <inheritdoc />
    public bool EnableAutoCommit { get; init; } = true;
    
    /// <inheritdoc />
    public bool EnableAutoTopicCreation { get; init; } = false;
    
    /// <inheritdoc />
    public ProducerConfig GetProducerConfig() => new()
    {
        BootstrapServers = BootstrapServers,
        ClientId = ClientId
    };
    
    /// <inheritdoc />
    public ConsumerConfig GetConsumerConfig(string groupId) => new()
    {
        BootstrapServers = BootstrapServers,
        ClientId = ClientId,
        GroupId = groupId
    };
}
```

#### **SchemaRegistrySection**
```csharp
// src/Configuration/Sections/SchemaRegistrySection.cs
using KsqlDsl.Serialization.Abstractions;

namespace KsqlDsl.Configuration.Sections;

/// <summary>
/// Schema Registry設定セクション
/// IAvroSchemaRegistryConfigurationを実装
/// </summary>
public record SchemaRegistrySection : IAvroSchemaRegistryConfiguration
{
    /// <inheritdoc />
    public string Url { get; init; } = "http://localhost:8081";
    
    /// <inheritdoc />
    public int MaxCachedSchemas { get; init; } = 1000;
    
    /// <inheritdoc />
    public TimeSpan CacheExpirationTime { get; init; } = TimeSpan.FromHours(1);
    
    /// <inheritdoc />
    public string? BasicAuthUsername { get; init; }
    
    /// <inheritdoc />
    public string? BasicAuthPassword { get; init; }
    
    /// <inheritdoc />
    public int RequestTimeoutMs { get; init; } = 30000;
    
    /// <inheritdoc />
    public bool AutoRegisterSchemas { get; init; } = true;
    
    /// <inheritdoc />
    public SubjectNameStrategy SubjectNameStrategy { get; init; } = SubjectNameStrategy.Topic;
    
    /// <inheritdoc />
    public bool UseLatestVersion { get; init; } = false;
}
```

#### **MetricsSection**
```csharp
// src/Configuration/Sections/MetricsSection.cs
using KsqlDsl.Monitoring.Abstractions;

namespace KsqlDsl.Configuration.Sections;

/// <summary>
/// メトリクス設定セクション
/// IBasicMetricsConfigurationを実装
/// </summary>
public record MetricsSection : IBasicMetricsConfiguration
{
    /// <inheritdoc />
    public bool EnableMetrics { get; init; } = true;
    
    /// <inheritdoc />
    public TimeSpan CollectionInterval { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <inheritdoc />
    public int MaxHistorySize { get; init; } = 1000;
}
```

### **3. 統合設定クラス**
```csharp
// src/Configuration/KsqlDslOptions.cs
using KsqlDsl.Configuration.Sections;

namespace KsqlDsl.Configuration;

/// <summary>
/// KsqlDsl統合設定
/// 各Sectionが対応するInterfaceを実装
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

### **4. 簡素化されたDI登録**
```csharp
// src/Configuration/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using KsqlDsl.Configuration.Common;
using KsqlDsl.Messaging.Abstractions;
using KsqlDsl.Serialization.Abstractions;
using KsqlDsl.Monitoring.Abstractions;

namespace KsqlDsl.Configuration.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsqlDsl(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        // メイン設定の登録
        services.Configure<KsqlDslOptions>(configurationSection);
        
        // ✅ 汎用Converterで各Interface実装を自動登録
        services.AddSingleton<IKafkaBusConfiguration>(provider =>
        {
            var ksqlOptions = provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value;
            return OptionsConverter.GetConfiguration<IKafkaBusConfiguration>(ksqlOptions);
        });
        
        services.AddSingleton<IAvroSchemaRegistryConfiguration>(provider =>
        {
            var ksqlOptions = provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value;
            return OptionsConverter.GetConfiguration<IAvroSchemaRegistryConfiguration>(ksqlOptions);
        });
        
        services.AddSingleton<IBasicMetricsConfiguration>(provider =>
        {
            var ksqlOptions = provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value;
            return OptionsConverter.GetConfiguration<IBasicMetricsConfiguration>(ksqlOptions);
        });
        
        // ValidationModeの直接登録
        services.AddSingleton<ValidationMode>(provider =>
        {
            var ksqlOptions = provider.GetRequiredService<IOptions<KsqlDslOptions>>().Value;
            return ksqlOptions.ValidationMode;
        });
        
        return services;
    }
}
```

---

## 🚀 **さらなる最適化：汎用DI登録**

### **完全自動化されたDI登録**
```csharp
public static IServiceCollection AddKsqlDsl(
    this IServiceCollection services,
    IConfigurationSection configurationSection)
{
    services.Configure<KsqlDslOptions>(configurationSection);
    
    // ✅ Interface型の配列で一括登録
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
```

---

## 📊 **最適化効果**

### **Before: 個別Converter**
```
MessagingOptionsConverter.cs      - 50行
SerializationOptionsConverter.cs  - 50行
MonitoringOptionsConverter.cs     - 50行
= 合計150行
```

### **After: 汎用Converter**
```
OptionsConverter.cs               - 30行
= 合計30行（80%削減）
```

### **新namespace追加時**

#### **Before**
1. 新SectionクラスとInterface実装
2. 専用Converter作成（50行）
3. ServiceCollectionExtensions修正

#### **After**
1. 新SectionクラスとInterface実装（のみ）
2. ✅ **Converter不要**
3. ServiceCollectionExtensionsの配列に1行追加

---

## ✅ **最終的なnamespace構成**

```
KsqlDsl.Configuration
├── KsqlDslOptions                 - 統合設定
├── ValidationMode                 - 共通enum
└── Sections/
    ├── KafkaSection              - IKafkaBusConfiguration実装
    ├── SchemaRegistrySection     - IAvroSchemaRegistryConfiguration実装
    └── MetricsSection            - IBasicMetricsConfiguration実装

KsqlDsl.Configuration.Common
└── OptionsConverter              - 汎用変換器（1つで全対応）

KsqlDsl.Configuration.Extensions
└── ServiceCollectionExtensions   - 自動DI登録
```

### **特徴**
- ✅ **Converter共通化**: 1つで全namespace対応
- ✅ **Interface実装**: Section直接実装で型安全
- ✅ **自動登録**: リフレクションベース一括処理
- ✅ **拡張容易**: 新namespace追加が簡単

**この最適化された設計で進めてもよろしいでしょうか？**