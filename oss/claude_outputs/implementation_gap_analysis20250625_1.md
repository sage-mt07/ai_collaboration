# 実装漏れ分析レポート【最終更新版】

## 概要
最新の設計資料を含めて再評価し、指定された7つの項目について実装漏れを精査しました。特に要約版の記載により、実装の軽重が明確になりました。

---

## 3.1 トピック (Kafka Topics)

### ✅ 実装済み項目
- **属性によるマッピング**: `TopicAttribute`クラスで実装済み
- **基本的なトピック構成**: パーティション設定、レプリケーション設定
- **Fluent API制限**: `AvroEntityConfigurationBuilder`で適切に制限実装

### ❌ 実装漏れ項目
1. **Fluent APIによるトピック設定（重要度：中）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Order>()
       .WithPartitions(12)
       .WithReplicationFactor(3);
   ```

2. **パーティショニング戦略設定（重要度：低）**
   - 設計書では「パーティショニング戦略」とあるが、具体的実装が不明確

3. **ISRの最小数設定（重要度：低）**
   - 「ISRの最小数」設定機能が未実装

---

## 3.2 ストリーム (KSQL Streams)

### ✅ 実装済み項目
- **ストリーム判定ルール**: `StreamTableAnalyzer`で実装
- **基本的なLINQ式解釈**: Where、Select等の基本機能
- **WithManualCommit()基本構造**: `EntityModelBuilder`に実装済み

### ❌ 実装漏れ項目
1. **Window DSL機能（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Order>()
       .Window(TumblingWindow.Of(TimeSpan.FromHours(1)))
       .GroupBy(o => o.CustomerId);
   ```

2. **購読モードの固定化制御（重要度：高）**
   - 設計書：「実行時に切り替え不可とする」機能の完全実装が不足
   - `EntityModel.UseManualCommit`は実装済みだが、実行時制御が不完全

---

## 3.3 テーブル (KSQL Tables)

### ✅ 実装済み項目
- **テーブル判定ルール**: GroupBy、Aggregate検出機能
- **基本的な集約操作**: Sum、Count、Max、Min
- **自動compact設定の基本構造**: `TopicAttribute.Compaction`プロパティで実装

### ❌ 実装漏れ項目
1. **LATEST_BY_OFFSET/EARLIEST_BY_OFFSET集約関数（重要度：高）**
   ```csharp
   // 設計書で重要視されているが未実装
   .Select(g => new
   {
       CustomerId = g.Key,
       LatestAmount = g.LatestByOffset(o => o.Amount) // ← 未実装
   });
   ```

2. **複数ウィンドウ定義とアクセス（重要度：高）**
   ```csharp
   // 設計書記載だが未実装
   modelBuilder.Entity<Chart>().Window(new int[]{1,5,15,60});
   var candles1m = ctx.Charts.Window(1).ToList();
   ```

3. **HasTopic()メソッド（重要度：中）**
   ```csharp
   // 設計書のサンプルに記載だが未実装
   modelBuilder.Entity<Order>()
       .HasTopic("orders") // ← 未実装
       .GroupBy(o => o.CustomerId);
   ```

4. **WindowStart/WindowEndプロパティ（重要度：中）**
   ```csharp
   // 設計書記載だが未実装
   WindowStart = g.WindowStart,
   WindowEnd = g.WindowEnd
   ```

---

## 3.4 クエリと購読

### ✅ 実装済み項目
- **基本的なForEachAsync**: `EventSet`で実装
- **IManualCommitMessage<T>インターフェース**: 定義済み

### ❌ 実装漏れ項目
1. **手動コミット購読処理の型分岐（重要度：高）**
   ```csharp
   // 設計書：「ForEachAsync() の中で分岐」だが実装不完全
   // WithManualCommit()指定時：IManualCommitMessage<T>を返す
   // 自動コミット時：Tのままを返す
   ```

