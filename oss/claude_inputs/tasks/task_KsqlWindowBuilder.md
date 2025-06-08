# Task: Implement KsqlWindowBuilder.cs
## Objective

Implement a new builder class `KsqlWindowBuilder` that generates appropriate KSQL syntax for Kafka stream windowing clauses...

## Requirements

- The builder should support the following Kafka window types:
  - Tumbling Window
  - Hopping Window
  - Session Window

- Each type should be configured with appropriate parameters:
  - Tumbling → `.Size(...)`
  - Hopping → `.Size(...)` and `.AdvanceBy(...)`
  - Session → `.Gap(...)`

## DSL Usage Examples

```csharp
Window.TumblingWindow().Size(TimeSpan.FromMinutes(1))
// → WINDOW TUMBLING (SIZE 1 MINUTES)

Window.HoppingWindow().Size(TimeSpan.FromMinutes(10)).AdvanceBy(TimeSpan.FromMinutes(5))
// → WINDOW HOPPING (SIZE 10 MINUTES, ADVANCE BY 5 MINUTES)

Window.SessionWindow().Gap(TimeSpan.FromSeconds(30))
// → WINDOW SESSION (GAP 30 SECONDS)
```

## Constraints

- Avoid reflection
- Ensure type-safety
- Methods must be chainable
- Output must be parseable into a single string (e.g., `Build()` method)

## Output

- `KsqlWindowBuilder.cs` in `claude_outputs`
- `WindowClauseTests.cs` with tests for all supported forms

