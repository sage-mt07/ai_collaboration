# ValidationMode 必要性分析（設計ドキュメント基準）

## 🔍 **設計ドキュメントでのValidationMode定義**

### **厳格モード vs ゆるめ運用モード**

#### **厳格モード（デフォルト: ValidateStrict = true）**
```csharp
// 設計ドキュメントより
- [Topic]（および [Key] など）必須属性未定義時は例外で停止
- 例外例：「TradeEventクラスに[Topic]属性がありません。POCOとKafkaトピック名の1:1マッピングが必要です」
- クラス名→トピック名等の自動補完は一切行わない（明示的設計のみ許可）
- 起動時/スキーマ初期化時に必ずバリデーションを行い、不備は即時通知
```

#### **ゆるめ運用モード（ValidateStrict = false）**
```csharp
// 設計ドキュメントより
- OnConfiguringで optionsBuilder.EnableRelaxedValidation(); を呼ぶことで
  「POCO属性がなくても自動マッピングで"なんとなく動く"」
- [Topic]属性未指定→クラス名＝トピック名、PartitionCount=1等のデフォルト値で自動登録
- 起動時に「属性未定義を自動補完しています」警告メッセージを必ず表示
- 本番運用には非推奨（学習・PoC用途限定）
```

---

## 📊 **現在のValidationMode実装との対応**

### **現在のConfiguration層実装**
```csharp
// src/Configuration/Abstractions/ValidationMode.cs
public enum ValidationMode
{
    Strict,    // 厳密検証
    Relaxed    // 緩い検証（自動補完あり）
}
```

### **設計ドキュメントとの一致度**
| 要素 | 設計ドキュメント | 現在の実装 | 一致度 |
|------|------------------|-----------|-------|
| **厳格モード** | `ValidateStrict = true` | `ValidationMode.Strict` | ✅ **完全一致** |
| **ゆるめモード** | `EnableRelaxedValidation()` | `ValidationMode.Relaxed` | ✅ **完全一致** |
| **用途** | 本番 vs 学習・PoC | 同様の区分 | ✅ **完全一致** |

---

## 🎯 **設計ドキュメントでの具体的な使用例**

### **厳格モード使用例**
```csharp
// 設計ドキュメントより：デフォルト動作
[Topic("trade-events", PartitionCount = 3)]
public class TradeEvent
{
    [Key]
    public long TradeId { get; set; }
    // ... 必須属性がすべて定義済み
}

// → ValidationMode.Strict で動作
// → 属性未定義なら即座に例外停止
```

### **ゆるめモード使用例**
```csharp
// 設計ドキュメントより：学習・PoC用途
public class TradeEvent  // ← [Topic]属性なし
{
    public long TradeId { get; set; }  // ← [Key]属性なし
}

protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.EnableRelaxedValidation();  // ← ValidationMode.Relaxed相当
}

// → 警告付きで自動補完：トピック名=TradeEvent、最初のプロパティ=Key
```

---

## 🔍 **責務の所在分析**

### **ValidationModeの使用層**

#### **Core層での使用（設計ドキュメント基準）**
```csharp
// POCOエンティティの検証で使用
public class EntityModelValidator
{
    private readonly ValidationMode _mode;
    
    public ValidationResult ValidateEntity(Type entityType)
    {
        if (_mode == ValidationMode.Strict)
        {
            // [Topic], [Key]必須チェック
            if (topicAttribute == null)
                throw new Exception("TradeEventクラスに[Topic]属性がありません");
        }
        else // ValidationMode.Relaxed
        {
            // 自動補完 + 警告
            Logger.Warning("属性未定義を自動補完しています");
        }
    }
}
```

#### **Configuration層での設定提供**
```csharp
// Configuration層：設定値として提供
public record KafkaContextOptions
{
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;
}
```

---

## 💡 **Configuration層での必要性評価**

### **✅ Configuration層で管理すべき理由**

#### **1. 全体的な動作モード制御**
- **Core層**: エンティティ検証の実行
- **Messaging層**: トピック作成時の検証
- **Serialization層**: スキーマ生成時の検証
- **Configuration層**: **各層に検証モードを提供**

#### **2. 設計ドキュメントとの整合性**
```csharp
// 設計ドキュメント通りの API
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.EnableRelaxedValidation();  // ← この設定をどこで管理？
}
```

#### **3. appsettings.json での設定**
```json
{
  "KsqlDsl": {
    "ValidationMode": "Strict",
    "Kafka": { ... },
    "SchemaRegistry": { ... }
  }
}
```

### **❌ Configuration層不要論**
#### **各層で個別管理案**
```csharp
// Core層で独自定義
namespace KsqlDsl.Core;
public enum CoreValidationMode { Strict, Relaxed }

// Messaging層で独自定義  
namespace KsqlDsl.Messaging;
public enum MessagingValidationMode { Strict, Relaxed }
```

**問題点**: 同じ概念の重複定義、設定の分散化

---

## 🎯 **最終判定**

### **ValidationMode = 保持推奨**

#### **保持理由**
1. ✅ **設計ドキュメントの中核概念** - 厳格モード vs ゆるめモードは基本方針
2. ✅ **全層横断的な設定** - Core/Messaging/Serialization すべてで参照
3. ✅ **KsqlDsl独自概念** - Confluent.Kafkaにはない独自の価値
4. ✅ **実用性が高い** - 本番運用 vs 開発・学習での使い分け

#### **Configuration層での役割**
- 各層に**統一された検証モード**を提供
- appsettings.json での設定可能
- `optionsBuilder.EnableRelaxedValidation()` API の実現

#### **実装例**
```csharp
// Configuration層：設定提供
public record KafkaContextOptions
{
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;
}

// 各層：設定を受け取って動作
public class EntityValidator
{
    public EntityValidator(IOptions<KafkaContextOptions> options)
    {
        _validationMode = options.Value.ValidationMode;
    }
}
```

---

## 🗑️ **最終的なConfiguration層構成**

### **保持対象 (4ファイル) - 変更なし**
```
✅ src/Configuration/Abstractions/KafkaBusOptions.cs          - 修正要
✅ src/Configuration/Abstractions/AvroSchemaRegistryOptions.cs
✅ src/Configuration/Abstractions/ValidationMode.cs           - 保持確定
✅ src/Configuration/Abstractions/IKsqlConfigurationManager.cs - 修正要
```

### **削除対象 (17ファイル)**
```
❌ src/Configuration/Abstractions/SchemaGenerationOptions.cs
❌ src/Configuration/Abstractions/RetryOptions.cs
❌ src/Configuration/Abstractions/AutoOffsetReset.cs
❌ (その他14ファイル...)
```

**ValidationMode保持に同意いただけますでしょうか？**

設計ドキュメントを見ると、これは**KsqlDslの中核的な設計思想**（厳格 vs ゆるめ）を表現する重要な概念で、Configuration層で統一管理すべきものと判断します。