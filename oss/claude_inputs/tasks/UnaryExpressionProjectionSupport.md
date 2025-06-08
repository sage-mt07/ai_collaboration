# 🛠 UnaryExpressionProjectionSupport.md

## 🎯 タスク概要

KsqlProjectionBuilder において、`UnaryExpression`（特に `Convert` 演算）を適切に処理できるよう拡張してください。これは、匿名型を `object` にキャストする C# の自然な動作により発生します。

## ✅ 背景

LINQ構文において `Select()` の戻り値が `object` 型の場合、式ツリー上では `UnaryExpression (Convert)` が挿入されます。例えば：

```csharp
Expression<Func<OrderEntity, object>> expr = o => new { o.Id, o.Name };
```

これは次のように式ツリーとして表現されます：

```
UnaryExpression (Convert)
└── NewExpression (new { o.Id, o.Name })
```

現在の KsqlProjectionBuilder は `VisitUnary` を実装していないため、このような構文が処理されず、出力に含まれない可能性があります。

## 🧩 仕様

- 対象：KsqlProjectionBuilder.cs
- メソッド追加：`protected override Expression VisitUnary(UnaryExpression node)`
- 実装内容：
  - `node.Operand` を再帰的に訪問することで `Convert` をスキップ
  - 他の演算子（`Not`, `Negate` など）には対応しなくてよい（KSQLの文脈上不要）

## 🔧 例（疑似コード）

```csharp
protected override Expression VisitUnary(UnaryExpression node)
{
    // objectへの変換はそのまま中身を処理
    return Visit(node.Operand);
}
```

## ✅ 期待されるテスト通過例

```csharp
Expression<Func<OrderEntity, object>> expr = o => new { o.Id, o.CustomerName };
```

出力：

```sql
SELECT Id, CustomerName
```

---

## 📝 メモ

この対応は KSQL における SELECT 句の表現を拡張し、匿名型に対するキャストを安全に処理できるようにするためのものです。
