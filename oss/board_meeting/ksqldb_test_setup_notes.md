# テスト環境構築 議事録（Kafka + ksqlDB）

## 概要
Kafka および ksqlDB を用いたローカルテスト環境を Docker Compose により構築。C# から Kafka にメッセージ送信、ksqlDB によるストリーム確認までを一通り検証。

---

## 使用バージョン
- Kafka: confluentinc/cp-kafka:7.4.3
- ZooKeeper: confluentinc/cp-zookeeper:7.4.3
- ksqlDB Server: confluentinc/ksqldb-server:0.29.0
- ksqlDB CLI: confluentinc/ksqldb-cli:0.29.0

## 前提環境
- Docker Desktop（WSL2対応）
- Windows 11 + Ubuntu（WSL2）
- .NET 8 + Confluent.Kafka C#ライブラリ

---

## 手順

### 1. Docker Compose ファイル準備
```yaml
# docker_compose_ksqldb.yaml
（※ Canvasにある内容が該当。port=9093に統一）
```

### 2. Docker 起動
```bash
docker compose -f docker_compose_ksqldb.yaml up -d
```

### 3. ksqlDB CLI 起動
```bash
docker-compose exec ksqldb-cli ksql http://ksqldb-server:8088
```

### 4. ストリーム作成
```sql
CREATE STREAM test_stream (
  id INT KEY,
  message STRING
) WITH (
  kafka_topic='test_topic',
  value_format='json',
  partitions=1
);
```

### 5. メッセージ投入
#### CLIから投入:
```bash
docker exec -it docker-kafka-1 kafka-console-producer \
  --broker-list kafka:9093 --topic test_topic \
  --property "parse.key=true" --property "key.separator=:"

1:{"id":1,"message":"Hello from CLI"}
```

#### C#から投入:
```csharp
var config = new ProducerConfig {
    BootstrapServers = "localhost:9093",
    ClientId = "csharp-producer"
};

using var producer = new ProducerBuilder<int, string>(config)
    .SetKeySerializer(Serializers.Int32)
    .SetValueSerializer(Serializers.Utf8)
    .Build();

await producer.ProduceAsync("test_topic", new Message<int, string>
{
    Key = 1,
    Value = JsonSerializer.Serialize(new { id = 1, message = "Hello from C# Kafka!" })
});
```

### 6. ストリームの確認
```sql
SELECT * FROM test_stream EMIT CHANGES;
```

### 7. トピックの中身を直接確認（デバッグ用）
```sql
PRINT 'test_topic' FROM BEGINNING;
```

---

## 留意点
- Kafka のポートを `9093` に変更して運用（Windows環境でポート衝突回避のため）
- `KSQL_BOOTSTRAP_SERVERS`, `KAFKA_ADVERTISED_LISTENERS` など設定も 9093 に一致させる必要あり
- C# クライアントからの接続では `localhost:9093` を使用
- `transaction.state.log.replication.factor`, `offsets.topic.replication.factor` などはブローカ数が1のためデフォルトで問題なし（複数台構成の場合は要調整）

---

## 今後の拡張予定
- Avroスキーマ対応
- ksqlDBによるJOINテスト
- Kafka Connect経由でのデータ注入

---

## 作成者
- 開発統括：司令（SEIJI）
- 作業支援：天城
- 最終更新日：2025年6月14日

