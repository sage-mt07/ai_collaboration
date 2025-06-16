### 8.4 .NET 8 依存性注入・設定統合
```csharp
// Program.cs または Startup.cs での設定
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAvroSerialization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // .NET 8 標準ログ設定
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddJsonConsole(); // 構造化ログ出力
        });

        // .NET 8 メトリクス設定
        services.AddMetrics()
            .AddMeter("KsqlDsl.Avro"); // AvroMetrics用

        // .NET 8 分散トレーシング設定
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder.AddSource("KsqlDsl.Avro"); // AvroActivitySource用
                builder.AddAspNetCoreInstrumentation();
                builder.AddHttpClientInstrumentation();
            });

        // 設定オプション
        services.Configure<AvroOperationRetrySettings>(
            configuration.GetSection("AvroSerialization:Retry"));
        services.Configure<PerformanceThresholds>(
            configuration.GetSection("AvroSerialization:Performance"));

        // Avroサービス登録
        services.AddSingleton<AvroSerializerCache>();
        services.AddSingleton<ResilientAvroSerializerManager>();
        services.AddSingleton<PerformanceMonitoringAvroCache>();

        return services;
    }
}

// appsettings.json 設定例
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "KsqlDsl.Avro": "Debug"
    },
    "Console": {
      "LogToStandardErrorThreshold": "Warning"
    },
    "JsonConsole": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-ddTHH:mm:ss.fffZ"
    }
  },
  "AvroSerialization": {
    "Retry": {
      "SchemaRegistration": {
        "MaxAttempts": 5,
        "InitialDelayMs": 200,
        "MaxDelayMs": 60000,
        "BackoffMultiplier": 2.0
      },
      "SchemaRetrieval": {
        "MaxAttempts": 3,
        "InitialDelayMs": 100,
        "MaxDelayMs": 10000,
        "BackoffMultiplier": 2.0
      }
    },
    "Performance": {
      "SlowSerializerCreationMs": 100,
      "MinimumHitRate": 0.7
    }
  }
}
```

### 8.5 アラート・通知設定（.NET 8 準拠）
```csharp
public class AvroHealthChecks
{
    public static IServiceCollection AddAvroHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddCheck<AvroSerializerCacheHealthCheck>("avro-cache")
            .AddCheck<SchemaRegistryHealthCheck>("schema-registry")
            .AddCheck<AvroCompatibilityHealthCheck>("avro-compatibility");

        return services;
    }
}

public class AvroSerializerCacheHealthCheck : IHealthCheck
{
    private readonly AvroSerializerCache _cache;
    private readonly IOptions<PerformanceThresholds> _thresholds;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stats = _cache.GetGlobalStatistics();
        var data = new Dictionary<string, object>
        {
            ["hit_rate"] = stats.HitRate,
            ["total_requests"] = stats.TotalRequests,
            ["cached_items"] = stats.CachedItemCount
        };

        if (stats.HitRate < _thresholds.Value.MinimumHitRate)
        {
            return HealthCheckResult.Degraded(
                $"Cache hit rate is {stats.HitRate:P1}, below threshold {_thresholds.Value.MinimumHitRate:P1}",
                data: data);
        }

        return HealthCheckResult.Healthy("Avro cache is performing well", data);
    }
}

public class SchemaRegistryHealthCheck : IHealthCheck
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly ILogger<SchemaRegistryHealthCheck> _logger;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var activity = AvroActivitySource.StartCacheOperation("health_check", "schema_registry");
            var stopwatch = Stopwatch.StartNew();
            
            // Schema Registryへの軽量なヘルスチェック
            var subjects = await _schemaRegistryClient.GetAllSubjectsAsync();
            stopwatch.Stop();
            
            var data = new Dictionary<string, object>
            {
                ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                ["subject_count"] = subjects.Count
            };

            if (stopwatch.ElapsedMilliseconds > 5000) // 5秒以上
            {
                return HealthCheckResult.Degraded(
                    $"Schema Registry response time is slow: {stopwatch.ElapsedMilliseconds}ms",
                    data: data);
            }

            return HealthCheckResult.Healthy("Schema Registry is accessible", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema Registry health check failed");
            return HealthCheckResult.Unhealthy("Schema Registry is not accessible", ex);
        }
    }
}
```

