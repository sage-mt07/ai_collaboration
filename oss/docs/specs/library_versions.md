# Confluent .NET クライアントライブラリとKafkaバージョンの互換性

本ドキュメントは、OSSプロジェクトにおけるKafka/kSQL構成とConfluent製.NETクライアントライブラリの互換性を示します。導入時の環境選定やバージョン整合性の確認に活用してください。

---

## ✅ 推奨構成（最新版ベース）

| 項目                        | 推奨バージョン           |
|-----------------------------|----------------------------|
| Kafka Broker                | 3.9.x                      |
| ksqlDB                      | 0.30.0                     |
| Confluent.Kafka             | 2.10.1                     |
| Confluent.SchemaRegistry    | 2.10.0                     |
| Confluent.SchemaRegistry.Serdes.Avro | 2.10.0           |
| .NET SDK                    | .NET 8                     |


---

## 📦 パッケージ別対応状況

### Confluent.Kafka

| バージョン   | Kafka対応           | .NET対応バージョン         | 備考 |
|--------------|----------------------|------------------------------|------|
| 1.10.0       | Kafka 0.11〜2.6      | .NET Core 3.1 / .NET 5/6     | 安定だが旧世代。Avro 1.10.xと組み合わせ推奨 |
| 1.14.x       | Kafka 2.1〜3.0       | .NET 6/7                     | PullConsumerなど非推奨APIあり |
| **2.10.1**   | Kafka 2.8〜3.9（推定）| .NET 6/8                     | 最新安定版。librdkafka v2.6ベース |

### Confluent.SchemaRegistry / Serdes.Avro

| パッケージ名                          | バージョン | 対応Kafka | 備考 |
|---------------------------------------|------------|------------|------|
| Confluent.SchemaRegistry              | 2.10.0     | Kafka 2.8+  | Schema Registryクライアント（単体） |
| Confluent.SchemaRegistry.Serdes.Avro | 2.10.0     | Kafka 2.8+  | Avroシリアライザ・デシリアライザ |
| Apache.Avro                          | 1.11.x     | -          | 内部依存。手動参照は基本不要 |


---

## 🧭 運用方針

- 本OSSは当面、**Kafka 3.9 + .NET 8 + Confluent.Kafka 2.10.1** を基準に開発・検証を行います。
- 今後、既存Kafka（2.6〜3.6）環境向けには `compatibility_legacy.md` を整備予定です。
- パッケージアップグレード時には本ファイルを随時更新し、導入者のバージョン判断を支援します。

---

## 最終更新日: 2025-06-16

