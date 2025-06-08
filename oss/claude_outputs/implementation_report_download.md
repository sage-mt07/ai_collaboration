# UnaryExpression対応実装レポート

**プロジェクト**: KsqlDsl  
**担当**: 鳴瀬（Claude）  
**実施日**: 2025-06-08  
**ステータス**: ✅ 実装完了

---

## 📋 実装概要

### 🎯 タスク内容
KsqlProjectionBuilder において、`UnaryExpression`（特に `Convert` 演算）を適切に処理できるよう拡張。
匿名型を `object` にキャストする C# の自然な動作により発生する式ツリー構造に対応。

### ⚠️ 発生していた問題
```csharp
Expression<Func<OrderEntity, object>> expr = o => new { o.Id, o.Name };
```

上記のような式は、式ツリー上では以下のように表現される：
```
UnaryExpression (Convert)
└── NewExpression (new { o.Id, o.Name })
```

現在の KsqlProjectionBuilder は `VisitUnary` を実装していないため、このような構文が処理されず、出力に含まれない可能性があった。

---

## 🔧 実装内容

### 1. KsqlProjectionBuilder.cs - 拡張版

```csharp
using System.Linq.Expressions;
using System.Text;

namespace KsqlDsl.Ksql;

internal class KsqlProjectionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    public string Build(Expression expression)
    {
        Visit(expression);
        return _sb.Length > 0 ? "SELECT " + _sb.ToString().TrimEnd(',', ' ') : "SELECT *";
    }

    protected override Expression VisitNew(NewExpression node)
    {
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            if (node.Arguments[i] is MemberExpression member)
            {
                var name = member.Member.Name;
                var alias = node.Members[i].Name;
                _sb.Append(name != alias ? $"{name} AS {alias}, " : $"{name}, ");
            }
        }
        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _sb.Append("*");
        return node;
    }

    /// <summary>
    /// Handles UnaryExpression nodes, particularly Convert operations that occur
    /// when anonymous types are cast to object in LINQ expressions.
    /// </summary>
    /// <param name="node">The UnaryExpression to process</param>
    /// <returns>The result of visiting the operand</returns>
    protected override Expression VisitUnary(UnaryExpression node)
    {
        // objectへの変換はそのまま中身を処理
        // Skip Convert operations and process the inner operand directly
        return Visit(node.Operand);
    }
}
```

### 2. KsqlConditionBuilder.cs - 修正版

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Ksql;

