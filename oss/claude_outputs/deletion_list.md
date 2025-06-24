# 物理削除対象ファイル一覧

## 🗑️ **即座に物理削除可能なファイル**

### **1. 重複Enum定義ファイル**
```
❌ src/Configuration/Abstractions/SecurityProtocol.cs
   → 既にコメントアウト済み、Confluent.Kafka.SecurityProtocolを使用

❌ src/Configuration/Abstractions/IsolationLevel.cs  
   → Confluent.Kafka.IsolationLevelを使用
```

### **2. 過剰なProducer/Consumer設定ファイル**
```
❌ src/Configuration/Abstractions/KafkaProducerOptions.cs
   → KafkaBusOptionsで代替、詳細すぎる

❌ src/Configuration/Abstractions/KafkaConsumerOptions.cs
   → KafkaBusOptionsで代替、詳細すぎる

❌ src/Configuration/Abstractions/KafkaBatchOptions.cs
   → バッチ処理詳細設定は不要

❌ src/Configuration/Abstractions/KafkaFetchOptions.cs
   → フェッチ詳細設定は不要

❌ src/Configuration/Abstractions/KafkaSubscriptionOptions.cs
   → 購読詳細設定は不要
```

### **3. Pool関連設定ファイル（過剰設計）**
```
❌ src/Configuration/Abstractions/ProducerPoolConfig.cs
   → プール詳細設定は現段階では不要

❌ src/Configuration/Abstractions/ConsumerPoolConfig.cs
   → プール詳細設定は現段階では不要
```

### **4. Health監視設定ファイル（責務混在）**
```
❌ src/Configuration/Abstractions/ProducerHealthThresholds.cs
   → Monitoring層で管理すべき

❌ src/Configuration/Abstractions/ConsumerHealthThresholds.cs
   → Monitoring層で管理すべき
```

### **5. 複雑すぎるContext設定**
```
❌ src/Configuration/Abstractions/KafkaContextOptions.cs
   → 過度に複雑、基本設定で代替可能

❌ src/Configuration/Builders/KafkaContextOptionsBuilder.cs
   → KafkaContextOptionsと連動、不要

❌ src/Configuration/Extensions/KafkaContextOptionsBuilderExtensions.cs
   → KafkaContextOptionsBuilderと連動、不要
```

### **6. Avro設定（Serialization層重複）**
```
❌ src/Configuration/Options/AvroHealthCheckOptions.cs
   → Monitoring層で管理すべき

❌ src/Configuration/Options/AvroRetryPolicy.cs
   → Serialization層で管理すべき
```

---

## ⚠️ **要注意：参照確認が必要なファイル**

### **Enum削除前に参照確認必要**
```
⚠️ src/Configuration/Abstractions/AutoOffsetReset.cs
   → 使用頻度高、削除前に参照確認

⚠️ src/Configuration/Abstractions/ValidationMode.cs
   → KsqlDsl独自概念、保持すべきかも
```

### **拡張メソッド（一部参照あり）**
```
⚠️ src/Configuration/Extensions/KafkaConfigurationExtensions.cs
   → 削除対象クラスへの変換処理含む、修正後に削除検討
```

---

## 📊 **削除サマリー**

| カテゴリ | 削除ファイル数 | 削除理由 |
|----------|---------------|----------|
| 重複Enum | 2個 | Confluent.Kafka直接使用 |
| Producer/Consumer詳細設定 | 5個 | 過剰設計 |
| Pool設定 | 2個 | 現段階では不要 |
| Health設定 | 2個 | 責務混在 |
| Context設定 | 3個 | 過度に複雑 |
| Avro設定 | 2個 | 他層での管理が適切 |
| **合計** | **16個** | |

---

## 🎯 **削除実行順序**

### **Phase 1: 安全な削除（参照なし）**
1. `SecurityProtocol.cs` (既にコメントアウト)
2. Pool関連設定ファイル群
3. Health関連設定ファイル群
4. Avro設定ファイル群

### **Phase 2: 参照修正後削除**
1. `KafkaConfigurationExtensions.cs`の修正
2. Producer/Consumer詳細設定削除
3. Context関連設定削除

### **Phase 3: 慎重な判断**
1. `AutoOffsetReset.cs`の参照確認
2. `IsolationLevel.cs`の参照確認

---

## ✅ **即座に削除開始可能**

**Phase 1の8ファイル**は参照依存が少ないため、即座に削除可能です。

削除を開始してもよろしいでしょうか？