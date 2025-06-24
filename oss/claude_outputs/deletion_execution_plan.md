# 削除実行計画（即時実行）

## ⚡ **即時削除対象（重複排除）**

### 📁 **完全削除ファイル**
```bash
# スキーマ生成重複
rm src/Serialization/Avro/Core/SchemaGenerator.cs
rm src/Serialization/Avro/Core/AvroSchemaGenerator.cs

# Serializer管理重複  
rm src/Serialization/Avro/AvroSerializerManager.cs
rm src/Serialization/Avro/EnhancedAvroSerializerManager.cs

# SerializationManager重複
rm src/Serialization/Avro/AvroSerializationManager.cs

# 不要Configuration
rm -rf src/Configuration/Abstractions/

# Metrics関連（Phase3で除去済みのはず）
find src/ -name "*Metrics*" -delete
find src/ -name "*Performance*" -delete
```

### 🔧 **部分削除（クラス削除）**

#### **KafkaContext.cs内の重複クラス削除**
```csharp
// src/KafkaContext.cs - 削除対象
❌ internal class EventSetWithServices<T> : EventSet<T> { ... } // 全削除

// CreateEntitySet実装も簡素化
protected override IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel)
{
    return new EventSet<T>(this, entityModel); // シンプル化
}
```

#### **未実装参照の削除**
```csharp
// src/Application/KsqlContext.cs - 削除・無効化
❌ 削除: GetHealthReportAsync()メソッド
❌ 削除: GetStatistics()メソッド  
❌ 削除: CheckEntitySchemaCompatibilityAsync<T>()メソッド

// GlobalAvroSerializationManager参照を既存実装に置換
// 修正前:
_serializationManager = new GlobalAvroSerializationManager(...);

// 修正後:
_serializationManager = new AvroSerializationManager<object>(...);
```

---

## 🚫 **Monitoring/Tracing完全除去（初期化パス）**

### **ResilientAvroSerializerManager.cs修正**
```csharp
public async Task<int> RegisterSchemaWithRetryAsync(string subject, string schema)
{
    // ❌ 完全削除
    // using var activity = AvroActivitySource.StartSchemaRegistration(subject);
    
    var policy = _retrySettings.SchemaRegistration;
    var attempt = 1;

    while (attempt <= policy.MaxAttempts)
    {
        try
        {
            // ❌ 完全削除  
            // using var operation = AvroActivitySource.StartCacheOperation("register", subject);
            
            var stopwatch = Stopwatch.StartNew();
            var schemaObj = new Schema(schema, SchemaType.Avro);
            var schemaId = await _schemaRegistryClient.RegisterSchemaAsync(subject, schemaObj);
            stopwatch.Stop();

            // 成功ログ：回数明記
            _logger.LogInformation(
                "Schema registration SUCCESS on attempt {Attempt}: {Subject} (ID: {SchemaId}, Duration: {Duration}ms)",
                attempt, subject, schemaId, stopwatch.ElapsedMilliseconds);

            return schemaId;
        }
        catch (Exception ex) when (ShouldRetry(ex, policy, attempt))
        {
            var delay = CalculateDelay(policy, attempt);

            // 警告ログ：例外種別・タイミング・詳細明記
            _logger.LogWarning(ex,
                "RETRY ATTEMPT {Attempt}/{MaxAttempts} [{ExceptionType}] at {Timestamp}: " +
                "Schema registration failed for {Subject}. " +
                "Next retry in {Delay}ms. Exception: {ExceptionMessage}",
                attempt, policy.MaxAttempts, ex.GetType().Name, DateTime.UtcNow, 
                subject, delay.TotalMilliseconds, ex.Message);

            await Task.Delay(delay);
            attempt++;
        }
        catch (Exception ex)
        {
            // 致命的エラー：人間介入必要を明記
            _logger.LogCritical(ex,
                "FATAL ERROR: Schema registration failed permanently after {MaxAttempts} attempts for {Subject}. " +
                "HUMAN INTERVENTION REQUIRED. Application will TERMINATE. Exception: {ExceptionMessage}",
                policy.MaxAttempts, subject, ex.Message);

            throw; // 即例外throw = アプリ停止（Fail Fast）
        }
    }

    // 最大試行到達時の致命的エラー
    var fatalMessage = $"FATAL: Schema registration exhausted all {policy.MaxAttempts} attempts for {subject}. HUMAN INTERVENTION REQUIRED.";
    _logger.LogCritical(fatalMessage);
    throw new InvalidOperationException(fatalMessage);
}
```

