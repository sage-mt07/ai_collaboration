
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
