# Monitoring インターフェース仕様

## 概要

KsqlDsl.Monitoring の各インターフェースの詳細仕様と使用方法を説明します。

## コアインターフェース

### IHealthMonitor

ヘルス監視機能の統一インターフェースです。

```csharp
public interface IHealthMonitor
{
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
    string ComponentName { get; }
    HealthLevel Level { get; }
    event EventHandler<HealthStateChangedEventArgs>? HealthStateChanged;
}
```

#### 実装例

```csharp
public class AvroHealthChecker : IHealthMonitor
{
    public string ComponentName => "Avro Serializer Cache";
    public HealthLevel Level => HealthLevel.Critical;

    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // ヘルスチェック実装
        var stats = _cache.GetExtendedStatistics();
        // ... 分析ロジック
        
        return new HealthCheckResult
        {
            Status = HealthStatus.Healthy,
            Description = "All systems operational",
            Duration = stopwatch.Elapsed
        };
    }
}
```

#### HealthCheckResult 構造

| プロパティ | 型 | 説明 |
|-----------|----|----|
| Status | HealthStatus | Healthy/Degraded/Unhealthy/Unknown |
| Description | string | 状態の説明 |
| Duration | TimeSpan | チェック実行時間 |
| Exception | Exception? | エラー時の例外情報 |
| Data | object? | 詳細データ |
| CheckedAt | DateTime | チェック実行時刻 |

### IMetricsCollector\<T>

型安全なメトリクス収集の統一インターフェースです。

```csharp
public interface IMetricsCollector<T>
{
    void RecordCounter(string name, long value, IDictionary<string, object?>? tags = null);
    void RecordGauge(string name, double value, IDictionary<string, object?>? tags = null);
    void RecordHistogram(string name, double value, IDictionary<string, object?>? tags = null);
    void RecordTimer(string name, TimeSpan duration, IDictionary<string, object?>? tags = null);
    MetricsSnapshot GetSnapshot();
    string TargetTypeName { get; }
    DateTime StartTime { get; }
}
```

#### 使用例

```csharp
var metricsCollector = new AvroMetricsCollector();

// カウンターメトリクス
metricsCollector.RecordCounter("cache_hits", 1, new Dictionary<string, object?>
{
    ["entity_type"] = "UserEntity",
    ["operation"] = "serialize"
});

// ヒストグラムメトリクス
metricsCollector.RecordHistogram("response_time", responseTime.TotalMilliseconds);

// タイマーメトリクス
metricsCollector.RecordTimer("processing_duration", processingTime);

// スナップショット取得
var snapshot = metricsCollector.GetSnapshot();
```

#### MetricsSnapshot 構造

```csharp
public class MetricsSnapshot
{
    public string TargetTypeName { get; set; }
    public DateTime SnapshotTime { get; set; }
    public Dictionary<string, CounterMetric> Counters { get; set; }
    public Dictionary<string, GaugeMetric> Gauges { get; set; }
    public Dictionary<string, HistogramMetric> Histograms { get; set; }
    public Dictionary<string, TimerMetric> Timers { get; set; }
}
```

### IDiagnosticsProvider

診断情報提供の統一インターフェースです。

```csharp
public interface IDiagnosticsProvider
{
    Task<DiagnosticsInfo> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    string ComponentName { get; }
    DiagnosticsCategory Category { get; }
    DiagnosticsPriority Priority { get; }
}
```

#### 実装例

```csharp
public class DiagnosticContext : IDiagnosticsProvider
{
    public string ComponentName => "Integrated Diagnostics";
    public DiagnosticsCategory Category => DiagnosticsCategory.General;
    public DiagnosticsPriority Priority => DiagnosticsPriority.High;

    public async Task<DiagnosticsInfo> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var diagnostics = new DiagnosticsInfo();
        
        // システム情報収集
        await CollectSystemInfoAsync(diagnostics, cancellationToken);
        
        // パフォーマンス情報収集
        await CollectPerformanceDiagnosticsAsync(diagnostics, cancellationToken);
        
        return diagnostics;
    }
}
```

## 専用実装クラス

### AvroHealthChecker

Avroキャッシュ専用のヘルス監視実装です。

```csharp
public class AvroHealthChecker : IHealthMonitor
```

#### 設定オプション (AvroHealthCheckOptions)