### **その他初期化パスからのTracing除去**
```csharp
// すべての初期化・モデル検証パスから以下を完全削除:
❌ AvroActivitySource.StartXxx(...)
❌ using var activity = ...
❌ activity?.SetTag(...)
❌ activity?.SetStatus(...)

// 対象ファイル:
- AvroSchemaRegistrationService.cs  
- AvroSerializerFactory.cs
- KsqlContext.cs（初期化部分）
```

---

## 🧹 **Namespace/循環参照の解消**

### **Configuration重複の削除**
```bash
# 完全削除
rm -rf src/Configuration/Abstractions/

# 参照を既存Coreに統合
sed -i 's/KsqlDsl.Configuration.Abstractions/KsqlDsl.Core.Configuration/g' src/**/*.cs
```

### **循環参照の解消**
```csharp
// 修正前の循環:
// Core.Abstractions.TopicAttribute → Serialization.Abstractions.AvroEntityConfiguration → Core.Models.EntityModel

// 修正後:
// Core.Abstractions.TopicAttribute → Core.Models.EntityModel → Serialization.Abstractions.AvroEntityConfiguration

// AvroEntityConfiguration.csの依存削除
❌ 削除: using KsqlDsl.Core.Models;
❌ 削除: EntityModelへの直接参照
```

### **Extensions整理**
```bash
# LoggerFactoryExtensionsを移動
mkdir -p src/Infrastructure/Extensions/
mv src/Core/Extensions/LoggerFactoryExtensions.cs src/Infrastructure/Extensions/

# 参照更新
sed -i 's/KsqlDsl.Core.Extensions/KsqlDsl.Infrastructure.Extensions/g' src/**/*.cs
```

---

## 🗑️ **不要クラス・メソッドの削除**

### **KsqlContext.cs削除対象**
```csharp
❌ 削除メソッド:
- GetHealthReportAsync()
- GetStatistics()  
- CheckEntitySchemaCompatibilityAsync<T>()
- GetRegisteredSchemasAsync()（重複機能）

❌ 削除フィールド:
- _schemaRegistrationService（削除されたクラスの参照）

❌ 削除クラス:
- EventSetWithServices<T>
```

### **重複Interfaceの削除**
```csharp
// 重複・不要インターフェースの削除
❌ IAvroSchemaProvider（複数箇所で重複定義）
❌ ICacheStatistics（実装なし）
❌ IHealthMonitor（Monitoring層の責務）
```

---

## 📊 **削除対象サマリー**

### **削除ファイル数: 12+**
```
✅ SchemaGenerator.cs
✅ AvroSchemaGenerator.cs  
✅ AvroSerializerManager.cs
✅ EnhancedAvroSerializerManager.cs
✅ AvroSerializationManager.cs
✅ Configuration/Abstractions/ フォルダ全体
✅ *Metrics* 関連ファイル
✅ *Performance* 関連ファイル
```

### **削除クラス数: 8+**
```
✅ EventSetWithServices<T>
✅ 重複Interface定義
✅ 未実装Health/Statisticsメソッド
✅ 不要Extension methods
```

### **削除コード行数: 2000+**
```
✅ 重複実装: ~1500行
✅ Monitoring初期化パス: ~300行  
✅ 不要メソッド/クラス: ~200行
```

---

## ⚡ **実行チェックリスト**

### **Phase 1: 削除実行（1時間以内）**
- [ ] 重複ファイル12個の完全削除
- [ ] 重複クラス8個の部分削除
- [ ] 未実装参照の削除・無効化
- [ ] コンパイル確認

### **Phase 2: Monitoring除去（30分以内）**
- [ ] 初期化パスからActivitySource完全除去
- [ ] リトライログの詳細化  
- [ ] 致命的エラーログの強化
- [ ] コンパイル確認

### **Phase 3: 循環参照解消（30分以内）**
- [ ] Namespace統合・移動
- [ ] 循環参照の切断
- [ ] 依存関係の一方向化確認
- [ ] 最終コンパイル確認

### **Phase 4: 検証（15分以内）**
- [ ] 重複検出ツール = 0件
- [ ] 循環参照検出 = 0件
- [ ] 未実装参照 = 0件
- [ ] 基本機能テスト通過

**結果**: ファイル数20%削減、クラス数30%削減、コード行数25%削減