### 8.6 .NET 8 アプリケーション設定統合
```csharp
// IHostedService を使用したバックグラウンド監視
public class AvroPerformanceMonitoringService : BackgroundService
{
    private readonly AvroSerializerCache _cache;
    private readonly ILogger<AvroPerformanceMonitoringService> _logger;
    private readonly IOptions<MonitoringOptions> _options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorPerformanceAsync();
                await Task.Delay(_options.Value.MonitoringIntervalMs, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during Avro performance monitoring");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // エラー時は1分待機
            }
        }
    }

    private async Task MonitorPerformanceAsync()
    {
        var healthReport = _cache.GetHealthReport();
        
        // .NET 8 メトリクス更新
        AvroMetrics.UpdateGlobalMetrics(_cache.GetGlobalStatistics());
        
        if (healthReport.HealthLevel == CacheHealthLevel.Critical)
        {
            _logger.LogWarning("Avro cache performance is critical. Issues: {Issues}",
                string.Join(", ", healthReport.Issues.Select(i => i.Description)));
        }
        
        // 推奨アクションの実行
        foreach (var recommendation in healthReport.Recommendations)
        {
            _logger.LogInformation("Avro performance recommendation: {Recommendation}", recommendation);
        }
    }
}

// Program.cs での完全な設定例
var builder = WebApplication.CreateBuilder(args);

// .NET 8 標準サービス設定
builder.Services.AddAvroSerialization(builder.Configuration);
builder.Services.AddAvroHealthChecks(builder.Configuration);
builder.Services.AddHostedService<AvroPerformanceMonitoringService>();

// KsqlDsl設定
builder.Services.AddKsqlDsl(options =>
{
    options.UseKafka(builder.Configuration.GetConnectionString("Kafka")!)
           .UseSchemaRegistry(builder.Configuration.GetConnectionString("SchemaRegistry")!)
           .EnableAvroSchemaAutoRegistration(forceRegistration: true)
           .EnableDebugLogging();
});

var app = builder.Build();

// .NET 8 ヘルスチェックエンドポイント
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

// .NET 8 メトリクスエンドポイント（Prometheus形式）
app.MapPrometheusScrapingEndpoint(); // OpenTelemetry.Exporter.Prometheus.AspNetCore

app.Run();
```

### 8.7 .NET 8 構成ファイル完全版
```json
{
  "ConnectionStrings": {
    "Kafka": "localhost:9092",
    "SchemaRegistry": "http://localhost:8081"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "KsqlDsl": "Debug",
      "KsqlDsl.Avro": "Trace"
    },
    "Console": {
      "LogToStandardErrorThreshold": "Warning",
      "Format": "Simple"
    },
    "JsonConsole": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-ddTHH:mm:ss.fffZ",
      "JsonWriterOptions": {
        "Indented": false
      }
    },
    "EventLog": {
      "LogLevel": {
        "KsqlDsl.Avro": "Error"
      }
    }
  },
  "OpenTelemetry": {
    "ServiceName": "KsqlDsl-AvroService",
    "ServiceVersion": "1.0.0",
    "Tracing": {
      "AspNetCore": {
        "RecordException": true
      },
      "HttpClient": {
        "RecordException": true
      }
    },
    "Metrics": {
      "ConsoleExporter": {
        "Enabled": false
      },
      "PrometheusExporter": {
        "Enabled": true,
        "ScrapeEndpointPath": "/metrics"
      }
    }
  },
  "AvroSerialization": {
    "Retry": {
      "SchemaRegistration": {
        "MaxAttempts": 5,
        "InitialDelayMs": 200,
        "MaxDelayMs": 60000,
        "BackoffMultiplier": 2.0,
        "RetryableExceptions": [
          "System.Net.Http.HttpRequestException",
          "System.TimeoutException",
          "System.Threading.Tasks.TaskCanceledException"
        ],
        "NonRetryableExceptions": [
          "KsqlDsl.SchemaRegistry.SchemaCompatibilityException",
          "System.ArgumentException",
          "System.InvalidOperationException"
        ]
      },
      "SchemaRetrieval": {
        "MaxAttempts": 3,
        "InitialDelayMs": 100,
        "MaxDelayMs": 10000,
        "BackoffMultiplier": 2.0
      },
      "CompatibilityCheck": {
        "MaxAttempts": 2,
        "InitialDelayMs": 50,
        "MaxDelayMs": 5000,
        "BackoffMultiplier": 2.0
      }
    },
    "Performance": {
      "SlowSerializerCreationMs": 100,
      "MinimumHitRate": 0.7,
      "MaxCacheSize": 10000,
      "CacheExpiryHours": 24
    },
    "Monitoring": {
      "MonitoringIntervalMs": 30000,
      "HealthCheckIntervalMs": 10000,
      "EnableDetailedMetrics": true,
      "EnablePerformanceCounters": true
    }
  },
  "HealthChecks": {
    "UI": {
      "EvaluationTimeInSeconds": 30,
      "MinimumSecondsBetweenFailureNotifications": 300
    }
  }
}
```