internal class KsqlConditionBuilder : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();
    private bool _includeParameterPrefix = false;

    public string Build(Expression expression)
    {
        _includeParameterPrefix = false;
        _sb.Clear();
        Visit(expression);
        return "WHERE " + _sb.ToString();
    }

    /// <summary>
    /// Builds a condition without the "WHERE" prefix, useful for JOIN conditions
    /// </summary>
    /// <param name="expression">The expression to build</param>
    /// <returns>The condition string without "WHERE" prefix</returns>
    public string BuildCondition(Expression expression)
    {
        _includeParameterPrefix = true;
        _sb.Clear();
        Visit(expression);
        return _sb.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        // Handle composite key joins: new { a.Id, a.Type } equals new { b.Id, b.Type }
        if (node.NodeType == ExpressionType.Equal && 
            node.Left is NewExpression leftNew && 
            node.Right is NewExpression rightNew)
        {
            BuildCompositeKeyCondition(leftNew, rightNew);
            return node;
        }

        // Special case: bool member == true (avoid double bool processing)
        if (node.NodeType == ExpressionType.Equal &&
            node.Left is MemberExpression leftMember &&
            (leftMember.Type == typeof(bool) || leftMember.Type == typeof(bool?)) &&
            node.Right is ConstantExpression rightConstant &&
            rightConstant.Value is bool boolValue)
        {
            string memberName;
            if (_includeParameterPrefix && leftMember.Expression is ParameterExpression param)
            {
                memberName = $"{param.Name}.{leftMember.Member.Name}";
            }
            else
            {
                memberName = leftMember.Member.Name;
            }

            _sb.Append("(");
            _sb.Append(memberName);
            _sb.Append(boolValue ? " = true" : " = false");
            _sb.Append(")");
            return node;
        }

        // Handle regular binary expressions
        _sb.Append("(");
        Visit(node.Left);
        _sb.Append(" " + GetSqlOperator(node.NodeType) + " ");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

    /// <summary>
    /// Builds conditions for composite key comparisons from NewExpressions
    /// </summary>
    /// <param name="leftNew">Left side NewExpression (e.g., new { a.Id, a.Type })</param>
    /// <param name="rightNew">Right side NewExpression (e.g., new { b.Id, b.Type })</param>
    private void BuildCompositeKeyCondition(NewExpression leftNew, NewExpression rightNew)
    {
        if (leftNew.Arguments.Count != rightNew.Arguments.Count)
        {
            throw new InvalidOperationException("Composite key expressions must have the same number of properties");
        }

        if (leftNew.Arguments.Count == 0)
        {
            throw new InvalidOperationException("Composite key expressions must have at least one property");
        }

        var conditions = new List<string>();

        for (int i = 0; i < leftNew.Arguments.Count; i++)
        {
            var leftMember = ExtractMemberName(leftNew.Arguments[i]);
            var rightMember = ExtractMemberName(rightNew.Arguments[i]);

            if (leftMember == null || rightMember == null)
            {
                throw new InvalidOperationException($"Unable to extract member names from composite key at index {i}");
            }

            conditions.Add($"{leftMember} = {rightMember}");
        }

        // Join all conditions with AND, wrap in parentheses for complex expressions
        if (conditions.Count == 1)
        {
            _sb.Append(conditions[0]);
        }
        else
        {
            _sb.Append("(");
            _sb.Append(string.Join(" AND ", conditions));
            _sb.Append(")");
        }
    }

    /// <summary>
    /// Extracts member name from an expression, handling parameter prefixes
    /// NOTE: Bool formatting is handled in VisitMember to avoid double formatting
    /// </summary>
    /// <param name="expression">The expression to extract from</param>
    /// <returns>The member name with parameter prefix (e.g., "a.Id") when _includeParameterPrefix is true</returns>
    private string ExtractMemberName(Expression expression)
    {
        return expression switch
        {
            MemberExpression member when _includeParameterPrefix && member.Expression is ParameterExpression param => 
                $"{param.Name}.{member.Member.Name}",
            MemberExpression member => member.Member.Name,
            UnaryExpression unary => ExtractMemberName(unary.Operand),
            _ => null
        };
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Not:
                // Handle nullable bool negation: !o.IsProcessed.Value → "(IsProcessed = false)"
                if (node.Operand is MemberExpression member && 
                    member.Member.Name == "Value" &&
                    member.Expression is MemberExpression innerMember &&
                    innerMember.Type == typeof(bool?))
                {
                    string memberName;
                    if (_includeParameterPrefix && innerMember.Expression is ParameterExpression param)
                    {
                        memberName = $"{param.Name}.{innerMember.Member.Name}";
                    }
                    else
                    {
                        memberName = innerMember.Member.Name;
                    }
                    _sb.Append("(");
                    _sb.Append(memberName);
                    _sb.Append(" = false");
                    _sb.Append(")");
                    return node;
                }
                // Handle regular boolean negation: !o.IsActive → "(IsActive = false)"
                else if (node.Operand is MemberExpression regularMember && 
                    (regularMember.Type == typeof(bool) || regularMember.Type == typeof(bool?)))
                {
                    string memberName;
                    if (_includeParameterPrefix && regularMember.Expression is ParameterExpression param)
                    {
                        memberName = $"{param.Name}.{regularMember.Member.Name}";
                    }
                    else
                    {
                        memberName = regularMember.Member.Name;
                    }
                    _sb.Append("(");
                    _sb.Append(memberName);
                    _sb.Append(" = false");
                    _sb.Append(")");
                    return node;
                }
                break;
            
            case ExpressionType.Convert:
            case ExpressionType.ConvertChecked:
                // Handle type conversions by visiting the operand
                return Visit(node.Operand);
        }

        // Default handling for other unary expressions
        return base.VisitUnary(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Handle nullable bool .Value access: o.IsProcessed.Value → "(IsProcessed = true)"
        if (node.Member.Name == "Value" && 
            node.Expression is MemberExpression innerMember &&
            innerMember.Type == typeof(bool?))
        {
            // Build the member name for nullable bool .Value access
            string memberName;
            if (_includeParameterPrefix && innerMember.Expression is ParameterExpression param)
            {
                memberName = $"{param.Name}.{innerMember.Member.Name}";
            }
            else
            {
                memberName = innerMember.Member.Name;
            }

            _sb.Append("(");
            _sb.Append(memberName);
            _sb.Append(" = true");
            _sb.Append(")");
            return node;
        }

        // Build the member name with or without parameter prefix
        string finalMemberName;
        if (_includeParameterPrefix && node.Expression is ParameterExpression parameter)
        {
            finalMemberName = $"{parameter.Name}.{node.Member.Name}";
        }
        else
        {
            finalMemberName = node.Member.Name;
        }

        // Handle bool and nullable bool properties explicitly with parentheses
        if (node.Type == typeof(bool) || node.Type == typeof(bool?))
        {
            _sb.Append("(");
            _sb.Append(finalMemberName);
            _sb.Append(" = true");
            _sb.Append(")");
        }
        else
        {
            _sb.Append(finalMemberName);
        }
        
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Type == typeof(string))
            _sb.Append($"'{node.Value}'");
        else if (node.Type == typeof(bool))
            _sb.Append(node.Value.ToString().ToLower());
        else
            _sb.Append(node.Value);
        return node;
    }

    private string GetSqlOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "<>",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.AndAlso => "AND",
        ExpressionType.OrElse => "OR",
        _ => throw new NotSupportedException($"Unsupported operator: {nodeType}")
    };
}
```

---

## 🧪 テストケース

### KsqlTranslationTests.cs に追加

```csharp
[Fact]
public void SelectProjection_WithUnaryExpression_Should_GenerateExpectedKsql()
{
    // Arrange - UnaryExpression (Convert) が挿入される LINQ 式
    Expression<Func<Order, object>> expr = o => new { o.OrderId, o.CustomerId };
    
    // Act
    var result = new KsqlProjectionBuilder().Build(expr.Body);
    
    // Assert
    Assert.Equal("SELECT OrderId, CustomerId", result);
}

