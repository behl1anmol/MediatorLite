using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace MediatorLite.Tests.UnitTests;

/// <summary>
/// Tests for the <see cref="Unit"/> value type, including a structural regression guard against the
/// Blazor/Mono WebAssembly type-loader crash described below.
/// </summary>
public class UnitTypeTests
{
    /// <summary>
    /// Regression guard for the Blazor WASM (Mono) crash.
    /// <para>
    /// <see cref="Unit"/> must never declare a <c>static</c> field whose type is
    /// <see cref="ValueTask{Unit}"/>. Such a self-referential static field
    /// (<c>Unit</c> statically holding a <c>ValueTask&lt;Unit&gt;</c>) makes the Mono/WASM type loader's
    /// recursion detector abort while loading <c>ValueTask&lt;Unit&gt;</c> — a native
    /// <c>object.c</c> assertion / <c>TypeLoadException: Recursive type definition detected</c> — the
    /// first time the recursive load path is hit (e.g. the generated mediator's
    /// <c>SendAsync&lt;Unit&gt;</c> dispatch). The failure is load-order dependent and does not occur on
    /// CoreCLR, which is why it can slip through non-WASM tests.
    /// </para>
    /// <para>
    /// This is why <see cref="Unit.CompletedTask"/> is an expression-bodied (computed) property rather
    /// than a get-only auto-property: an auto-property would emit exactly such a static backing field.
    /// </para>
    /// </summary>
    [Fact]
    public void Unit_HasNoStaticFieldOfTypeValueTaskOfUnit()
    {
        var offendingFields = typeof(Unit)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(ValueTask<Unit>))
            .Select(f => f.Name)
            .ToArray();

        offendingFields.Should().BeEmpty(
            "a static ValueTask<Unit> field on Unit makes ValueTask<Unit> self-referential and aborts "
            + "the Mono/WASM type loader; keep Unit.CompletedTask a computed property. Offending: {0}",
            string.Join(", ", offendingFields));
    }

    /// <summary>
    /// Confirms <see cref="Unit.CompletedTask"/> is a completed task that yields <see cref="Unit.Value"/>.
    /// </summary>
    [Fact]
    public async Task CompletedTask_IsCompleted_AndReturnsValue()
    {
        var task = Unit.CompletedTask;

        task.IsCompletedSuccessfully.Should().BeTrue("Unit.CompletedTask must be an already-completed task");
        (await task).Should().Be(Unit.Value, "the completed task must yield the singleton Unit value");
    }
}
