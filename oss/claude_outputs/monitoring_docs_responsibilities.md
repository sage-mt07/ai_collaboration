# Monitoring 機能の責務分離

## 概要

KsqlDsl の監視機能（Phase 1）では、Avro系とKafka通信層に分散していた監視機能を独立構造に抽出し、責務の分離・モジュール統合性の向上・テスト性の向上を実現しています。

## 責務分離の設計方針

### 1. インターフェース主導設計
- `IHealthMonitor`: ヘルス監視の統一インターフェース
- `IMetricsCollector<T>`: 型安全なメトリクス収集
- `IDiagnosticsProvider`: 診断情報提供の統一化

### 2. 名前空間による責務明確化
```
KsqlDsl.Monitoring.Abstractions  - 共通インターフェース
KsqlDsl.Monitoring.Health        - ヘルス監視実装
KsqlDsl.Monitoring.Metrics       - メトリクス収集実装
KsqlDsl.Monitoring.Diagnostics   - 診断情報提供実装
```

## 各コンポーネントの責務

### ヘルス監視 (Health)

#### `IHealthMonitor`
- **責務**: ヘルスチェック機能の統一インターフェース
- **目的**: 監視対象の健全性評価、状態変更通知
- **設計理由**: プラグイン化による拡張性、統一された監視API

#### `AvroHealthChecker`
- **責務**: Avroキャッシュの健全性監視
- **移行元**: `AvroSerializerCacheHealthCheck.cs`
- **改善点**:
  - `IHealthMonitor` 実装による標準化
  - イベント通知による状態変更の追跡
  - 非同期処理の最適化

### メトリクス収集 (Metrics)

#### `IMetricsCollector<T>`
- **責務**: 型安全なメトリクス収集の統一インターフェース
- **目的**: カウンター、ゲージ、ヒストグラム、タイマーの統一管理
- **設計理由**: .NET Metrics との統合、型安全性の確保

#### `AvroMetricsCollector`
- **責務**: Avroシリアライザー向けメトリクス収集
- **移行元**: `AvroMetrics.cs`
- **改善点**:
  - `IMetricsCollector<object>` 実装による標準化
  - 内部メトリクス管理とスナップショット機能
  - .NET Diagnostics.Metrics との統合

### 診断情報 (Diagnostics)

#### `IDiagnosticsProvider`
- **責務**: 診断情報提供の統一インターフェース
- **目的**: トラブルシューティング支援、設定検証
- **設計理由**: 各コンポーネントの診断情報統合

#### `DiagnosticContext`
- **責務**: 診断情報の統合コンテキスト
- **機能**: システム情報、パフォーマンス情報、警告・エラーの統合
- **設計理由**: 一元的な診断情報管理、運用支援

## 既存コードからの移行

### 移行対象ファイル

1. **`AvroSerializerCacheHealthCheck.cs` → `Health/AvroHealthChecker.cs`**
   - 約380行の責務を明確化
   - `IHealthMonitor` 実装による統一化
   - イベント駆動型の状態管理

2. **`AvroMetrics.cs` → `Metrics/AvroMetricsCollector.cs`**
   - 静的メソッドからインスタンスベースへ
   - `IMetricsCollector<T>` 実装による型安全性
   - スナップショット機能による統計取得

### 参照の更新

```csharp
// 移行前
using KsqlDsl.Avro;
var healthCheck = new AvroSerializerCacheHealthCheck(...);

// 移行後
using KsqlDsl.Monitoring.Health;
var healthCheck = new AvroHealthChecker(...);
```

## テスト性の向上

### 1. インターフェース分離による Mockability
- `IHealthMonitor` のモック化が容易
- `IMetricsCollector<T>` の単体テスト支援
- `IDiagnosticsProvider` の独立テスト

### 2. 専用テストプロジェクト
- `tests/Monitoring/` 配下でのテスト分離
- `AvroHealthCheckerTests.cs`: 健全性判定ロジックの検証
- `AvroMetricsCollectorTests.cs`: メトリクス収集の正確性検証

### 3. InternalsVisibleTo の調整
```csharp
[assembly: InternalsVisibleTo("KsqlDsl.Monitoring.Tests")]
```

## 統合性向上

### 1. 統一されたイベント機能
- `HealthStateChangedEventArgs` による状態変更通知
- 監視対象の一元的な状態管理

### 2. メトリクス標準化
- .NET Diagnostics.Metrics との統合
- OpenTelemetry 対応の基盤整備

### 3. 診断情報の構造化
- `DiagnosticsInfo` による統一フォーマット
- カテゴリ、優先度による分類管理

## Phase 2 以降の拡張計画

### Phase 2: Kafka通信系監視の統合
- Producer/Consumer プールの監視統合
- ネットワーク層の健全性監視

### Phase 3: 設定・検証の統合
- 設定妥当性の監視
- パフォーマンス閾値の動的調整

## まとめ

Phase 1 の監視機能分離により以下を実現：

1. **責務の明確化**: 各監視機能の責務を明確に分離
2. **拡張性向上**: インターフェース主導による新機能追加の容易性
3. **テスト性向上**: モック化・単体テストの実施しやすさ
4. **統合性向上**: 統一されたAPI・イベント・診断による運用性向上

この設計により、構造負荷を軽減しつつ、高品質な監視基盤を構築しています。