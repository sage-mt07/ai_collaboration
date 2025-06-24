## ⚙️ KsqlDsl appsettings.json 構成仕様

Kafka.Ksql.Linq では、`appsettings.json` を通じて柔軟なDSL設定が可能です。以下はその構成要素と意味です。

---

### 📐 基本構造

```json
{
  "KsqlDsl": {
    "ValidationMode": "Strict|Relaxed",
    "Common": { /* 共通設定 */ },
    "Topics": { /* トピック別設定 */ },
    "SchemaRegistry": { /* スキーマレジストリ設定 */ },
    "Entities": [ /* エンティティ／StateStore 設定 */ ]
  }
}
```

---

### 🧱 2.1 Common（共通Kafka設定）

| 項目 | 説明 |
|------|------|
| `BootstrapServers` | Kafkaブローカーの接続先 |
| `ClientId` | 接続クライアント識別子 |
| `RequestTimeoutMs` | Kafka操作タイムアウト（ms） |
| `SecurityProtocol` | `Plaintext` / `SaslPlaintext` など |
| `SaslMechanism` | 認証方式（例：`Plain`, `ScramSha256`） |
| `SaslUsername`, `SaslPassword` | SASL認証情報 |

```json
"Common": {
  "BootstrapServers": "localhost:9092",
  "ClientId": "ksql-dsl-client",
  "RequestTimeoutMs": 30000,
  "SecurityProtocol": "Plaintext",
  "SaslMechanism": "Plain",
  "SaslUsername": "user",
  "SaslPassword": "pass"
}
```

---

### 📦 2.2 Topics（トピックごとの詳細設定）

```json
"Topics": {
  "my-topic": {
    "Producer": {
      "Acks": "All",
      "CompressionType": "Snappy",
      "EnableIdempotence": true,
      "BatchSize": 16384,
      "LingerMs": 5
    },
    "Consumer": {
      "GroupId": "my-group",
      "AutoOffsetReset": "Latest",
      "EnableAutoCommit": true,
      "SessionTimeoutMs": 30000
    }
  }
}
```

| Producer設定 | 説明 |
|------------------|------|
| `Acks` | 書き込み応答の強度設定（例：`All`, `1`） |
| `CompressionType` | 圧縮方式（`Snappy`, `Gzip`, `Lz4`など） |
| `EnableIdempotence` | 冪等性設定（重複防止） |
| `BatchSize` | バッチ書き込み単位（byte） |
| `LingerMs` | バッチ送信待機時間（ms） |

| Consumer設定 | 説明 |
|------------------|------|
| `GroupId` | コンシューマーグループID |
| `AutoOffsetReset` | `Latest` or `Earliest` |
| `EnableAutoCommit` | 自動コミット可否 |
| `SessionTimeoutMs` | セッションタイムアウト（ms） |

---

### 🧬 2.3 SchemaRegistry（スキーマレジストリ設定）

```json
"SchemaRegistry": {
  "Url": "http://localhost:8081",
  "MaxCachedSchemas": 1000,
  "AutoRegisterSchemas": true,
  "BasicAuthUserInfo": "user:pass"
}
```

| 項目 | 説明 |
|------|------|
| `Url` | スキーマレジストリURL |
| `MaxCachedSchemas` | クライアント側でキャッシュする最大スキーマ数 |
| `AutoRegisterSchemas` | スキーマを自動登録するかどうか |
| `BasicAuthUserInfo` | Basic認証用クレデンシャル（形式：`user:pass`） |

---

### 🏪 2.4 Entities（StateStore定義）

```json
"Entities": [
  {
    "Entity": "OrderEntity",
    "SourceTopic": "orders",
    "StoreType": "RocksDb",
    "EnableCache": true,
    "Windows": [5, 15, 60]
  }
]
```

| 項目 | 説明 |
|------|------|
| `Entity` | 対象POCOクラス名 |
| `SourceTopic` | 入力元となるKafkaトピック名 |
| `StoreType` | ストレージ方式（例：`RocksDb`） |
| `EnableCache` | キャッシュ有効化（bool） |
| `Windows` | タンブリングウィンドウサイズ（整数：分単位） |

---

### 🛡️ 2.5 ValidationMode

| 値 | 説明 |
|-----|------|
| `Strict` | 起動時に全構成値を検証。欠落や不整合があるとエラー（推奨） |
| `Relaxed` | 柔軟に読み込む。テスト・一時構成に使用可 |

```json
"ValidationMode": "Strict"
```

---

### 🔄 今後追加予定（案内のみ）

- `DLQ（Dead Letter Queue）` 設定（メッセージ変換失敗時の退避先）
- `WindowHeartbeat`: ウィンドウの生存確認用トピック設定
- `FileRotation`: RocksDbなどの物理ファイル分割・保存条件
- `PreWarm`: 起動時にスキーマやメタ情報を事前読み込み

（これらは別途 `AdvancedSettings` などの構造で提供予定）