2. **購読処理の完全実装（重要度：高）**
   - `.CommitAsync()`と`.NegativeAckAsync()`の具体的実装が不足
   - yield型の ForEachAsync での try-catch 処理サポート

---

## 4.1 POCOの基本定義

### ✅ 実装済み項目
- **基本属性**: `KeyAttribute`, `TopicAttribute`, `KafkaIgnoreAttribute`等
- **属性ベースDSL**: 基本機能は実装済み
- **AvroTimestamp属性**: `AvroTimestampAttribute`で実装済み

### ❌ 実装漏れ項目
1. **特に大きな実装漏れなし**
   - 設計書の要約により、基本的な属性は実装済みと確認

---

## 4.2 特殊型のサポート

### ✅ 実装済み項目
- **基本データ型**: int, long, string, DateTime等
- **Decimal精度指定**: `DecimalPrecisionAttribute`
- **DateTime処理**: 基本的なUTC変換サポート
- **AvroTimestamp(IsEventTime=true)**: `AvroTimestampAttribute`で実装

### ❌ 実装漏れ項目
1. **char型の非推奨化処理（重要度：低）**
   - 設計書：「事実上非推奨」だが、警告機能が未実装

2. **short型の自動int変換処理（重要度：低）**
   - 設計書：「shortはintとして扱う」の明示的処理が不明確

---

## 6.1 エラー処理戦略

### ✅ 実装済み項目
- **基本的なエラー処理**: `ErrorHandlingPolicy`, `ErrorAction`
- **リトライ機能**: `ResilientAvroSerializerManager`で実装

### ❌ 実装漏れ項目
1. **チェーン可能なエラー処理DSL（重要度：高）**
   ```csharp
   // 設計書で重要視されているが未実装
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

## 🆕 新発見：要約版による重要項目の明確化

### 高優先度として明確化された項目
1. **OnError(...), .WithRetry(...), .Map(...)などのDSL**
   - 要約版で「提供予定」として明記されており、重要度が高い

2. **yield型のForEachAsyncでのtry-catch処理サポート**
   - 要約版で明確に言及されており、購読処理の核心機能

3. **POD内タイマーによるWindow確定の自律実行**
   - 要約版で言及されている高度な機能

### 実装済みとして確認された項目
1. **[AvroTimestamp]属性**
   - 要約版で明記されており、`AvroTimestampAttribute`で実装済み

2. **自動compact設定**
   - 基本的な仕組みは`TopicAttribute.Compaction`で実装済み

---

## 優先度付き実装推奨事項【更新版】

### 🔴 最高優先度（差別化要因）
1. **チェーン可能なエラー処理DSL (.OnError, .Map, .WithRetry)**
2. **LATEST_BY_OFFSET / EARLIEST_BY_OFFSET 集約関数**
3. **手動コミット購読処理の型分岐機能**
4. **yield型ForEachAsyncでのtry-catch処理**

### 🟠 高優先度（重要機能）
1. **複数ウィンドウ定義とアクセス機能**
2. **Window DSL機能（TumblingWindow等）**
3. **購読モードの固定化制御**

### 🟡 中優先度（拡張機能）
1. **HasTopic()メソッド**
2. **WindowStart/WindowEndプロパティ**
3. **Fluent APIによるトピック設定**
4. **DLQ設定のModelBuilder対応**

### 🟢 低優先度（品質向上）
1. **型安全性の強化（char/short型の適切な処理）**
2. **パーティショニング戦略設定**
3. **ISRの最小数設定**

---

## 総評
要約版の記載により、**エラー処理DSL**と**LATEST_BY_OFFSET系集約関数**が最重要機能として明確化されました。特に`.OnError()`, `.Map()`, `.WithRetry()`のチェーン可能なDSLは、このフレームワークの差別化要因となる重要機能です。

また、yield型ForEachAsyncでのtry-catch処理サポートは、Kafkaストリーム処理における実用性に直結する核心機能として重要度が高いことが判明しました。