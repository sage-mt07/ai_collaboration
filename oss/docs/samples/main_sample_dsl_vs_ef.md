# Kafka DSL & Entity Framework 比較ガイド（Main Sample）

このドキュメントは、Entity Framework（RDB）と Kafka DSL（ストリーム）を**同じ構文で記述できることの価値**を明示的に示すためのメインサンプルです。

---

## ⚙️ 構成要素の概要

| 項目     | Entity Framework (RDB)  | Kafka DSL (ksqlDB)               |
| ------ | ----------------------- | -------------------------------- |
| データ定義  | POCO クラス＋Fluent API／属性  | POCO クラス＋属性                      |
| 永続化先   | `.mdf`（SQL Serverなど）    | Kafkaトピック＋ksqlDBストリーム／テーブル       |
| コンテキスト | `DbContext`             | `KafkaDbContext`（継承クラス）          |
| 接続先管理  | 接続文字列（ConnectionString） | BootstrapServers / REST Endpoint |
| 開発ツール  | SSMS / EF Power Tools 等 | Confluent CLI / ksqlDB CLI       |

これらの構成は VS 上でもほぼ同等に扱うことができ、学習コストを抑えながらストリーム処理にスムーズに移行可能です。

---

## 🧾 コード比較サンプル

以下に、RDBとKafka/ksqlDBに同じLINQクエリを投げるコードを並列表記します：

```csharp
// 📦 RDB用 Entity Framework
var results = dbContext.Orders.Where(o => o.Price > 1000).ToList();
```

```csharp
// 🔄 Kafka/ksqlDB用 DSL
var streamResults = kafkaContext.Orders.Where(o => o.Price > 1000).ToList();
```

> ✅ 同じ構文で記述可能でありながら、裏側で異なる実行基盤（SQL Server vs ksqlDB）に向けた処理が生成されます。

---

## 🔍 違いと適用上の注意

| 観点       | Entity Framework (RDB) | Kafka DSL (ksqlDB)   |
| -------- | ---------------------- | -------------------- |
| トランザクション | ACID完全対応               | 非対応（ストリーム処理中心）       |
| リアルタイム性  | バッチ処理前提                | EMIT CHANGES による即時反映 |
| 遅延／可用性   | ストレージI/O次第             | 高スループット／高可用性         |
| クエリ制限    | 複雑な結合・集約可能             | JOIN条件・GROUP BY制限あり  |
| データ保持    | 永続データ                  | 保存期間＝レコード保持期間        |

---

## 🧠 適材適所の設計観点

- **Entity Framework：** 主に OLTP 操作や更新系処理に向く。マスタ情報や確定データの保持に。
- **Kafka DSL：** 高頻度イベント・ログ処理、リアルタイム分析、通知系処理に最適。

---
## 🧩よくあるクエリ構文の比較サンプル

🔗 Join
```csharp
// RDB: 商品と注文の結合
var query = from order in dbContext.Orders
            join product in dbContext.Products
            on order.ProductId equals product.Id
            select new { order.OrderId, product.Name };

// Kafka DSL: ストリーム同士のJOIN
var query = from order in kafkaContext.Orders
            join product in kafkaContext.Products
            on order.ProductId equals product.Id
            select new { order.OrderId, product.Name };
```
📊 GroupBy
```csharp
// RDB: ユーザーごとの合計金額
var grouped = dbContext.Orders
    .GroupBy(o => o.UserId)
    .Select(g => new { g.Key, Total = g.Sum(o => o.Price) });

// Kafka DSL: ユーザーごとの集計
var grouped = kafkaContext.Orders
    .GroupBy(o => o.UserId)
    .Select(g => new { g.Key, Total = g.Sum(o => o.Price) });
```
⏱️ Window（時間集約）
```csharp
// Kafka DSL: 5分単位の売上集計
var windowed = kafkaContext.Orders
    .GroupBy(o => o.Symbol)
    .Window(TimeSpan.FromMinutes(5))
    .Select(w => new { w.Key, Avg = w.Avg(o => o.Price) });
```
⛔ Entity Framework における時間ウィンドウの概念は通常存在しません（代替は時間条件＋GROUP BY）。



## 🔗 サンプルコードへの導線

- `docs/sample/README_sample.md`（AddAsync / ForEachAsync）
- 今後追加予定：Join / GroupBy / Window 処理サンプル

---

この「構文の共通化」こそが、Kafka DSL の最大のUX価値です。導入判断の材料として、技術的な位置づけや適用場面を整理するベースとしてご活用ください。

