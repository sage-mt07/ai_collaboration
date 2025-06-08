# Task: Extend `KsqlWindowBuilder` for Full Window Clause Support

## 🧭 Objective

Enhance the existing `KsqlWindowBuilder` class to support the complete set of KSQL window clause options, including retention, grace period, and emit behavior.

## 🧱 Requirements

### 🎯 Supported Window Types

- Tumbling
- Hopping
- Session

### 🔧 New Methods to Implement

| DSL Method Signature         | KSQL Output Example                     | Applicable To            |
|-----------------------------|-----------------------------------------|---------------------------|
| `.Retention(TimeSpan)`      | `RETENTION 2 HOURS`                     | Tumbling, Hopping         |
| `.GracePeriod(TimeSpan)`    | `GRACE PERIOD 10 SECONDS`               | Tumbling, Hopping         |
| `.EmitFinal()`              | `EMIT FINAL`                            | Tumbling, Hopping         |
| (default)                   | `EMIT CHANGES` (default - implicit)     | All windows               |

### ⚠️ Notes on EMIT FINAL

- EMIT FINAL only emits when a new event arrives *after* the window ends.
- If no event occurs at window close, the final result may never be emitted.
- This behavior must be documented in the builder’s XML comments.

---

## 🔍 Examples

```csharp
Window
  .TumblingWindow()
  .Size(TimeSpan.FromMinutes(5))
  .Retention(TimeSpan.FromHours(2))
  .GracePeriod(TimeSpan.FromSeconds(10))
  .EmitFinal();
```

Produces:

```sql
WINDOW TUMBLING (SIZE 5 MINUTES, RETENTION 2 HOURS, GRACE PERIOD 10 SECONDS) EMIT FINAL
```

---

## 🧪 Testing Expectations

Add tests to `KsqlTranslationTests.cs`:

- One test per window type with optional clauses
- One test for default EMIT behavior
- One test verifying EMIT FINAL behavior edge case

---

## 📁 Output Location

Write the updated `KsqlWindowBuilder.cs` to:

```
/claude_outputs/KsqlWindowBuilder.cs
```

And test file to:

```
/claude_outputs/window_clause_tests.cs
```

---

## 📌 Naming Consistency

Use Kafka terminology for all method names. See `KsqlDslSpec.md` under “Naming Policy”.

---

## ✅ Acceptance Criteria

- All methods compile and are test-covered
- Output matches KSQL syntax precisely
- EMIT FINAL edge cases are documented and tested