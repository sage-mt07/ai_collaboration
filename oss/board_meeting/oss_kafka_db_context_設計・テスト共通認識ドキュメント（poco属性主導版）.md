# KafkaContext OSS

## 設計ポリシー（2025-06-14修正版）

### 【POCO属性で一意定義、Fluent APIの物理名上書きは禁止】

- POCOクラス属性（例：[Topic(...)]) で物理トピック・パーティション等を一意に指定すること。
  - 例：
  ```csharp
  [Topic("trade-events", PartitionCount = 3)]
  public class TradeEvent
  {
      [Key]
      public long TradeId { get; set; }
      [MaxLength(12)]
      public string Symbol { get; set; }
      // ...他プロパティ
  }
  ```
- POCO\:Topic=1:1のマッピングをライブラリ側で強制。
- Fluent APIでのトピック名や物理名の上書き（WithKafkaTopic等）は禁止。
- modelBuilderはPOCOを宣言するだけ（属性による設定を利用）。
- プロパティの型・バリデーション・デフォルト値もPOCO属性で記述。
  - 例：[MaxLength(12)] [DefaultValue(0)] [Key] など。

## 1. 概要

本OSSは、EntityFramework（EF）流の記述体験でKafka/ksqlDB/ストリームデータを型安全・LINQで操作可能にするC#ライブラリです。 POCO属性主導で「型・物理マッピング・制約」が一元管理され、実装・運用・テストの一貫性を担保します。

## 2. 主要クラス/I/F一覧（RDB対比）

| 用途         | EntityFramework       | 本OSS（Kafka/ksqlDB）     | 備考                       |
| ---------- | --------------------- | ---------------------- | ------------------------ |
| 管理本体       | DbContext             | KafkaContext           |                          |
| エンティティ     | DbSet                 | EventSet               | 型で区別                     |
| FluentAPI  | Entity                | Event                  | modelBuilder.〜（POCO列挙のみ） |
| クエリ記述      | LINQ                  | LINQ                   | どちらも共通                   |
| 追加         | Add/AddAsync          | AddAsync               | Kafka Produce            |
| 取得         | ToList/FirstOrDefault | ToList/FirstOrDefault  |                          |
| 購読         | (なし)                  | Subscribe/ForEachAsync | Push型体験                  |
| SQL/KSQL出力 | ToSql                 | ToKsql                 | デバッグ/説明用                 |

## 3. 主な protected override（RDB流との対応）

| メソッド名             | 本OSSでの役割                         | 必要性・備考 |
| ----------------- | -------------------------------- | ------ |
| OnModelCreating   | POCOをmodelBuilderで宣言             | 必須     |
| OnConfiguring     | Kafka/ksqlDB/Schema Registry接続設定 | 必須     |
| Dispose           | Producer/Consumerリソース解放          | 必須     |
| SaveChanges/Async | Kafka流では即時送信なので通常不要（拡張可）         | 要件次第   |
| EnsureCreated     | ストリーム/テーブル/スキーマ自動作成              | 任意     |

## 4. サンプルコード（利用イメージ・POCO属性主導版）

```csharp
[Topic("trade-events", PartitionCount = 3)]
public class TradeEvent
{
    [Key]
    public long TradeId { get; set; }
    [MaxLength(12)]
    public string Symbol { get; set; }
    [DefaultValue(0)]
    public decimal Amount { get; set; }
}

public class MyKafkaContext : KafkaContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Event<TradeEvent>(); // POCOを宣言するだけ
    }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseKafka("localhost:9092");
        optionsBuilder.UseSchemaRegistry("http://localhost:8081");
    }
}

var db = new MyKafkaContext();
await db.TradeEvents.AddAsync(new TradeEvent { TradeId = 1, Symbol = "USD/JPY", Amount = 1000000 });
var list = db.TradeEvents.Where(e => e.Amount > 1000).ToList();
db.TradeEvents.Subscribe(e => Console.WriteLine(e));
Console.WriteLine(db.TradeEvents.Where(e => e.Amount > 1000).ToKsql());
```

## 5. テスト観点サンプル

- POCOモデル（属性付き）でKafkaストリーム/テーブル定義可能か
- LINQクエリでフィルタ/集計/Select/GroupByが正常動作するか
- AddAsyncでKafkaにイベントが正しく送信されるか
- ToList, Subscribe, ForEachAsync等でリアルタイム/バッチ購読が動作するか
- ToKsqlでLINQ→KSQL文変換が期待通りか
- OnConfiguring/Dispose等のリソース・設定が意図通り動作するか

## 6. 属性未定義時の動作規定（バリデーションモード選択）

### 厳格モード（デフォルト: ValidateStrict = true）

- [Topic]（および [Key] など）**必須属性未定義時は例外で停止**
  - 例外例：「TradeEventクラスに[Topic]属性がありません。POCOとKafkaトピック名の1:1マッピングが必要です」
- クラス名→トピック名等の自動補完は**一切行わない**（明示的設計のみ許可）
- [MaxLength]や[DefaultValue]等の**任意属性が未定義の場合は.NET/Avro/KSQLのデフォルト挙動に従う**
  - 例：stringはnull許容、数値型は0、KSQL DDLにも追加制約なし
- 起動時/スキーマ初期化時に**必ずバリデーションを行い、不備は即時通知**

### ゆるめ運用モード（ValidateStrict = false）

- OnConfiguringで `optionsBuilder.EnableRelaxedValidation();` を呼ぶことで「POCO属性がなくても自動マッピングで“なんとなく動く”」
- この場合、[Topic]属性未指定→クラス名＝トピック名、PartitionCount=1等のデフォルト値で自動登録
- 起動時に「属性未定義を自動補完しています」**警告メッセージを必ず表示**
- 本番運用には非推奨（学習・PoC用途限定）

---

## 7. 補足

- DbContext/DbSetと並列運用可、現場混乱なし
- Kafka/ksqlDB知識ゼロでも、EF流でそのまま使える命名・API設計
- 属性で物理トピック等を一意定義することで型安全・運用一貫性・拡張性を実現

---

### POCOプロパティのnull許容について

- `int?`や`decimal?`、`string?`のように**C#標準のnullable型**を使うことで、そのままKafka/Avroでもnull許容フィールドとなります。

- `[IsRequired]`等の「必須指定」属性は本OSSでは用意しません。

- **必須値としたい場合は、非null型（例：**``**、**``**、**``**）で定義してください**。

- Kafka/ksqlDB/Avroのスキーマ設計もC#型定義に追従します。

- DbContext/DbSetと並列運用可、現場混乱なし

- Kafka/ksqlDB知識ゼロでも、EF流でそのまま使える命名・API設計

- 属性で物理トピック等を一意定義することで型安全・運用一貫性・拡張性を実現

