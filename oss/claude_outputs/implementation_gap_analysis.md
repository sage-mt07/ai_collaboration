# 実装漏れ分析レポート【更新版】

## 概要
追加資料を含めて添付設計資料とソースコードを再比較し、指定された7つの項目について実装漏れを調査しました。

---

## 3.1 トピック (Kafka Topics)

### ✅ 実装済み項目
- **属性によるマッピング**: `TopicAttribute`クラスで実装済み
- **トピック構成**: パーティション設定、レプリケーション設定、保持ポリシー、圧縮設定
- **Fluent API制限**: `AvroEntityConfigurationBuilder`で適切に制限実装

### ❌ 実装漏れ項目
1. **トピック操作API（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   await context.EnsureDeclaredAsync<Order>();
   await context.UndeclareAsync<Order>();
   ```

2. **スキーマ進化機能（重要度：中）**
   - スキーマバージョン管理は`AvroSchemaVersionManager`で基本実装があるが、完全ではない
   - マイグレーション機能が不完全

3. **WithPartitions/WithReplicationFactorのFluent API**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Order>()
       .WithPartitions(12)
       .WithReplicationFactor(3);
   ```

---

## 3.2 ストリーム (KSQL Streams)

### ✅ 実装済み項目
- **ストリーム判定ルール**: `StreamTableAnalyzer`で実装
- **基本的なLINQ式解釈**: Where、Select等の基本機能

### ❌ 実装漏れ項目
1. **WithManualCommit()の完全実装（重要度：高）**
   ```csharp
   // EntityModelBuilderに基本構造はあるが、実行時処理が不完全
   modelBuilder.Entity<Order>()
       .Where(o => o.Amount > 1000)
       .WithManualCommit(); // ← EntityModel保存まで、実行時制御が不足
   ```

2. **Window DSL機能（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Order>()
       .Window(TumblingWindow.Of(TimeSpan.FromHours(1)))
       .GroupBy(o => o.CustomerId);
   ```

3. **AsStream()/AsTable()の明示的指定**
   - `EntityModelBuilder`に基本実装はあるが、実行時の動作制御が不完全

---

## 3.3 テーブル (KSQL Tables)

### ✅ 実装済み項目
- **テーブル判定ルール**: GroupBy、Aggregate検出機能
- **基本的な集約操作**: Sum、Count、Max、Min
- **結合処理の基本構造**: `JoinBuilder`等で基本実装

### ❌ 実装漏れ項目
1. **LATEST_BY_OFFSET/EARLIEST_BY_OFFSET集約関数（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   var latestOrders = context.Orders
       .GroupBy(o => o.CustomerId)
       .Select(g => new {
           CustomerId = g.Key,
           LatestAmount = g.LatestByOffset(o => o.Amount) // ← 未実装
       });
   ```

