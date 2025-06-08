
## 🧠 Stream vs Table Inference Logic (Extended)

Claude should infer the target KSQL entity type (STREAM or TABLE) based on the following rules, unless explicitly overridden via metadata:

| LINQ Pattern Includes...               | Type          |
|----------------------------------------|---------------|
| `.Where()`, `.Select()` only           | STREAM        |
| `.GroupBy()`, `.Sum()`, `.Count()`     | TABLE         |
| `.Window()`, `.Aggregate()`            | TABLE         |
| `.AsStream()`                          | FORCE STREAM  |
| `.AsTable()`                           | FORCE TABLE   |

These heuristics are used when metadata annotations are not present or incomplete. Claude should prioritize explicit markers but fall back to pattern-based inference when needed.

This behavior must be reflected in the `KsqlCreateStatementBuilder` and covered by unit tests that validate correct inference for various DSL expressions.
