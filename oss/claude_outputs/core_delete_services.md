# Core層サービスクラス削除リスト

## 削除対象ファイル

### 1. CoreIntegrationService関連
- ✅ `src/Core/Services/CoreIntegrationService.cs` - Serialization側でキャッシュされるため不要
- ✅ `src/Core/Services/ICoreIntegrationService.cs` - 上記インターフェース
- ✅ `src/Core/IModelBindingService.cs` - ModelBuilderで代替

### 削除理由
- `ValidateEntityAsync<T>()` → `CoreEntityValidator`直接呼び出しで代替
- `GetEntityModelAsync<T>()` → EntityModelキャッシュはSerialization側で実施
- `GetHealthReportAsync()` → 不要
- `GetDiagnostics()` → 不要

### 2. Integration層関連
- ✅ `src/Core/Integration/LayerIntegrationBridge.cs` - 中途半端な統合チェック、UnitTestで代替
- ✅ `src/Core/Integration/LayerIntegrationReport.cs` - 上記で使用されるレポートクラス
- ✅ `src/Core/Integration/ILayerIntegrationBridge.cs` - 上記インターフェース

### 削除理由（Integration層）
- メトリクス → Confluent.Kafka側に委譲
- ヘルスチェック → 初回起動時接続確認のみ（失敗=終了）
- 開発デバッグ → 個別UnitTestで十分
- アセンブリ存在チェックは実用的価値が低い

### 3. Pool関連の残存参照
- ✅ `src/Core/CoreDependencyConfiguration.cs` - `typeof(IPoolManager<,>)` 参照を削除
- 🔍 `IPoolManager` インターフェース定義の確認が必要
- 🔍 Pool関連のusing文、コメントの確認が必要

### 4. 重複・冗長クラス
- ✅ `src/Core/Abstractions/CoreDiagnostics.cs` - 単純なプロパティ集約、診断機能不要
- ✅ `src/Core/Abstractions/CoreHealthReport.cs` - ヘルスレポート不要
- ✅ `src/Core/Abstractions/CoreHealthStatus.cs` - 上記で使用される列挙型
- ✅ `src/Core/Abstractions/CoreSerializationStatistics.cs` - 統計取得は活用意味なし

### 削除理由（重複・冗長）
- 診断機能 → 不要と判断済み
- ヘルスレポート → 初回接続確認のみで十分  
- 統計機能 → 取っても活用する意味がない
- 単純なプロパティ集約 → Dictionary<string,object>で代替可能

### 5. 他層移譲済み機能
- ✅ `src/Core/Abstractions/ICacheStatistics.cs` - キャッシュ統計、Serialization層で実装済み
- ✅ `src/Core/Abstractions/IHealthMonitor.cs` - ヘルス監視、Monitoring層に移譲済み
- ✅ `src/Core/Abstractions/IHealthMonitor.cs`内の関連クラス:
  - `HealthCheckResult`
  - `HealthStatus` 列挙型
  - `HealthLevel` 列挙型  
  - `HealthStateChangedEventArgs`

### 削除理由（他層移譲済み）
- キャッシュ統計 → Serialization層で実装
- ヘルス監視 → Monitoring層に移譲、Core層は初回接続確認のみ
- 詳細なヘルスチェック機能は不要

### 6. 使用されていない/重複クラス（OSS設計ポリシー準拠）
- ✅ `src/Core/Context/ModelBinding.cs` - ModelBuilder一本化、属性主導設計に統合
- ✅ `src/Core/Factories/CoreEntityFactory.cs` - Factory/Bindingパターン除外、OSS本体からは削除
- ✅ `src/Core/Factories/ICoreEntityFactory.cs` - 上記インターフェース

### 削除理由（OSS設計ポリシー）
- **属性主導・上書き禁止** → ModelBuilder一本化で実現
- **Factory/Bindingパターン** → OSS本体から除外（テスト/CLI用は別途）
- **設定抽出・サマリー** → ModelBuilder拡張で対応
- **重複排除** → ModelBuilder.Event<T>()のみ残存

## 確認完了カテゴリ
✅ CoreIntegrationService関連  
✅ Integration層関連  
✅ Pool参照  
✅ 重複・冗長クラス  
✅ 他層移譲済み機能  
✅ Factory/Bindingパターン（OSS除外）
