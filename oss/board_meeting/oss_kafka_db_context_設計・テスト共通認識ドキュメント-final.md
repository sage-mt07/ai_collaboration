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

## 鳴瀬へのコーディング指示

（KafkaContext OSS: POCO属性主導バージョン）

---

## 1. POCOクラスへの属性設計（[Topic]属性）

- POCOごとに `[Topic]` 属性で**物理トピック名・各種パラメータ**を必ず指定すること。
  ```csharp
  [Topic(
      "trade-events",
      PartitionCount = 3,
      ReplicationFactor = 2,
      RetentionMs = 259200000,
      Compaction = true,
      DeadLetterQueue = true,
      Description = "FX取引イベントストリーム"
  )]
  public class TradeEvent
  {
      [Key]
      public long TradeId { get; set; }
      [MaxLength(12)]
      public string Symbol { get; set; }
      [DefaultValue(0)]
      public decimal Amount { get; set; }
      // 他プロパティもC#標準属性を優先
  }
  ```

---

## 2. モデル登録はPOCO列挙のみ

- `modelBuilder.Event<TradeEvent>();` のように**属性付きPOCOを登録するだけ**\
  （Fluent APIで物理名やパラメータ上書き禁止）

---

## 3. バリデーションモード

- デフォルトは**厳格バリデーション**\
  → `[Topic]`や`[Key]`未定義は例外停止
- 学習・PoC用途のみ、`optionsBuilder.EnableRelaxedValidation();`で「属性なしでも動作」可（警告必須）

---

## 4. 運用値パラメータの上書き

- RetentionMsなど**運用値パラメータは外部設定/Fluent APIで上書き可能**
  - 属性値は初期値・設計ガイド
  - OnConfiguring等で `optionsBuilder.OverrideTopicOption<TradeEvent>(...)` で上書きOK

---

## 5. POCOプロパティのnull許容

- `int?` `decimal?` `string?` など**C#標準nullable型でnull許容**
- `[IsRequired]`属性は実装しない
- 必須値としたい場合は**非null型で宣言**
- Kafka/ksqlDB/Avroスキーマもこの型定義に従う

---

## 6. テスト観点

- 上記仕様で「型安全・Kafka/ksqlDB/Avroスキーマ自動生成・LINQクエリ・リアルタイム購読」などが一貫して動作するか網羅テスト

---

> **このガイドラインに従い、POCO属性主導型のKafkaContext OSS実装・テストコードを作成してください。**\
> **疑問点や補足要望があれば、天城までエスカレーション！**

---

## 7. KSQL変換ルール対応表（OrderByはサポート外）

| C# LINQ記述例                                         | 生成されるKSQL文例                                        | 備考・補足            |
| -------------------------------------------------- | -------------------------------------------------- | ---------------- |
| `Where(e => e.Amount > 1000)`                      | `WHERE Amount > 1000`                              | フィルタ条件           |
| `Select(e => new { e.TradeId, e.Amount })`         | `SELECT TradeId, Amount`                           | 投影・プロジェクション      |
| `GroupBy(e => e.Symbol)`                           | `GROUP BY Symbol`                                  | 集約・ウィンドウ         |
| `.Sum(e => e.Amount)`                              | `SUM(Amount)`                                      | 集計関数             |
| `Join(db.Other, ...)`                              | `JOIN other_stream ON ...`                         | ストリーム/テーブルJOIN   |
| `Take(10)`                                         | `LIMIT 10`                                         | KSQLは一部LIMITサポート |
| `AsTable()` / `AsStream()`                         | `CREATE TABLE ...` / `CREATE STREAM ...`           | 明示的なテーブル/ストリーム指定 |
| `Select(e => new { e.Symbol, Count = e.Count() })` | `SELECT Symbol, COUNT(*) AS Count GROUP BY Symbol` | グループ集計例          |
| `WindowedBy(TimeSpan.FromMinutes(1))`              | `WINDOW TUMBLING (SIZE 1 MINUTE)`                  | ウィンドウクエリ         |

> **OrderByは今回サポート外です。**

## 8. 例外設計・未サポート/不正ケースのふるまい

