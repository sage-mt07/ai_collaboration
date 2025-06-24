
# 鳴瀬への指示書: Monitoring 名前空間の撤去および統合について

## 🎯 指示概要

現在の `Monitoring/` 名前空間は、設計責務の範囲外もしくは冗長であり、以下の方針で整理・撤去を行うこと。

---

## ✅ 実施項目一覧

| No | 内容 | 対応指示 |
|----|------|----------|
| 1 | `Monitoring/Abstractions` を全廃止 | 使用箇所がなければ完全削除。使用中なら統一IFに統合 |
| 2 | `ISerializationMetricsProvider` を `Serialization/Metrics` に作成 | `AvroSerializerCacheHealthCheck` 相当機能を移管 |
| 3 | `AvroMetricsRecorder` 等の実装を `Serialization/Metrics` に集約 | 分割されすぎたクラスは統合または削除 |
| 4 | `ILogger` で代替できる情報はログ出力で処理 | メトリクス形式では持たない |
| 5 | `Query`/`EventSet` 系メトリクスは全削除 | アプリ停止で検知されるべきエラーであり運用観測ではないため |
| 6 | `docs/metrics_policy.md` に準拠し、無断追加は不可 | 天城または司令の承認が必要 |

---

## 🧪 テスト・移行上の注意点

- `InternalsVisibleTo` に依存するテストがある場合、`Serialization` 側へ移行すること
- `IHealthMonitor` 系のIFや実装が不要となるため、併せて撤去
- `Monitoring/` 関連のユニットテストプロジェクトが存在する場合は、使用状況を精査して対応

---

## 📎 新規インターフェース例

```csharp
namespace KsqlDsl.Serialization.Metrics;

public interface ISerializationMetricsProvider
{
    double CacheHitRate { get; }
    long TotalDeserializationCount { get; }

    void RecordDeserialization();
    void RecordCacheHit();
    void Reset();
}
```

---

## 📁 ファイル名案

- `ISerializationMetricsProvider.cs`
- `AvroSerializationMetrics.cs`
- `AvroSerializationMetricsTests.cs`

---

以上をもって、Monitoring 名前空間の整理を段階的に実施すること。