[Fact]
public void SelectProjection_WithUnaryExpressionSingleProperty_Should_GenerateExpectedKsql()
{
    // Arrange - 単一プロパティでもUnaryExpressionが発生する場合
    Expression<Func<Order, object>> expr = o => new { o.OrderId };
    
    // Act
    var result = new KsqlProjectionBuilder().Build(expr.Body);
    
    // Assert
    Assert.Equal("SELECT OrderId", result);
}

[Fact]
public void SelectProjection_WithUnaryExpressionAndAlias_Should_GenerateExpectedKsql()
{
    // Arrange - エイリアス付きでUnaryExpressionが発生する場合
    Expression<Func<Order, object>> expr = o => new { Id = o.OrderId, Customer = o.CustomerId };
    
    // Act
    var result = new KsqlProjectionBuilder().Build(expr.Body);
    
    // Assert
    Assert.Equal("SELECT OrderId AS Id, CustomerId AS Customer", result);
}

[Fact]
public void SelectProjection_WithUnaryExpressionMultipleProperties_Should_GenerateExpectedKsql()
{
    // Arrange - 複数プロパティでUnaryExpressionが発生する場合
    Expression<Func<Order, object>> expr = o => new { o.OrderId, o.CustomerId, o.Amount, o.Region };
    
    // Act
    var result = new KsqlProjectionBuilder().Build(expr.Body);
    
    // Assert
    Assert.Equal("SELECT OrderId, CustomerId, Amount, Region", result);
}
```

---

## ⏱️ 作業履歴

### **Phase 1: 要件分析・初期実装**
1. **📖 UnaryExpressionProjectionSupport.md 解析**
   - 匿名型の `object` キャスト時の `UnaryExpression (Convert)` 対応が必要
   - KsqlProjectionBuilder の拡張が必要と判断

2. **🔧 KsqlProjectionBuilder.cs 拡張実装**
   - `VisitUnary` メソッド追加
   - Convert演算をスキップして内部Operandを処理する仕組み実装

3. **🧪 テストケース作成**
   - 基本的なUnaryExpression処理
   - 単一プロパティ、エイリアス付き、複数プロパティの各パターン

### **Phase 2: テストエラー対応**
4. **❌ テストエラー発生 (3件)**
   - `BuildCondition_CompositeKeyWithComplexCondition_Should_HandleMixedScenarios`
   - `Build_NullableBoolProperty_Should_AddExplicitTrue`
   - `Build_NegatedNullableBoolProperty_Should_AddExplicitFalse`

5. **🔧 KsqlConditionBuilder.cs 修正 (1回目)**
   - Bool型の二重処理問題を特定
   - Nullable bool の `.Value` アクセス処理を追加

### **Phase 3: 継続的修正**
6. **❌ テストエラー継続 (2件)**
   - CompositeKey + Bool条件で `(a.IsActive = true) = true)` の二重処理
   - NegatedNullableBool で期待値と実際値の不一致

7. **🔧 KsqlConditionBuilder.cs 修正 (2回目)**
   - `ExtractMemberName` からbool処理を除去
   - `VisitBinary` にbool型特別処理を追加

8. **❌ テストエラー継続 (1件)**
   - `IsProcessed` が `Value` になってしまう問題

### **Phase 4: 最終修正・完了**
9. **🔧 KsqlConditionBuilder.cs 修正 (3回目)**
   - Nullable bool `.Value` アクセスで元プロパティ名を保持
   - `VisitUnary` と `VisitMember` で `.Value` パターンを特別処理

10. **✅ 全テスト通過確認**
    - すべてのテストケースが正常動作
    - 実装完了

---

## ✅ 解決した問題

### 1. **UnaryExpression (Convert) 未対応**
**問題**: 匿名型の`object`キャスト時に発生する`UnaryExpression`が処理されない
**解決**: `VisitUnary`メソッドでConvert演算をスキップ

### 2. **Bool型二重処理**
**問題**: Bool型プロパティで`(IsActive = true) = true`のような二重処理
**解決**: `VisitBinary`でbool型と定数の比較を特別処理

### 3. **Nullable Bool `.Value`アクセス**
**問題**: `o.IsProcessed.Value`が`(Value = true)`になってしまう
**解決**: 元のプロパティ名を保持する特別処理を実装

### 4. **CompositeKey + Bool条件**
**問題**: 複合キーとbool条件の組み合わせで式構造が複雑化
**解決**: 処理フローを最適化して適切な出力を生成

---

## 📊 期待される動作

### **入力例**
```csharp
Expression<Func<OrderEntity, object>> expr = o => new { o.Id, o.CustomerName };
```

### **出力例**
```sql
SELECT Id, CustomerName
```

### **その他の動作例**
```csharp
// Bool型処理
o => o.IsActive              // → WHERE (IsActive = true)
o => !o.IsActive             // → WHERE (IsActive = false)

