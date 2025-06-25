## ウィンドウDSL拡張設計（2025年6月25日）

### 概要

本拡張は、POCOエンティティに対して複数のウィンドウサイズ（例：1分足、5分足）でのKSQL Table定義を一括生成し、LINQスタイルでアクセスできる機能を提供する。

対象エンティティの拡張として `.Window(int)` アクセサを導入し、

```csharp
ctx.Charts.Window(1).ToList(); // 1分足
ctx.Charts.Window(5).ToList(); // 5分足
```

のような記述を可能にする。

---

### 実行例とKSQLクエリ

#### 1. エンティティ定義例

```csharp
[Topic("orders")]
[Window(Windows = new[] { 1, 5, 15 }, GracePeriodSeconds = 3)]
public class OrderEntity
{
    [Key]
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }

    [AvroTimestamp(IsEventTime = true)]
    public DateTime OrderTime { get; set; }
}
```

#### 2. 発行されるKSQLクエリ

**ベーステーブル作成**

```sql
CREATE TABLE orders_base (
    CUSTOMERID VARCHAR,
    AMOUNT DECIMAL,
    ORDERTIME TIMESTAMP
) WITH (
    KAFKA_TOPIC='orders', 
    VALUE_FORMAT='AVRO', 
    KEY='CUSTOMERID'
);
```

**ウィンドウ集約テーブル作成（1分足）**

```sql
CREATE TABLE orders_window_1min_agg_1234 AS
SELECT 
    CUSTOMERID,
    SUM(AMOUNT) AS total_amount,
    COUNT(*) AS order_count,
    WINDOWSTART,
    WINDOWEND
FROM orders_base
WINDOW TUMBLING (SIZE 1 MINUTES, GRACE PERIOD 3 SECONDS)
GROUP BY CUSTOMERID
EMIT CHANGES;
```

#### 3. 実行時のクエリ例

**Pull Query（現在の状態取得）**

```sql
SELECT * FROM orders_window_1min_agg_1234 
WHERE CUSTOMERID = 'CUST001';
```

**Push Query（リアルタイム監視）**

```sql
SELECT * FROM orders_window_1min_agg_1234 
WHERE total_amount > 1000
EMIT CHANGES;
```

---

### Kafka Streams処理フローと確定足設計

#### レート送信と受信における役割整理

```mermaid
flowchart TD
    A[送信POD: レート配信] -->|orders トピック| B1[受信POD①]
    A -->|orders トピック| B2[受信POD②]
    B1 --> C1[KStream: レート受信 + タイマー処理によるWindow確定]
    B2 --> C2[KStream: レート受信 + タイマー処理によるWindow確定]
    C1 --> D1[集約ウィンドウ処理]
    C2 --> D2[集約ウィンドウ処理]
    D1 --> E[確定足 (orders_window_final) に書き込み]
    D2 --> E
    E --> F[Consumer/分析エンジンが購読]
```

> 🔁 各PODはタイマー駆動でWindowを確定し、確定データを単一のTopic `orders_window_final` に集約して出力する。

> 💡 Heartbeatトピックは廃止。各PODが内部タイマーで自律的にWindow確定を行うことで、設計が簡潔かつ安定性も高くなる。

---

### 補足

- Heartbeat送信は不要。
- RocksDBによるStateStoreは内部処理と再処理用途で、**参照系ではKafkaのみを使用可能**
- 再起動時の課題は、永続化されたトピックへの出力により緩和される
- EMIT FINALが有効になれば、集約トピックへの書き出しがより明確になる
- `orders_window_final` は**複数のPODから書き込まれる可能性がある**。
  - **ウィンドウとキーが一致する場合は「最初に確定したデータを採用」する方針を採る**。
  - 同一足に複数の確定レコードが書き込まれるのは設計上の冗長性であり、整合性の観点から「最初に到着したレコード＝正」の方針が適切。

---