---

この .NET 8 標準準拠の設計により、モダンな .NET アプリケーションとしてのベストプラクティスに従った、保守性・運用性の高いAvro統合が実現できます。# Avro × Kafka × 複合PK 設計ドキュメント

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

### 3.4 ヒット率計測・監視API

#### 3.4.1 キャッシュ統計情報
```csharp
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0.0;
    public int CachedItemCount { get; set; }
    public DateTime LastAccess { get; set; }
    public DateTime? LastClear { get; set; }
    public TimeSpan Uptime { get; set; }
}

public class EntityCacheStatus
{
    public Type EntityType { get; set; }
    public long KeySerializerHits { get; set; }
    public long KeySerializerMisses { get; set; }
    public long ValueSerializerHits { get; set; }
    public long ValueSerializerMisses { get; set; }
    public long KeyDeserializerHits { get; set; }
    public long KeyDeserializerMisses { get; set; }
    public long ValueDeserializerHits { get; set; }
    public long ValueDeserializerMisses { get; set; }
    
    public double KeySerializerHitRate => GetHitRate(KeySerializerHits, KeySerializerMisses);
    public double ValueSerializerHitRate => GetHitRate(ValueSerializerHits, ValueSerializerMisses);
    public double KeyDeserializerHitRate => GetHitRate(KeyDeserializerHits, KeyDeserializerMisses);
    public double ValueDeserializerHitRate => GetHitRate(ValueDeserializerHits, ValueDeserializerMisses);
    public double OverallHitRate => GetHitRate(AllHits, AllMisses);
    
    private long AllHits => KeySerializerHits + ValueSerializerHits + KeyDeserializerHits + ValueDeserializerHits;
    private long AllMisses => KeySerializerMisses + ValueSerializerMisses + KeyDeserializerMisses + ValueDeserializerMisses;
}
```

#### 3.4.2 スキーマ情報管理
```csharp
public class AvroSchemaInfo
{
    public Type EntityType { get; set; }
    public SerializerType Type { get; set; } // Key or Value
    public int SchemaId { get; set; }
    public string Subject { get; set; } // orders-key, orders-value
    public DateTime RegisteredAt { get; set; }
    public DateTime LastUsed { get; set; }
    public long UsageCount { get; set; }
    public string SchemaJson { get; set; } // 実際のAvroスキーマ
    public int Version { get; set; } // Schema Registry version
}

public class AvroSerializerCache
{
    // 監視API
    public CacheStatistics GetGlobalStatistics();
    public EntityCacheStatus GetEntityCacheStatus<T>();
    public Dictionary<Type, EntityCacheStatus> GetAllEntityStatuses();
    
    // スキーマ一覧取得API
    public List<AvroSchemaInfo> GetRegisteredSchemas();
    public List<AvroSchemaInfo> GetRegisteredSchemas<T>();
    public AvroSchemaInfo? GetSchemaInfo<T>(SerializerType type);
    public List<AvroSchemaInfo> GetSchemasBySubject(string subject);
    
    // 管理API
    public void ClearCache();
    public void ClearCache<T>();
    public void ClearExpiredSchemas(TimeSpan maxAge);
    public bool RemoveSchema<T>(SerializerType type);
    
    // 健康状態チェック
    public CacheHealthReport GetHealthReport();
}
```