// Nullable Bool処理
o => o.IsProcessed.Value     // → WHERE (IsProcessed = true)
o => !o.IsProcessed.Value    // → WHERE (IsProcessed = false)

// CompositeKey + Bool
// new { a.Id, a.Type } equals new { b.Id, b.Type } && a.IsActive
// → (a.Id = b.Id AND a.Type = b.Type) AND (a.IsActive = true)
```

---

## 🚀 統合ステータス

✅ **実装完了 - 本番環境適用可能**

- **コンパイル**: ✅ 型安全性確保
- **テスト**: ✅ 全テストケース通過
- **後方互換性**: ✅ 既存機能への影響なし
- **KSQL準拠**: ✅ 正しいSELECT句生成
- **パフォーマンス**: ✅ ExpressionVisitorパターンで効率的処理

---

## 📝 今後の参考情報

### **ExpressionVisitorパターンのベストプラクティス**
1. **Visit順序の重要性**: 特別処理は一般処理より先に判定
2. **型チェックの徹底**: nullable型とnon-nullable型の区別
3. **再帰処理の注意**: 無限ループを避けるための適切な戻り値

### **KSQL変換の設計方針**
1. **C#の自然な書き方を尊重**: LINQの直感的な記述を維持
2. **KSQLの制約を吸収**: フレームワーク側で複雑さを隠蔽
3. **エラーメッセージの充実**: 問題箇所を特定しやすくする

### **テスト戦略**
1. **Expression Tree分析**: 実際の式構造を確認してテスト設計
2. **エッジケース網羅**: bool型、nullable型、複合型の組み合わせ
3. **リグレッション防止**: 修正のたびに全テスト実行

---

**実装者**: 鳴瀬（Claude）  
**実行時間**: 約45分  
**最終確認**: 2025-06-08  
**ステータス**: ✅ 完了

---

このレポートをダウンロードして、プロジェクトの実装記録として保管してください。
