// src/Core/Documentation/CoreResponsibilities.md
/*
# Core層責務定義

## 概要
Core層はKsqlDslライブラリの最下層として、他の全層の基盤となる抽象定義と共通POCOモデルを提供する。

## 責務

### 1. 抽象インターフェース定義
- `IKafkaContext`: DbContext風の統一コンテキスト
- `IEntitySet<T>`: LINQ対応クエリ・更新操作
- `ISerializationManager<T>`: シリアライザー抽象化
- `IPoolManager<TKey, TItem>`: プール管理統一
- `IHealthMonitor`: ヘルス監視抽象化

### 2. POCOモデル管理
- Trade, Quote, Order, User, MarketData
- Avroスキーマとの対応準備
- 属性ベース設定（Topic, Key, DefaultValue等）

### 3. コンテキスト基盤
- KafkaContextCore: 外部依存なし実装
- ModelBinding: POCO属性解析
- 設定管理・診断機能

## 設計制約

### 依存関係
- Core層は他の全層に依存してはならない
- 上位層（Messaging, Serialization, Monitoring）はCore層のみに依存
- 循環依存の完全排除

### 名前空間構造
```
KsqlDsl.Core/
├── Abstractions/     # インターフェース定義
├── Context/          # KafkaContext実装
├── Models/           # POCOモデル
└── Documentation/    # 設計文書
```

### テスト戦略
- インターフェース単体テスト
- ModelBinding機能テスト
- 依存関係検証テスト
- POCO属性解析テスト

## 利用方法

### 基本的な使用例
```csharp
public class TradingContext : KafkaContextCore
{
    public IEntitySet<Trade> Trades => Set<Trade>();
    public IEntitySet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Event<Trade>();
        modelBuilder.Event<Quote>();
    }

    protected override IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel)
    {
        // 上位層で実装される
        return new EventSet<T>(this, entityModel);
    }
}
```

### 依存注入
```csharp
services.AddKsqlDslCore()
        .AddKsqlDslMessaging()  // Communication層
        .AddKsqlDslSerialization()  // Avro層
        .AddKsqlDslMonitoring();    // 監視層
```

## 今後の拡張

### Phase 2での追加予定
- パフォーマンスメトリクス抽象化
- 分散トレーシング統合
- 設定検証機能強化
- POCO自動生成支援

### 互換性維持
- インターフェース変更時のバージョニング
- 既存POCOモデルとの後方互換性
- 段階的移行支援機能
*/