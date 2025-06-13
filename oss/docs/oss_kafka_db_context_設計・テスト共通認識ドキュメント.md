# KafkaDbContext OSS

## 1. 概要

本OSSは、EntityFramework（EF）流の記述体験でKafka/ksqlDB/ストリームデータを型安全・LINQで操作可能にするC#ライブラリです。 DbContext流API・POCOモデル・Fluent API・LINQ・リアルタイム購読までカバーします。

---

## 2. 主要クラス/I/F一覧（RDB対比）

| 用途         | EntityFramework       | 本OSS（Kafka/ksqlDB）     | 備考             |
| ---------- | --------------------- | ---------------------- | -------------- |
| 管理本体       | DbContext             | KafkaDbContext         |                |
| エンティティ     | DbSet                 | EventSet               | 型で区別           |
| FluentAPI  | Entity                | Event                  | modelBuilder.〜 |
| クエリ記述      | LINQ                  | LINQ                   | どちらも共通         |
| 追加         | Add/AddAsync          | AddAsync               | Kafka Produce  |
| 取得         | ToList/FirstOrDefault | ToList/FirstOrDefault  |                |
| 購読         | (なし)                  | Subscribe/ForEachAsync | Push型体験        |
| SQL/KSQL出力 | ToSql                 | ToKsql                 | デバッグ/説明用       |

---

## 3. 主な protected override（RDB流との対応）

| メソッド名             | 本OSSでの役割                         | 必要性・備考 |
| ----------------- | -------------------------------- | ------ |
| OnModelCreating   | POCO/FluentでKafkaストリーム/スキーマ定義    | 必須     |
| OnConfiguring     | Kafka/ksqlDB/Schema Registry接続設定 | 必須     |
| Dispose           | Producer/Consumerリソース解放          | 必須     |
| SaveChanges/Async | Kafka流では即時送信なので通常不要（拡張可）         | 要件次第   |
| EnsureCreated     | ストリーム/テーブル/スキーマ自動作成              | 任意     |

---

## 4. サンプルコード（利用イメージ）

```csharp
public class MyKafkaDbContext : KafkaDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Event<TradeEvent>(e => {
            e.HasKey(t => t.TradeId);
            e.Property(t => t.Symbol).HasMaxLength(12);
            e.WithKafkaTopic("trade-events");
            e.AsStream();
            e.WithSchemaRegistry(reg =>
            {
                reg.Avro();
                reg.RegisterOnStartup();
            });
        });
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseKafka("localhost:9092");
        optionsBuilder.UseSchemaRegistry("http://localhost:8081");
    }
}

var db = new MyKafkaDbContext();
await db.TradeEvents.AddAsync(new TradeEvent { TradeId = 1, Symbol = "USD/JPY", Amount = 1000000 });
var list = db.TradeEvents.Where(e => e.Amount > 1000).ToList();
db.TradeEvents.Subscribe(e => Console.WriteLine(e));
Console.WriteLine(db.TradeEvents.Where(e => e.Amount > 1000).ToKsql());
```

---

## 5. テスト観点サンプル

- POCOモデル・Fluent APIでKafkaストリーム/テーブル定義可能か
- LINQクエリでフィルタ/集計/Select/GroupByが正常動作するか
- AddAsyncでKafkaにイベントが正しく送信されるか
- ToList, Subscribe, ForEachAsync等でリアルタイム/バッチ購読が動作するか
- ToKsqlでLINQ→KSQL文変換が期待通りか
- OnConfiguring/Dispose等のリソース・設定が意図通り動作するか

---

## 6. 補足

- DbContext/DbSetと並列運用可、現場混乱なし
- Kafka/ksqlDB知識ゼロでも、EF流でそのまま使える命名・API設計

---

> 本ドキュメントはOSS設計・テストの共通認識用として作成しています。ご質問・指摘は天城まで！