| プロパティ | デフォルト値 | 説明 |
|-----------|-------------|-----|
| WarningHitRateThreshold | 0.70 | 警告レベルのヒット率閾値 |
| CriticalHitRateThreshold | 0.50 | 危険レベルのヒット率閾値 |
| WarningSlowOperationRateThreshold | 0.05 | 警告レベルのスロー操作率閾値 |
| CriticalSlowOperationRateThreshold | 0.15 | 危険レベルのスロー操作率閾値 |
| MinimumRequestsForEvaluation | 100 | 評価に必要な最小リクエスト数 |

#### ヘルス判定ロジック

1. **リクエスト数チェック**: 最小評価リクエスト数未満の場合は健全と判定
2. **ヒット率評価**: Critical → Unhealthy, Warning → Degraded
3. **スロー操作率評価**: 高い場合は Unhealthy または Degraded
4. **総合判定**: 最も深刻な状態を全体ステータスとする

### AvroMetricsCollector

Avro専用のメトリクス収集実装です。

```csharp
public class AvroMetricsCollector : IMetricsCollector<object>
```

#### 既存互換メソッド

```csharp
// Avro固有のメトリクス記録
void RecordCacheHit(string entityType, string serializerType);
void RecordCacheMiss(string entityType, string serializerType);
void RecordSerializationDuration(string entityType, string serializerType, TimeSpan duration);
void RecordSchemaRegistration(string subject, bool success, TimeSpan duration);

// グローバルメトリクス更新
void UpdateGlobalMetrics(CacheStatistics stats);

// Observable Metrics 登録 (.NET Metrics統合)
void RegisterObservableMetrics(AvroSerializerCache cache);
```

#### .NET Metrics 統合

```csharp
private static readonly Meter _meter = new("KsqlDsl.Monitoring.Avro", "1.0.0");
private static readonly Counter<long> _cacheHitCounter = _meter.CreateCounter<long>("avro_cache_hits_total");
private static readonly Histogram<double> _serializationDuration = _meter.CreateHistogram<double>("avro_serialization_duration_ms");
```

## エラーハンドリング

### HealthCheckResult でのエラー処理

```csharp
try
{
    var stats = _cache.GetExtendedStatistics();
    // 分析処理...
    
    return new HealthCheckResult
    {
        Status = HealthStatus.Healthy,
        Description = "All systems operational"
    };
}
catch (Exception ex)
{
    return new HealthCheckResult
    {
        Status = HealthStatus.Unhealthy,
        Description = "Health check failed due to internal error",
        Exception = ex
    };
}
```

### DiagnosticsInfo でのエラー処理

```csharp
public class DiagnosticsInfo
{
    public List<DiagnosticsError> Errors { get; set; } = new();
    public List<DiagnosticsWarning> Warnings { get; set; } = new();
}

// エラー追加例
diagnostics.Errors.Add(new DiagnosticsError
{
    Message = "Failed to collect system information",
    Exception = ex,
    Code = "SYSTEM_INFO_FAILED"
});
```

## イベント通知

### HealthStateChanged イベント

```csharp
public event EventHandler<HealthStateChangedEventArgs>? HealthStateChanged;

// イベント引数
public class HealthStateChangedEventArgs : EventArgs
{
    public HealthStatus PreviousStatus { get; set; }
    public HealthStatus CurrentStatus { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}
```

#### 使用例

```csharp
var healthChecker = new AvroHealthChecker(...);
healthChecker.HealthStateChanged += (sender, args) =>
{
    logger.LogInformation(
        "Health status changed: {Component} {Previous} -> {Current} ({Reason})",
        args.ComponentName, args.PreviousStatus, args.CurrentStatus, args.Reason);
};
```

## DI コンテナ統合

### サービス登録例

```csharp
services.AddSingleton<PerformanceMonitoringAvroCache>();
services.AddSingleton<IMetricsCollector<object>, AvroMetricsCollector>();
services.AddSingleton<IHealthMonitor, AvroHealthChecker>();
services.AddSingleton<IDiagnosticsProvider, DiagnosticContext>();

services.Configure<AvroHealthCheckOptions>(options =>
{
    options.WarningHitRateThreshold = 0.8;
    options.CriticalHitRateThreshold = 0.6;
});
```