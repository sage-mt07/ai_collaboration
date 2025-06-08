# Ksql.EntityFrameworkCore

A C#-based DSL for generating KSQL queries from LINQ expression trees.  
Inspired by Entity Framework, tailored for Apache Kafka + ksqlDB integration.

---

## 🌟 Project Purpose

This library allows developers to:

- Use familiar C# expression trees to construct KSQL queries
- Abstract away the syntax complexity of KSQL
- Support `JOIN`, `WINDOW`, `GROUP BY`, `HAVING`, and other core KSQL clauses
- Focus on business logic, not query syntax

---

## 📁 Project Structure

```
/src
  └── Ksql.EntityFrameworkCore        →  実装コード（今後、namespaceとディレクトリ構成を一致させる方針にすると保守性が向上）
/tests
  └── Ksql.Tests                      → Unit tests for LINQ → KSQL conversion
/claude_inputs                        → Design specs and prompts for Claude
  ├── specs/                          → Claudeの振る舞い/全体設計
  ├── tasks/                          → タスクごとの指示
  └── insights/                       → Naruse・Amagi・鏡花の考察
/claude_outputs                       → Claude-generated code (for manual review)
```

---

## ⚙️ Core Design Principles

- **Expression Tree Driven**  
  DSL relies entirely on `Expression<Func<...>>` inputs for transformation.

- **Composable Builders**  
  Each clause (e.g., `JOIN`, `WHERE`, `SELECT`, `HAVING`) is handled by a dedicated builder class.

- **Testable by Design**  
  Tests follow the pattern:  
  `Expression → KSQL string`  
  Example:  
  ```csharp
  Expression<Func<IGrouping<string, Order>, object>> expr = g => new { Total = g.Sum(x => x.Amount) };
  // => SELECT SUM(Amount) AS Total
  ```

---

## 🤖 Claude Usage Guidelines

Claude is expected to:

1. Read design intent from this README and `claude_inputs/*.md`
2. Generate or improve builder code inside `/src`
3. Follow naming and formatting consistent with existing builder classes
4. Output new code in `/claude_outputs`, without modifying project files directly

Claude does **not**:
- Push changes to GitHub
- Run or validate tests
- Execute code (design-time only)

---

## ✅ Example Claude Prompt (used in /claude_inputs)

```
Implement a `KsqlHavingBuilder` that transforms:
g => g.Sum(x => x.Amount) > 1000
into:
HAVING SUM(Amount) > 1000

Make it expression-tree based and follow the same builder pattern as `KsqlWhereBuilder`.
```

---

## 🔁 Human-AI Collaboration Flow

1. Design → Documented by ChatGPT ("天城") into `claude_inputs/`
2. Implementation → Proposed by Claude
3. Review & Integration → Performed in VSCode with GitHub + Copilot
4. Feedback → Iterated via ChatGPT and Claude

---

## 📌 Notes

- DSL is intended for compile-time query generation only.
- No runtime interpretation or reflection should be used.
- Precision-sensitive types (e.g., `decimal`) and time zones (e.g., `DateTimeOffset`) must retain schema fidelity.

---