#### 3.4.3 健康状態レポート
```csharp
public class CacheHealthReport
{
    public DateTime GeneratedAt { get; set; }
    public CacheStatistics GlobalStats { get; set; }
    public List<EntityCacheStatus> EntityStats { get; set; }
    public List<CacheIssue> Issues { get; set; }
    public CacheHealthLevel HealthLevel { get; set; }
    
    // 推奨アクション
    public List<string> Recommendations { get; set; }
}

public class CacheIssue
{
    public CacheIssueType Type { get; set; }
    public string Description { get; set; }
    public CacheIssueSeverity Severity { get; set; }
    public Type? AffectedEntityType { get; set; }
    public string? Recommendation { get; set; }
}

public enum CacheIssueType
{
    LowHitRate,      // ヒット率50%未満
    ExcessiveMisses, // 1時間で100回以上ミス
    StaleSchemas,    // 24時間以上未使用
    MemoryPressure,  // キャッシュサイズ過大
    SchemaVersionMismatch // スキーマバージョン不整合
}

public enum CacheHealthLevel
{
    Healthy,    // ヒット率90%以上
    Warning,    // ヒット率70%以上
    Critical    // ヒット率70%未満
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

## 8. 運用時のログ出力・再試行方針

### 8.1 ログ出力設計

#### 8.1.1 ログレベル定義
```csharp
public enum AvroLogLevel
{
    Trace,    // キャッシュヒット/ミス詳細
    Debug,    // スキーマ生成・登録詳細
    Info,     // 正常な操作完了
    Warning,  // 互換性警告・性能問題
    Error,    // シリアライゼーション失敗
    Critical  // Schema Registry接続断・致命的エラー
}
```

#### 8.1.2 .NET 8標準ログ統合
```csharp
// Microsoft.Extensions.Logging 使用
public class AvroSerializerCache
{
    private readonly ILogger<AvroSerializerCache> _logger;
    
    public AvroSerializerCache(ILogger<AvroSerializerCache> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}

// 構造化ログ出力（LoggerMessage Source Generator 使用）
public static partial class AvroLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Avro cache hit: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms, HitRate: {HitRate:P1})")]
    public static partial void CacheHit(
        ILogger logger, 
        string entityType, 
        string serializerType, 
        int schemaId, 
        long durationMs, 
        double hitRate);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Avro cache miss: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms, HitRate: {HitRate:P1})")]
    public static partial void CacheMiss(
        ILogger logger, 
        string entityType, 
        string serializerType, 
        int schemaId, 
        long durationMs, 
        double hitRate);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Schema registration succeeded: {Subject} → SchemaId {SchemaId} (Attempt: {Attempt}, Duration: {DurationMs}ms)")]
    public static partial void SchemaRegistrationSucceeded(
        ILogger logger, 
        string subject, 
        int schemaId, 
        int attempt, 
        long durationMs);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Schema registration retry: {Subject} (Attempt: {Attempt}/{MaxAttempts}, Delay: {DelayMs}ms)")]
    public static partial void SchemaRegistrationRetry(
        ILogger logger, 
        string subject, 
        int attempt, 
        int maxAttempts, 
        long delayMs, 
        Exception exception);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Schema registration failed permanently: {Subject} (Attempts: {Attempts})")]
    public static partial void SchemaRegistrationFailed(
        ILogger logger, 
        string subject, 
        int attempts, 
        Exception exception);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Critical,
        Message = "Schema Registry connection failed, switching to offline mode")]
    public static partial void SchemaRegistryOfflineMode(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Low cache hit rate detected: {EntityType} (HitRate: {HitRate:P1} < Threshold: {ThresholdRate:P1})")]
    public static partial void LowCacheHitRate(
        ILogger logger, 
        string entityType, 
        double hitRate, 
        double thresholdRate);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Slow serializer creation: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms > Threshold: {ThresholdMs}ms)")]
    public static partial void SlowSerializerCreation(
        ILogger logger, 
        string entityType, 
        string serializerType, 
        int schemaId, 
        long durationMs, 
        long thresholdMs);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Schema compatibility check failed: {Subject} - {Reason}")]
    public static partial void SchemaCompatibilityFailed(
        ILogger logger, 
        string subject, 
        string reason, 
        Exception? exception = null);
}
```

#### 8.1.3 .NET 8 メトリクス統合
```csharp
// System.Diagnostics.Metrics 使用
public class AvroMetrics
{
    private static readonly Meter _meter = new("KsqlDsl.Avro", "1.0.0");
    
