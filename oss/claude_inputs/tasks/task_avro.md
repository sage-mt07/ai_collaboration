# 全体構成概要（対象はAvro × Kafka × 複合PK）

- 各POCOはKafka Topicに対応するEntityである
- KeyとValueは別スキーマとしてSchema Registryに登録される
- Producer/Consumerで型整合性が重要（KafkaMessage<Key, Value>）
- 複数の型にわたるキャッシュ設計が必要
- 将来的にスキーマ進化やバージョン切り替えを想定

## AvroSerializer/Deserializerの生成に関して

- 各Entityに対して Key/Value を分離
- スキーマは `Avro_{Entity}_Key`, `Avro_{Entity}_Value`
- `decimal`, `DateTime` はlogicalType対応（schema側）
- キャッシュは型＋スキーマIDで管理
- テストは round-trip 完全一致で自動生成

## 鳴瀬：まず設計ドキュメントを出力せよ

次の観点からクラス・構造・依存関係を整理せよ：

1. POCO → AvroKey / AvroValue の変換規則
2. AvroSerializer/Deserializerの生成およびキャッシュ構造
3. テストの構成（対象、命名、roundtrip確認）
4. SchemaRegistry登録構造（configと連動）
5. スキーマ進化時の影響と対応想定

構造のドラフト出力後に、必要に応じて製造に進むこと。
