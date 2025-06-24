# Phase1移行ガイド：旧Serviceクラス廃止対応

## 概要

duplicate_classes_analysis.mdに従い、**Phase1: 旧Serviceクラス削除**を実行しました。
この移行により、重複実装が削除され、より効率的な新Manager実装に統一されます。

## 🔴 廃止されたクラス

### 1. KafkaProducerService → EnhancedKafkaProducerManager
**削除理由**: プール管理なし、パフォーマンス監視機能なし、型安全性が低い

```csharp
// ❌ 廃止 (Phase1で[Obsolete(true)]マーキング)
var service = new KafkaProducerService(options);
await service.SendAsync(entity, entityModel);

// ✅ 新実装
var manager = new EnhancedKafkaProducerManager(...);
var producer = await manager.GetProducerAsync<T>();
await producer.SendAsync(entity);
```

### 2. KafkaConsumerService → KafkaConsumerManager  
**削除理由**: プール管理なし、型安全性が低い、パフォーマンス監視なし

```csharp
// ❌ 廃止 (Phase1で[Obsolete(true)]マーキング)
var service = new KafkaConsumerService(options);
var results = await service.QueryAsync<T>(ksql, entityModel);

// ✅ 新実装 (Phase2で完全移行予定)
var manager = new KafkaConsumerManager(...);
var consumer = await manager.CreateConsumerAsync<T>(options);
var results = await consumer.ConsumeBatchAsync(batchOptions);
```

## 🟡 Phase1での変更内容

### KafkaContext の変更

```csharp
public class MyKafkaContext : KafkaContext
{
    // Phase1変更：内部的に新Manager使用
    // - GetProducerManager() : 新ProducerManager使用
    // - GetConsumerManager() : 新ConsumerManager使用
    
    // 後方互換性維持（廃止予定警告付き）
    // - GetProducerService() : [Obsolete]警告
    // - GetConsumerService() : [Obsolete]警告
}
```

### EventSet の変更

```csharp
// Producer系：Phase1で新Manager移行完了
await events.AddAsync(entity);      // ✅ 新ProducerManager使用
await events.AddRangeAsync(list);   // ✅ バッチ最適化済み

// Consumer系：Phase1では既存実装維持、Phase2で移行予定
var results = events.ToList();      // ⚠️ 既存実装継続
var results = await events.ToListAsync(); // ⚠️ Phase2で移行予定
```

## 🛠️ 移行手順

### Step 1: 直接利用している場合

```csharp
// ❌ 廃止対象
using (var producer = new KafkaProducerService(options))
{
    await producer.SendAsync(entity, entityModel);
}

// ✅ 新実装への移行
var producerManager = serviceProvider.GetService<KafkaProducerManager>();
var typedProducer = await producerManager.GetProducerAsync<MyEntity>();
try
{
    await typedProducer.SendAsync(entity);
}
finally
{
    producerManager.ReturnProducer(typedProducer);
}
```

### Step 2: DI設定の更新

```csharp
// Program.cs または Startup.cs
services.AddScoped<EnhancedKafkaProducerManager>();
services.AddScoped<KafkaConsumerManager>(); 

// ❌ 廃止対象の削除
// services.AddScoped<KafkaProducerService>();
// services.AddScoped<KafkaConsumerService>();
```

### Step 3: 例外処理の更新

```csharp
try
{
    await producer.SendAsync(entity);
}
// ❌ 旧例外
catch (KafkaProducerException ex) 

// ✅ 新例外
catch (KafkaProducerManagerException ex)
{
    // 新例外処理
}
catch (KafkaBatchSendException ex)
{
    // バッチ送信固有の例外処理
}
```

## 📊 移行の利点

### パフォーマンス向上
- **プール管理**: Producer/Consumer の効率的な再利用
- **バッチ最適化**: `SendBatchOptimizedAsync()` による高スループット
- **型安全性**: `TypedKafkaProducer<T>` による型安全な操作

### 監視機能強化
- **メトリクス収集**: 詳細なパフォーマンス統計
- **ヘルス監視**: 自動的な健全性チェック
- **診断情報**: トラブルシューティング支援

### 開発体験改善
- **型安全API**: コンパイル時エラー検出
- **統一インターフェース**: IKafkaProducer<T>/IKafkaConsumer<T>
- **非同期ストリーム**: IAsyncEnumerable<T> 対応

## ⚠️ 注意事項

### Phase1の制限
1. **Consumer系は部分移行**: ToList/ToListAsync は Phase2で完全移行
2. **後方互換性維持**: 既存コードは警告付きで動作継続
3. **段階的削除**: 即座には削除せず、警告→エラー→削除の段階実行

### 破壊的変更
- `KafkaProducerService` のコンストラクタは `NotSupportedException`
- `KafkaConsumerService` のコンストラクタは `NotSupportedException`
- 例外型の変更: `KafkaProducerException` → `KafkaProducerManagerException`

## 🗓️ 移行スケジュール

| Phase | 期間 | 内容 | 状態 |
|-------|------|------|------|
| Phase1 | 2025年6月 | 旧Serviceクラス廃止 | ✅ 完了 |
| Phase2 | 2025年7月 | 重複Implementation削除 | 🔄 予定 |
| Phase3 | 2025年8月 | キャッシュ統合 | 📋 予定 |
| Phase4 | 2025年9月 | Schema Registry整理 | 📋 予定 |

## 🔍 トラブルシューティング

### コンパイルエラー：CS0619
```
error CS0619: 'KafkaProducerService' is obsolete: 'KafkaProducerServiceは廃止されました...'
```
**解決**: 新しい `EnhancedKafkaProducerManager` を使用してください。

### 実行時例外：NotSupportedException
```
NotSupportedException: KafkaProducerServiceは廃止されました。
```
**解決**: DI設定を更新し、新Managerクラスを登録してください。

### 診断情報の確認
```csharp
// Phase1移行状況の確認
var context = new MyKafkaContext();
Console.WriteLine(context.GetDiagnostics());

// EventSet別の移行状況
var eventSet = context.Set<MyEntity>();
Console.WriteLine(eventSet.GetPhase1MigrationStatus());
```

## 📞 サポート

移行に関する質問や問題が発生した場合：
1. プロジェクトの Issues で報告
2. 移行ガイドの更新提案
3. 新機能の要望提案

**Phase1 完了** - 次は Phase2: 重複Implementation削除に進みます。