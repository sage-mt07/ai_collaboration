# Kafka ライブラリサンプル使用ガイド（AddAsync/ForEachAsync 編）

このガイドでは、KafkaDbContext を利用した POCO ベースの操作を通じて、ksqlDB クエリや Kafka システムの動作を確認するサンプルを紹介します。

---

## 🧱 前提

- Kafka 開発環境構築済（`docs/setup/README_kafka_env_setup.md` 参照）
- VS Code または Visual Studio で本リポジトリを開いていること
- サンプルエンティティ：`Trade` クラス（プロパティ定義済）

---

## 📝 OnModelCreating の役割について

KafkaDbContext はアプリケーション固有の `AppKafkaContext` によって継承されることを前提としています。

この構成において `OnModelCreating` はエンティティの登録を担い、Fluent API（e.Property など）は使用せず、POCO クラスに付与された属性（Attributes）によって構成されます。

このため `OnModelCreating` は `modelBuilder.Event<Trade>();` のように POCO の登録のみを行います。

---

## ➕ レコードの送信（AddAsync）

```csharp
await using var context = new AppKafkaContext();
await context.Trades.AddAsync(new Trade
{
    TradeId = Guid.NewGuid(),
    Symbol = "AAPL",
    Price = 184.12m,
    Volume = 100
});
```

この操作により、Kafka トピック `trades` にレコードが送信されます。

---

## 🔄 レコードの取得（ForEachAsync）

```csharp
await using var context = new AppKafkaContext();
await context.Trades.ForEachAsync(trade =>
{
    Console.WriteLine($"[{trade.Symbol}] Price: {trade.Price} Volume: {trade.Volume}");
});
```

このクエリは ksqlDB 経由で push query を発行します。

---

## 🧪 動作確認

1. Kafka/ksqlDB サービスが稼働していることを確認
2. `.AddAsync()` を実行し、CLIなどで `SELECT * FROM trades EMIT CHANGES;` を確認
3. `.ForEachAsync()` を実行し、リアルタイムにレコードが受信されることを確認

---

## 🛠 ヒント：ブレークポイント位置

- `OnModelCreating` の内部（エンティティ登録処理）
- `AddAsync` の前後
- `ForEachAsync` 内部（デリゲート）

にブレークを置くことで、送信処理・受信処理の流れをデバッグ可能です。

---

このサンプルを通じて、Kafka DSL ライブラリが提供する EntityFramework 風のインターフェースと ksqlDB の連携動作を確認することができます。

