# 4.Messaging Phase3: Kafka通信層の機能分離とモジュール再構成

## 🎯 目標
Communication名前空間の機能をMessaging専用モジュールへ分離し、責務を明確化

## 📦 新構造配置

```
src/Messaging/
├── Producers/
│   ├── Core/
│   │   └── KafkaProducer.cs (移動 + 最適化)
│   ├── Pool/
│   │   └── ProducerPoolManager.cs (統合・IPoolManager準拠)
│   └── Health/
│       └── ProducerHealthChecker.cs (新設)
├── Consumers/
│   ├── Core/
│   │   └── KafkaConsumer.cs (移動 + 最適化)
│   ├── Pool/
│   │   └── ConsumerPoolManager.cs (移動)
│   └── Subscription/
│       └── SubscriptionManager.cs (独立分離)
├── Bus/
│   ├── KafkaMessageBus.cs (移動)
│   └── BusDiagnostics.cs (新設・監視分離)
└── Abstractions/
    ├── IKafkaProducer<T>.cs (移動)
    ├── IKafkaConsumer<T>.cs (移動)
    ├── ISubscriptionManager<T>.cs (新設)
    └── IMessageBusCoordinator.cs (新設)
```

## ✅ 分離内容

### Producer系
- `KafkaProducer.cs` → `Producers/Core/` へ移動
- `KafkaProducerPool.cs` → `Producers/Pool/ProducerPoolManager.cs` に統合
- `IPoolManager<TKey, T>` 準拠に改修

### Consumer系  
- `KafkaConsumer.cs` → `Consumers/Core/` へ移動
- `KafkaConsumerPool.cs` → `Consumers/Pool/ConsumerPoolManager.cs` へ移動
- 購読制御ロジック → `Subscription/SubscriptionManager.cs` として独立

### 統合バス
- `KafkaMessageBus.cs` → `Bus/` へ移動
- `BusDiagnostics.cs` を新設し、監視/統計の分離
- `IMessageBusCoordinator` による依存注入構成

## 📘 設計ガイドライン

- 名前空間: `KsqlDsl.Messaging.Xxx`
- Producer/Consumer は単体でも利用可能
- `KafkaMessageBus` は `IMessageBusCoordinator` 実装
- 設定系は次フェーズ（Phase 4）で分離

## 🧪 テスト配置

```
tests/Messaging/
├── Producers/
├── Consumers/
├── Bus/
└── Integration/
```

## ⚠️ 注意事項

- `KafkaMessageBusOptions.cs` は Phase 4 で移動
- 監視ロジックは既存 `Monitoring/` と統合想定
- インターフェースは `Abstractions/` に統一