\- OSSがサポートしていないLINQ記述（例：OrderBy等）を検出した場合、即時 \`NotSupportedException\` を発生させます。

&#x20; \- 例外例：「OrderBy句はサポート外です。現バージョンでは指定しないでください。」

\- KSQL文の自動生成ロジックが、変換不能や不正な状態になった場合、\`InvalidOperationException\` で停止します。

&#x20; \- 例外例：「不正なKSQL文生成要求です。GroupByのキーが指定されていません。」

\- POCO属性や型定義に不備があった場合、既定通りの厳格バリデーションで即例外停止となります。

&#x20; \- 例外例：「[Topic]属性未定義のため、TradeEventをマッピングできません。」

\- Join/Window等のパラメータ不整合も、同様に明示的な例外で通知します。

&#x20; \- 例外例：「Join対象のEventSetが見つかりません」「Window句指定に不正なパラメータ（例：負値）が含まれています」



\- これら例外メッセージは、OSS利用者が即座に原因特定できるよう、\*\*「どこがどう未サポート・不正か」を具体的に記述\*\*することを原則とします。

#### ■ Deserializeエラーの発生抑止設計

- 本OSSは`OnModelCreating`でPOCO定義→Avroスキーマ自動生成→Schema Registry登録→そのスキーマでのみConsume、という設計方針を採用しています。
- このため、**通常運用下（設計時のPOCO定義とスキーマが一致している限り）、Deserialize例外（型・フィールド不一致等）は原則発生しません**。
- Deserialize例外が発生するのは、外部から不正データが投入された場合や、複数バージョン混在等の「設計外の状況」のみです。
- **この設計方針により「型安全」「高信頼なメッセージ処理」が保証されます。**

---

## 9. Avroスキーマ自動生成：型マッピング仕様

### 1. 基本型対応表

| C#型      | Avro型                                                     | Nullable時                                                                  | 備考                                                                  |
| -------- | --------------------------------------------------------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| int      | int                                                       | ["null","int"]                                                             | 32bit整数                                                             |
| long     | long                                                      | ["null","long"]                                                            | 64bit整数                                                             |
| float    | float                                                     | ["null","float"]                                                           | 32bit浮動小数                                                           |
| double   | double                                                    | ["null","double"]                                                          | 64bit浮動小数                                                           |
| decimal  | bytes + logicalType\:decimal (precision: 28, scale: 6 既定) | ["null",{"type":"bytes","logicalType":"decimal","precision":28,"scale":6}] | **精度・スケール指定がなければ precision=28, scale=6 を既定値とする。必要に応じてPOCO属性で明示指定可** |
| string   | string                                                    | ["null","string"]                                                          | MaxLengthはAvro上制約不可                                                 |
| bool     | boolean                                                   | ["null","boolean"]                                                         |                                                                     |
| DateTime | long + logicalType\:timestamp-millis                      | ["null",{"type":"long","logicalType":"timestamp-millis"}]                  | ISO8601変換                                                           |
| byte[]   | bytes                                                     | ["null","bytes"]                                                           |                                                                     |
| enum     | enum                                                      | ["null", ...enum...]                                                       | Avro enum自動生成                                                       |

### 2. POCO属性のAvro反映

| POCO属性               | Avroスキーマ反映例                       | 備考                          |
| -------------------- | --------------------------------- | --------------------------- |
| [Key]                | "name": "TradeId", "type": "long" | Avro自体はPK概念なし               |
| [MaxLength(12)]      | "type": "string"                  | Avro上はMaxLength制約不可         |
| [DefaultValue(0)]    | "default": 0                      | デフォルト値を反映                   |
| [DefaultValue(null)] | "default": null                   | nullable型は"default": null必須 |
| [Topic(...)]         | メタ情報                              | スキーマフィールドとは直接関係なし           |

### 3. サンプル自動生成Avroスキーマ（TradeEvent）

```json
{
  "type": "record",
  "name": "TradeEvent",
  "fields": [
    { "name": "TradeId", "type": "long" },
    { "name": "Symbol",  "type": ["null", "string"], "default": null },
    { "name": "Amount",  "type": "double", "default": 0 }
  ]
}
```

- `string?`は`["null","string"]`形式
- `decimal`や`DateTime`はlogicalType指定
- `[DefaultValue]`も反映される

### 4. 拡張・バージョニング

- フィールド追加時は「default」必須（Avro互換維持のため）

- フィールド削除・型変更時は原則非推奨（互換性注意）

- KafkaContext初期化時またはトピック初利用時に、POCOクラスからAvroスキーマを自動生成し、Schema Registryに登録します。既存スキーマがある場合は必ず互換性を検証します。

- スキーマ登録や互換性検証で失敗した場合、例外停止＆エラーメッセージで詳細を通知します。

- スキーマ名は\`\<Namespace>.\<POCO名>\`で統一し、バージョン管理はSchema Registryに委任します。

- 破壊的変更が避けられない場合は「新POCO＋新トピック」の追加で運用分離することを推奨します。

## 10. Avroスキーマのバージョニング・Schema Registry連携ルール

- **Avroスキーマの自動生成・Schema Registry登録は、必ず**\`\`**完了後に行うこと。**
  - `OnModelCreating`で全POCO・マッピング定義が確定してからでないと、スキーマ自動生成やRegistry登録を行うべきではありません。
- これ以外（例えば初回アクセスやAddAsync時）に登録処理を行うと、未確定モデルの状態でスキーマ登録が走り、実行時例外や破壊的挙動（型不一致等）につながるため、アプリケーションの安全性・予測可能性を大きく損ないます。
- KafkaContextの初期化フロー内で、`OnModelCreating`実行後「POCO全体からAvroスキーマ自動生成→Schema Registryへ一括登録」を強制する実装・運用を原則とします。
- これにより「未定義エンティティの登録事故」や「起動後動的スキーマ変更」による不整合を100%防止できます。

### スキーマ互換性・バージョニング

- スキーマの後方互換性を最優先し、フィールド追加時は必ず"default"値を付与してください。
- フィールド削除・型変更（例：int→string）は原則非推奨とし、どうしても必要な場合はSchema Registryの互換性チェック結果を確認してください。
- KafkaContext初期化時またはトピック初利用時に、POCOクラスからAvroスキーマを自動生成し、Schema Registryに登録します。既存スキーマがある場合は必ず互換性を検証します。
- スキーマ登録や互換性検証で失敗した場合、例外停止＆エラーメッセージで詳細を通知します。
- スキーマ名は`<Namespace>.<POCO名>`で統一し、バージョン管理はSchema Registryに委任します。
- 破壊的変更が避けられない場合は「新POCO＋新トピック」の追加で運用分離することを推奨します。

## 11. 非同期AddAsyncのawaitとパフォーマンス設計

- \*\*通常、\*\*\*\*`AddAsync`**は**`await`\*\***を付けて呼ぶことを推奨します。**
  - 標準的なC#プログラマーが直感的に理解できる「確実な送信・エラーハンドリング」を保証します。
- \*\*高パフォーマンス用途・大量送信バッチ等では、\*\*\*\*`await`\*\***を付けず「Fire & Forget」型で呼び出し、後続でまとめて完了を監視・回収する運用もサポートします。**
  - この場合は「送信キュー溢れ」や「例外の遅延検知」に注意が必要です。
- \*\*OSSガイド・サンプルでもこの両モードの使い分けを明記し、\*\*\*\*`await`\*\***無しの場合の動作注意事項も設計ガイドに記載します。**

---

## 12. ForEachAsyncのタイムアウト・CancellationToken設計

- **ForEachAsyncなどのConsumer APIには、タイムアウト（timeout）およびCancellationTokenパラメータを指定可能とします。**
  - 例:
    ```
    csharp
    ```
    コピーする編集する

    `await db.TradeEvents.ForEachAsync(handler, timeout: TimeSpan.FromSeconds(30), cancellationToken: token);`
- タイムアウトを指定することで、一定時間内に新規データが到着しなければ自動で処理を終了できます。
- CancellationTokenを渡すことで、外部から安全に購読処理のキャンセル（停止）が可能です。
- OSSガイド・サンプルにもこのオプション指定例を明記し、「長期購読・バッチ処理・アプリシャットダウン対応」など実運用シーンをカバーします。

## 13. Consumer設計とKafka接続時のオプション整理

### 1. 主なConsumer API設計（ForEachAsync/Subscribe等）

- ForEachAsync/Subscribe等のConsumer APIは、非同期でKafkaトピックからPOCOモデルのストリームデータを受信する。
- ForEachAsyncは
  - handler（1件ごとの処理デリゲート）
  - timeout（一定時間無通信で自動終了）
  - cancellationToken（外部キャンセル用）\
    の各パラメータ指定が可能。
    ```
    csharp
    ```
    コピーする編集する

    `await db.TradeEvents.ForEachAsync( handler, timeout: TimeSpan.FromSeconds(30), cancellationToken: token);`
- 長期購読や一括バッチ読込、Graceful Shutdown、タイムアウト制御など実運用に即した制御が可能。

---

### 2. Kafka接続時の主なオプション（OnConfiguring等）

- KafkaContextのOnConfiguringで指定できる代表的なKafka Consumerオプション：
  - BootstrapServers（Kafkaブローカーアドレス、必須）
  - GroupId（Consumer Group ID、複数アプリ分散協調に必須）
  - AutoOffsetReset（最初の購読開始位置："earliest" or "latest"）
  - EnableAutoCommit（オフセット自動コミット可否。高信頼用途では明示的コミット推奨）
  - MaxPollIntervalMs（メッセージ最大処理時間。長処理系は値拡大必須）
  - SessionTimeoutMs（Consumer検出までの最大時間）
  - SecurityProtocol等（SSL/SASL認証利用時の各種パラメータ）
  - FetchMinBytes/FetchMaxBytes等（1バッチ最大データ量等）
  ```
  csharp
  ```
  コピーする編集する

  `protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { optionsBuilder.UseKafka("localhost:9092", consumerOptions: new KafkaConsumerOptions { GroupId = "my-app-group", AutoOffsetReset = "earliest", EnableAutoCommit = false, MaxPollIntervalMs = 300000, // 他必要な項目… }); }`

---

### 3. 複数トピックのAutoOffsetReset個別指定

- 一つのアプリから複数のトピックを参照する場合、**EventSet（トピック）ごとにAutoOffsetReset等のConsumerオプションを個別に設定可能とする**。
- 例：
  ```
  csharp
  ```
  コピーする編集する

  `protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { optionsBuilder.UseKafka("localhost:9092"); optionsBuilder.ConfigureConsumer<TradeEvent>(opt => opt.AutoOffsetReset = "earliest"); optionsBuilder.ConfigureConsumer<SystemAlert>(opt => opt.AutoOffsetReset = "latest"); }`
- 監査用途やリプレイ用途はearliest、本番運用はlatestなどの現実的なシナリオに対応。

---

### 4. 注意点・設計方針

- Consumerは必ずGroupIdごとに分離され、複数Consumerでの分散協調が可能。
- オフセット管理は業務要件に合わせて「自動/手動コミット」選択可。
- DLQ連携やエラー発生時のリカバリも今後の詳細設計で明記。
- APIガイド・サンプルにもConsumerオプション例・ベストプラクティスを反映予定。

### 5. Kafka Consumerオプション未指定時のデフォルト動作

- KafkaContextのOnConfiguringでグローバルなConsumerオプションを指定しなかった場合、OSSはKafka公式クライアントの標準デフォルト値を採用します。
  - 例：AutoOffsetReset未指定時は "latest"（Kafka .NET公式デフォルトに準拠）
  - GroupId未指定時はエラー（Kafkaクライアントで必須）
- EventSet単位で個別指定がある場合はそのEventSetの値が優先、なければグローバル指定、なければKafkaクライアントの標準デフォルトが適用されます。
  1. EventSet（トピック）ごとの個別指定
  2. OnConfiguringでのグローバルConsumerオプション指定
  3. Kafka公式クライアントの標準デフォルト
- 公式サンプルにもこの優先順位を明示し、未指定時の「想定外挙動」を防止します。
