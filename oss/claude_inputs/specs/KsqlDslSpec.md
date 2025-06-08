
# Claude Integration Specification for Ksql.EntityFrameworkCore

This document provides task-specific guidance for Claude when working with the Ksql.EntityFrameworkCore project.
Claude should use this file as the primary reference when generating or modifying code based on provided DSL logic.

---

## 📁 Project Structure Overview

```
KsqlDsl.sln                      → Main solution file
/src
  ├── KsqlAggregateBuilder.cs    → Expression-based KSQL aggregation clause builder
  ├── KsqlConditionBuilder.cs    → WHERE clause builder
  ├── KsqlGroupByBuilder.cs      → GROUP BY clause builder
  ├── KsqlJoinBuilder.cs         → JOIN clause builder (uses LINQ Join syntax)
  ├── DateTimeFormatAttribute.cs → Custom attribute for formatting DateTime fields
  ├── DecimalPrecisionAttribute.cs → Custom attribute for decimal precision
/tests
  └── KsqlTranslationTests.cs    → Unit tests to verify LINQ → KSQL translation
```

---

## ✅ Tasks for Claude

Claude may be asked to perform the following:

- Add support for new LINQ patterns to existing builder classes
- Generate new builder classes (e.g., `KsqlHavingBuilder`)
- Refactor or optimize expression tree traversal code
- Extend test coverage in `KsqlTranslationTests.cs`

---

## ⚙️ Style & Convention Guidelines

- Follow C# naming conventions (PascalCase for classes and methods)
- Do not use reflection — rely on expression tree analysis
- Keep all logic type-safe and statically analyzable
- Keep transformation logic composable via isolated static methods
- Prefer `Expression<Func<>>` for all inputs

---

## 🧪 Testing Expectations

All builder features must be testable via `KsqlTranslationTests.cs` using the following structure:

```csharp
Expression<Func<IGrouping<string, Order>, object>> expr = g => new {
    Total = g.Sum(x => x.Amount)
};
var result = KsqlAggregateBuilder.Build(expr.Body);
Assert.Equal("SELECT SUM(Amount) AS Total", result);
```

Claude may add similar tests when implementing new functionality.

---

## 🧠 Claude Usage Notes

Claude will:

- Read builder implementations and tests from the extracted project files
- Receive task-specific prompts (in natural language or markdown)
- Generate corresponding C# code or suggestions
- Write results to `claude_outputs/*.cs` (outside of actual `/src` or `/tests` folders)

Claude will not:

- Push to GitHub
- Compile or run code
- Automatically modify the actual project files

---

## 🔁 Workflow Example

1. Human defines task in markdown or prompt (e.g., “Add HAVING clause support”)
2. Claude reads related specs from this doc + project structure
3. Claude generates builder logic and test code
4. Human copies result to `/src` and `/tests` and verifies functionality
5. Refined feedback is looped back to Claude if needed

---

## 🏷️ Naming Policy

The naming convention in this project strictly follows Kafka/KSQL terminology. It assumes DSL users are familiar with Kafka concepts, and prioritizes consistency with the official Kafka documentation.

### Windowing

| Kafka Term      | Method Name Used in DSL           |
|----------------|--------------------------------|
| Tumbling Window | TumblingWindow()              |
| Hopping Window  | HoppingWindow()               |
| Session Window  | SessionWindow()               |

- .Size(TimeSpan) → maps to SIZE clause
- .AdvanceBy(TimeSpan) → maps to ADVANCE BY clause (Hopping only)
- .Gap(TimeSpan) → used in SESSION (GAP ...)

### General Principles

- Terms and structures defined by Kafka should be used verbatim without translation or abstraction
- Internal helper classes and interfaces should also align with Kafka vocabulary as much as possible

### EMIT FINAL Caution

- EMIT FINAL will only emit output when an event arrives at the end of the window
- Windows with events that receive no activity at closing time may produce no output at all
- This behavior is expected in KSQL and should be accounted for in use cases
- Users who require guaranteed final output for every window must insert a dummy event or use EMIT CHANGES with downstream filtering


## Completed Tasks & Claude Logs
### ✅ LINQ to KSQL DSL Interpretation

- Expression tree-based translation completed
- Full clause support: SELECT, WHERE, GROUP BY, HAVING, WINDOW, JOIN
- Integrated and tested under KsqlTranslationTests.cs
- DSL expression patterns confirmed and documented

### Window Clause Full Support (by Naruse)
⏱ Estimated Active Work Time: ~1 hour 30 minutes
(excluding break, reflection, and unrelated chat)

##### Implemented Features:
- Retention(TimeSpan)
- GracePeriod(TimeSpan)
- EmitFinal()

##### Test Coverage:

- 17 test cases including option combinations and EMIT FINAL edge cases

##### Notes:

- EMIT FINAL only emits when a new event occurs after window close
- Session windows do not support retention/grace/emit final
- Default is EMIT CHANGES (implicit)

### 📁 Artifacts:

- KsqlWindowBuilder.cs (extended)
- WindowClauseTests.cs (new)
- KsqlTranslationTests.cs (updated)
- Implementation Log

### 🛡️ Quality Notes:

- Type-safe, XML documented
- Matches KSQL production semantics
- Enterprise-grade implementation

