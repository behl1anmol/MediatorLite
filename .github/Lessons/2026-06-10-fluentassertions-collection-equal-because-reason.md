# Lesson: FluentAssertions `Equal(...)` Eats the `because` Reason on String Collections

## Metadata
- PatternId: fluentassertions-collection-equal-because
- PatternVersion: 1
- Status: active
- Supersedes:
- CreatedAt: 2026-06-10
- LastValidatedAt: 2026-06-10
- ValidationEvidence: `dotnet test --filter NotificationTests` went 15/17 → 17/17 after rewriting `Equal("h1","h2","reason")` as `Equal(new[]{"h1","h2"}, "reason")`; full suite `dotnet test MediatorLite.sln` 78/78, build 0 warnings.

## Task Context
- Triggering task: Add start-phase / await-phase tests for parallel notification dispatch.
- Date/time: 2026-06-10
- Impacted area: tests/MediatorLite.Tests/SourceGeneration/NotificationTests.cs

## Mistake
- What went wrong: Wrote `collection.Should().Equal("h1", "h2", "the start phase invokes every handler before awaiting any")`, intending the third string as the FluentAssertions `because` argument.
- Expected behavior: Assert the collection equals `["h1", "h2"]` and attach the reason to any failure message.
- Actual behavior: The call bound to `StringCollectionAssertions.Equal(params string[] expected)`. The reason string became a **third expected element**, so the assertion expected a 3-item collection and failed — `Expected ... to be equal to {"h1", "h2", "the start phase..."}, but {"h1", "h2"} contains 1 item(s) less` — even though the phase logic under test was correct.

## Root Cause Analysis
- Primary cause: The `Equal(params string[])` convenience overload shadows `Equal(IEnumerable<string> expected, string because, params object[] becauseArgs)` when every argument is a `string`. Overload resolution prefers the params form, so the `because` parameter is never reached.
- Contributing factors: When the collection element type is `string` (or `object`), the reason text is indistinguishable from data at the call site — there is no compile error; both overloads are valid.
- Detection gap: Only surfaced at test-run time. A green-looking assertion shape hid a wrong overload binding.

## Resolution
- Fix implemented: Pass the expected sequence as an explicit array so the params overload no longer matches — `Equal(new[] { "h1", "h2" }, "reason")` binds the `(IEnumerable<string>, string, params object[])` overload and the reason lands on `because`. For membership, used `.Contain("h1").And.Contain("h2")` instead of `Contain(new[]{...})`.
- Why this fix works: A `string[]` first argument plus a separate `string` second argument cannot collapse into a single `params string[]`, forcing the reason-carrying overload.
- Verification performed: Filtered `NotificationTests` 17/17, full suite 78/78, `dotnet build` 0 warnings (warnings-as-errors).

## Preventive Actions
- Guardrails added: For any FluentAssertions **collection** assertion whose element type is `string`/`object`, never pass the `because` as a trailing bare argument. Wrap the expected items in an explicit array/collection first (`Equal(new[]{...}, "reason")`), or drop the reason.
- Tests/checks added: None at the unit level — this is an assertion-authoring style concern, not a product path.
- Process updates: Treat the failure signature `Expected ... to be equal to {…, <your reason text>}, but … contains 1 item(s) less` as the unmistakable tell of this overload trap.

## Reuse Guidance
- Applies to every string/object collection assertion in this xUnit + FluentAssertions suite (`Equal`, and any other method exposing a `params <element>[]` convenience overload alongside a `because` overload — e.g. `ContainInOrder`, `StartWith`, `EndWith`).
- If a collection equality assertion fails by exactly one extra expected item that happens to be your human-readable reason, you hit this — switch to the explicit-array form.