2. **複数ウィンドウ定義とアクセス（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Chart>().Window(new int[]{1,5,15,60});
   var candles1m = ctx.Charts.Window(1).ToList();
   var candles5m = ctx.Charts.Window(5).ToList();
   ```

3. **WindowHeartbeat属性（重要度：中）**
   ```csharp
   // 設計書記載だが未実装
   [WindowHeartbeat("chart_1min_heartbeat")]
   ```

4. **自動compact設定**
   - 設計書：「GroupBy含む場合は自動的にcompactモードで作成」
   - 実装で明確な自動compact設定機能が確認できない

---

## 3.4 クエリと購読

### ✅ 実装済み項目
- **基本的なForEachAsync**: `EventSet`で実装
- **IManualCommitMessage<T>インターフェース**: 定義済み

### ❌ 実装漏れ項目
1. **手動コミット購読処理の完全実装（重要度：高）**
   ```csharp
   // IManualCommitMessage<T>は定義済みだが、ForEachAsyncでの分岐処理が不完全
   await foreach (var order in context.HighValueOrders.ForEachAsync())
   {
       // orderがIManualCommitMessage<T>として返される処理が不完全
       await order.CommitAsync();      // ← 実装不完全
       await order.NegativeAckAsync(); // ← 実装不完全
   }
   ```

2. **購読モードの固定化制御**
   - 設計書：「実行時に切り替え不可」だが、この制御機能が不明確

---

## 4.1 POCOの基本定義

### ✅ 実装済み項目
- **基本属性**: `KeyAttribute`, `TopicAttribute`, `KafkaIgnoreAttribute`等
- **属性ベースDSL**: 基本機能は実装済み
- **Fluent API制限**: 設計通りに制限実装

### ❌ 実装漏れ項目
1. **WindowHeartbeat属性（重要度：中）**
   ```csharp
   // 設計書記載だが属性クラス自体が未実装
   [WindowHeartbeat("heartbeat-topic")]
   ```

2. **MaxLength属性のAvroスキーマ反映**
   - `MaxLengthAttribute`は実装済みだが、Avroスキーマ生成時の反映処理が不明確

---

## 4.2 特殊型のサポート

### ✅ 実装済み項目
- **基本データ型**: int, long, string, DateTime等
- **Decimal精度指定**: `DecimalPrecisionAttribute`
- **DateTime処理**: 基本的なUTC変換サポート

### ❌ 実装漏れ項目
1. **char型の非推奨化処理（重要度：低）**
   - 設計書：「char型は事実上非推奨」だが、検証・警告機能が未実装

2. **short型の自動int変換処理（重要度：低）**
   - 設計書：「shortはintとして扱う」だが、明示的な変換処理が不明確

3. **DateTimeOffset推奨の強制機能（重要度：中）**
   - 設計書でDateTimeOffset推奨だが、DateTime使用時の自動変換処理が部分実装

---

## 6.1 エラー処理戦略

### ✅ 実装済み項目
- **基本的なエラー処理**: `ErrorHandlingPolicy`, `ErrorAction`
- **リトライ機能**: `ResilientAvroSerializerManager`で実装

### ❌ 実装漏れ項目
1. **チェーン可能なエラー処理（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   var processedOrders = context.Orders
       .OnError(ErrorAction.Skip)  // ← 未実装
       .Map(order => ProcessOrder(order))  // ← 未実装
       .WithRetry(3);  // ← 未実装
   ```

2. **デシリアライゼーションエラーポリシー（重要度：中）**
   ```csharp
   // 設計書記載だが未実装
   context.Options.DeserializationErrorPolicy = ErrorPolicy.Skip;
   ```

3. **ModelBuilderでのDLQ設定（重要度：中）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Order>().WithDeadLetterQueue();
   ```

---

## 🆕 新発見：設計書追加機能の実装漏れ

### 基本原則への新項目
**購読モードの固定化（重要度：高）**
- 設計書：「ストリーム定義時に自動コミット／手動コミットの方式を明示し、実行時に切り替え不可とする」
- 実装：この制御機能が不完全

### KsqlContextBuilderの未実装
```csharp
// 設計書記載だが未実装
var context = CsharpKsqlContextBuilder.Create()
    .UseSchemaRegistry("http://localhost:8081")
    .EnableLogging(loggerFactory)
    .ConfigureValidation(autoRegister: true, failOnErrors: false, enablePreWarming: true)
    .WithTimeouts(TimeSpan.FromSeconds(5))
    .EnableDebugMode(true)
    .Build()
    .BuildContext<MyKsqlContext>();
```

### 初期化タイミングの明示化
```csharp
// 設計書記載だが未実装
await _context.EnsureKafkaReadyAsync();
```

---

## 優先度付き実装推奨事項

### 🔴 最高優先度（コア機能）
1. **手動コミット購読処理の完全実装**
2. **LATEST_BY_OFFSET / EARLIEST_BY_OFFSET 集約関数**
3. **複数ウィンドウ定義とアクセス機能**
4. **チェーン可能なエラー処理**

### 🟠 高優先度（重要機能）
1. **WithManualCommit()の実行時制御**
2. **トピック操作API (EnsureDeclaredAsync/UndeclareAsync)**
3. **Window DSL機能**
4. **KsqlContextBuilderの実装**

### 🟡 中優先度（拡張機能）
1. **WindowHeartbeat属性**
2. **自動compact設定**
3. **初期化タイミングの明示化**
4. **DLQ設定のModelBuilder対応**

### 🟢 低優先度（品質向上）
1. **型安全性の強化（char/short型の適切な処理）**
2. **MaxLength属性のAvroスキーマ反映**
3. **DateTimeOffset推奨の強制機能**

---

## 総評
再評価の結果、**手動コミット関連機能**と**高度な集約関数**が最重要の実装漏れです。特に設計書で強調されている「購読モードの固定化」「複数ウィンドウ」「チェーン可能エラー処理」は、このフレームワークの差別化要因となる重要機能です。

また、KsqlContextBuilderの未実装は、フレームワークの使いやすさに直結する重要な漏れとして新たに発見されました。