    // カウンター
    private static readonly Counter<long> _cacheHitCounter = 
        _meter.CreateCounter<long>("avro_cache_hits_total", description: "Total cache hits");
    private static readonly Counter<long> _cacheMissCounter = 
        _meter.CreateCounter<long>("avro_cache_misses_total", description: "Total cache misses");
    private static readonly Counter<long> _schemaRegistrationCounter = 
        _meter.CreateCounter<long>("avro_schema_registrations_total", description: "Total schema registrations");
    
    // ヒストグラム
    private static readonly Histogram<double> _serializationDuration = 
        _meter.CreateHistogram<double>("avro_serialization_duration_ms", "ms", "Serialization duration");
    private static readonly Histogram<double> _schemaRegistrationDuration = 
        _meter.CreateHistogram<double>("avro_schema_registration_duration_ms", "ms", "Schema registration duration");
    
    // ゲージ（ObservableGauge）
    private static readonly ObservableGauge<int> _cacheSize = 
        _meter.CreateObservableGauge<int>("avro_cache_size", description: "Current cache size");
    private static readonly ObservableGauge<double> _cacheHitRate = 
        _meter.CreateObservableGauge<double>("avro_cache_hit_rate", description: "Cache hit rate");

    public static void RecordCacheHit(string entityType, string serializerType)
    {
        _cacheHitCounter.Add(1, 
            new KeyValuePair<string, object?>("entity_type", entityType),
            new KeyValuePair<string, object?>("serializer_type", serializerType));
    }

    public static void RecordCacheMiss(string entityType, string serializerType)
    {
        _cacheMissCounter.Add(1,
            new KeyValuePair<string, object?>("entity_type", entityType),
            new KeyValuePair<string, object?>("serializer_type", serializerType));
    }

    public static void RecordSerializationDuration(string entityType, string serializerType, TimeSpan duration)
    {
        _serializationDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("entity_type", entityType),
            new KeyValuePair<string, object?>("serializer_type", serializerType));
    }

    public static void RecordSchemaRegistration(string subject, bool success, TimeSpan duration)
    {
        _schemaRegistrationCounter.Add(1,
            new KeyValuePair<string, object?>("subject", subject),
            new KeyValuePair<string, object?>("success", success));
            
        _schemaRegistrationDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("subject", subject),
            new KeyValuePair<string, object?>("success", success));
    }
    
    // ゲージ値の提供（定期的に呼び出される）
    public static void RegisterObservableMetrics(AvroSerializerCache cache)
    {
        _meter.CreateObservableGauge<int>("avro_cache_size", () => cache.GetCachedItemCount());
        _meter.CreateObservableGauge<double>("avro_cache_hit_rate", () => cache.GetGlobalStatistics().HitRate);
    }
}
```

#### 8.1.4 .NET 8 Activity (分散トレーシング) 統合
```csharp
public class AvroActivitySource
{
    private static readonly ActivitySource _activitySource = new("KsqlDsl.Avro", "1.0.0");
    
    public static Activity? StartSchemaRegistration(string subject)
    {
        return _activitySource.StartActivity("avro.schema.register")
            ?.SetTag("subject", subject)
            ?.SetTag("component", "schema-registry");
    }
    
    public static Activity? StartSerialization(string entityType, string serializerType)
    {
        return _activitySource.StartActivity("avro.serialize")
            ?.SetTag("entity.type", entityType)
            ?.SetTag("serializer.type", serializerType)
            ?.SetTag("component", "avro-serializer");
    }
    
    public static Activity? StartCacheOperation(string operation, string entityType)
    {
        return _activitySource.StartActivity($"avro.cache.{operation}")
            ?.SetTag("entity.type", entityType)
            ?.SetTag("component", "avro-cache");
    }
}

