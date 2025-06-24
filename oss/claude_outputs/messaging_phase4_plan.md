# 🎯 4.Messaging Phase 4: 設定系分離と統合

## 📦 作業概要
`KafkaMessageBusOptions.cs`（800行）を Configuration/ へ分離し、環境別設定とモニタリング統合を実現

## 🔄 移動対象

### Phase 3 → Phase 4 移動
```
src/Communication/KafkaMessageBusOptions.cs (800行)
↓
src/Messaging/Configuration/
├── MessageBusConfiguration.cs     (統合設定)
├── ProducerConfiguration.cs       (Producer設定)  
├── ConsumerConfiguration.cs       (Consumer設定)
├── PoolConfiguration.cs           (プール設定)
├── HealthConfiguration.cs         (ヘルス設定)
└── EnvironmentOverrides.cs        (環境別上書き)
```

## 🎯 Phase 4 目標

✅ **設定分離**: 800行設定ファイルの機能別分割  
✅ **環境対応**: 開発/本番/テスト環境別設定  
✅ **モニタリング統合**: 既存Monitoring/との連携  
✅ **検証強化**: 設定値の検証とデフォルト補完  
✅ **Hot Reload**: 実行時設定変更対応  

## 📋 作業手順

1. 設定クラス分離・再構成
2. 環境別オーバーライド機能
3. Monitoring統合
4. 検証・デフォルト補完
5. Hot Reload対応