// 使用例
public async Task<int> RegisterSchemaWithRetryAsync(string subject, string schema)
{
    using var activity = AvroActivitySource.StartSchemaRegistration(subject);
    
    try
    {
        var schemaId = await _schemaRegistryClient.RegisterSchemaAsync(subject, schema);
        activity?.SetTag("schema.id", schemaId)?.SetStatus(ActivityStatusCode.Ok);
        return schemaId;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

### 8.2 再試行方針

#### 8.2.1 再試行戦略
```csharp
public class AvroRetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public double BackoffMultiplier { get; set; } = 2.0;
    public List<Type> RetryableExceptions { get; set; } = new()
    {
        typeof(HttpRequestException),
        typeof(TimeoutException),
        typeof(TaskCanceledException)
    };
    public List<Type> NonRetryableExceptions { get; set; } = new()
    {
        typeof(SchemaCompatibilityException),
        typeof(ArgumentException),
        typeof(InvalidOperationException)
    };
}
```

#### 8.2.2 操作別再試行設定
```csharp
public class AvroOperationRetrySettings
{
    // Schema Registry操作
    public AvroRetryPolicy SchemaRegistration { get; set; } = new()
    {
        MaxAttempts = 5,
        InitialDelay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(60)
    };
    
    public AvroRetryPolicy SchemaRetrieval { get; set; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromMilliseconds(100),
        MaxDelay = TimeSpan.FromSeconds(10)
    };
    
    public AvroRetryPolicy CompatibilityCheck { get; set; } = new()
    {
        MaxAttempts = 2,
        InitialDelay = TimeSpan.FromMilliseconds(50),
        MaxDelay = TimeSpan.FromSeconds(5)
    };
    
    // シリアライゼーション操作（基本的に再試行しない）
    public AvroRetryPolicy Serialization { get; set; } = new()
    {
        MaxAttempts = 1 // 再試行なし
    };
}
```

#### 8.2.3 回復力のある実装
```csharp
public class ResilientAvroSerializerManager
{
    private readonly AvroOperationRetrySettings _retrySettings;
    private readonly ILogger<ResilientAvroSerializerManager> _logger;
    
    public async Task<int> RegisterSchemaWithRetryAsync(string subject, string schema)
    {
        using var activity = AvroActivitySource.StartSchemaRegistration(subject);
        var policy = _retrySettings.SchemaRegistration;
        var attempt = 1;
        
        while (attempt <= policy.MaxAttempts)
        {
            try
            {
                using var operation = AvroActivitySource.StartCacheOperation("register", subject);
                var stopwatch = Stopwatch.StartNew();
                
                var schemaId = await _schemaRegistryClient.RegisterSchemaAsync(subject, schema);
                stopwatch.Stop();
                
                // .NET 8 標準ログ + メトリクス
                AvroLogMessages.SchemaRegistrationSucceeded(_logger, subject, schemaId, attempt, stopwatch.ElapsedMilliseconds);
                AvroMetrics.RecordSchemaRegistration(subject, success: true, stopwatch.Elapsed);
                
                activity?.SetTag("schema.id", schemaId)?.SetStatus(ActivityStatusCode.Ok);
                return schemaId;
            }
            catch (Exception ex) when (ShouldRetry(ex, policy, attempt))
            {
                var delay = CalculateDelay(policy, attempt);
                
                // .NET 8 標準ログ
                AvroLogMessages.SchemaRegistrationRetry(_logger, subject, attempt, policy.MaxAttempts, (long)delay.TotalMilliseconds, ex);
                
                await Task.Delay(delay);
                attempt++;
            }
            catch (Exception ex)
            {
                // .NET 8 標準ログ + メトリクス
                AvroLogMessages.SchemaRegistrationFailed(_logger, subject, attempt, ex);
                AvroMetrics.RecordSchemaRegistration(subject, success: false, TimeSpan.Zero);
                
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
        
        throw new InvalidOperationException($"Schema registration failed after {policy.MaxAttempts} attempts: {subject}");
    }
    
    private bool ShouldRetry(Exception ex, AvroRetryPolicy policy, int attempt)
    {
        if (attempt >= policy.MaxAttempts) return false;
        if (policy.NonRetryableExceptions.Any(type => type.IsInstanceOfType(ex))) return false;
        return policy.RetryableExceptions.Any(type => type.IsInstanceOfType(ex));
    }
    
    private TimeSpan CalculateDelay(AvroRetryPolicy policy, int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(
            policy.InitialDelay.TotalMilliseconds * Math.Pow(policy.BackoffMultiplier, attempt - 1));
        return delay > policy.MaxDelay ? policy.MaxDelay : delay;
    }
}
```

### 8.3 障害対応・フォールバック戦略

#### 8.3.1 Schema Registry接続断対応
```csharp
public class OfflineAvroSerializerManager
{
    private readonly IMemoryCache _schemaCache;
    private readonly IAvroSchemaStorage _persistentStorage; // ローカルファイル/DB
    private readonly ILogger<OfflineAvroSerializerManager> _logger;
    
    public async Task<ISerializer<object>> CreateSerializerAsync<T>(SerializerType type)
    {
        try
        {
            // 通常のSchema Registry操作
            return await _onlineManager.CreateSerializerAsync<T>(type);
        }
        catch (SchemaRegistryUnavailableException ex)
        {
            // .NET 8 標準ログ
            AvroLogMessages.SchemaRegistryOfflineMode(_logger, ex);
            
            // フォールバック: キャッシュまたは永続化ストレージから復元
            var cachedSchema = await GetCachedSchemaAsync<T>(type);
            if (cachedSchema != null)
            {
                return CreateSerializerFromCachedSchema<T>(cachedSchema);
            }
            
            // 最終手段: 動的スキーマ生成（警告付き）
            _logger.LogWarning("No cached schema found, generating schema dynamically for {EntityType}:{SerializerType} (compatibility not guaranteed)",
                typeof(T).Name, type);
            
            return CreateSerializerFromGeneratedSchema<T>(type);
        }
    }
}
```

#### 8.3.2 パフォーマンス劣化対応
```csharp
public class PerformanceMonitoringAvroCache : AvroSerializerCache
{
    private readonly PerformanceThresholds _thresholds;
    private readonly ILogger<PerformanceMonitoringAvroCache> _logger;
    
    public override ISerializer<object> GetOrCreateSerializer<T>(SerializerType type, int schemaId, Func<ISerializer<object>> factory)
    {
        using var activity = AvroActivitySource.StartCacheOperation("get_or_create", typeof(T).Name);
        var stopwatch = Stopwatch.StartNew();
        
        var result = base.GetOrCreateSerializer<T>(type, schemaId, factory);
        stopwatch.Stop();
        
        // .NET 8 メトリクス記録
        AvroMetrics.RecordSerializationDuration(typeof(T).Name, type.ToString(), stopwatch.Elapsed);
        
        // パフォーマンス監視
        if (stopwatch.ElapsedMilliseconds > _thresholds.SlowSerializerCreationMs)
        {
            AvroLogMessages.SlowSerializerCreation(_logger, typeof(T).Name, type.ToString(), schemaId, 
                stopwatch.ElapsedMilliseconds, _thresholds.SlowSerializerCreationMs);
        }
        
        // ヒット率監視
        var hitRate = GetEntityCacheStatus<T>().OverallHitRate;
        if (hitRate < _thresholds.MinimumHitRate)
        {
            AvroLogMessages.LowCacheHitRate(_logger, typeof(T).Name, hitRate, _thresholds.MinimumHitRate);
                
            // 必要に応じてキャッシュ戦略調整
            ConsiderCacheOptimization<T>();
        }
        
        return result;
    }
    
    private void ConsiderCacheOptimization<T>()
    {
        // キャッシュサイズ拡張、TTL調整、プリロード等の最適化検討
        var entityStats = GetEntityCacheStatus<T>();
        
        if (entityStats.AllMisses > 100 && entityStats.OverallHitRate < 0.5)
        {
            _logger.LogInformation("Considering cache preload for {EntityType} due to frequent misses",
                typeof(T).Name);
            
            // 非同期でよく使われるスキーマをプリロード
            _ = Task.Run(() => PreloadFrequentlyUsedSchemasAsync<T>());
        }
    }
}
```

### 8.4 アラート・通知設定
```csharp
public class AvroAlertingService
{
    public void ConfigureAlerts()
    {
        // キャッシュヒット率低下アラート
        AlertOn(metrics => metrics.CacheHitRate < 0.7)
            .WithSeverity(AlertSeverity.Warning)
            .WithMessage("Avro cache hit rate below 70%")
            .WithRunbook("https://wiki.company.com/avro-cache-troubleshooting");
            
        // Schema Registry接続断アラート
        AlertOn(metrics => metrics.SchemaRegistryConnectionFailures > 0)
            .WithSeverity(AlertSeverity.Critical)
            .WithMessage("Schema Registry connection failures detected")
            .WithRunbook("https://wiki.company.com/schema-registry-outage");
            
        // スキーマ互換性エラーアラート
        AlertOn(metrics => metrics.SchemaCompatibilityErrors > 0)
            .WithSeverity(AlertSeverity.Error)
            .WithMessage("Schema compatibility errors detected")
            .WithRunbook("https://wiki.company.com/schema-compatibility-issues");
    }
}
```

---

この運用設計により、本番環境での安定稼働と迅速な問題解決が実現できます。