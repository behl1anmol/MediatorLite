using System.Collections;
using FluentAssertions;
using MediatorLite.SourceGeneration;
using Xunit;

namespace MediatorLite.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="EquatableArray{T}"/> — the value-equality wrapper that keeps the
/// incremental generator's pipeline models cacheable. Its equality/hash contract is what makes
/// unchanged models compare equal across generator runs, so it is worth pinning directly.
/// </summary>
public class EquatableArrayTests
{
    [Fact]
    public void Equals_SameContent_IsTrueWithEqualHash()
    {
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([1, 2, 3]);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals((object)b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentLength_IsFalse()
    {
        var a = new EquatableArray<int>([1, 2, 3]);
        var b = new EquatableArray<int>([1, 2]);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentElement_IsFalse()
    {
        var a = new EquatableArray<string>(["x", "y"]);
        var b = new EquatableArray<string>(["x", "z"]);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_DefaultAndEmpty_AreEqualWithMatchingHashAndZeroCount()
    {
        EquatableArray<int> def = default;
        var empty = new EquatableArray<int>([]);

        def.Count.Should().Be(0);
        empty.Count.Should().Be(0);
        def.Equals(empty).Should().BeTrue();
        def.Equals(default).Should().BeTrue();
        // Equal values MUST hash equally, or hash-based caches miss them.
        def.GetHashCode().Should().Be(empty.GetHashCode());
    }

    [Fact]
    public void Equals_SameBackingReference_ShortCircuitsTrue()
    {
        var backing = new[] { 5, 6 };
        var a = new EquatableArray<int>(backing);
        var b = new EquatableArray<int>(backing);

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_NonEquatableArrayObject_IsFalse()
    {
        var a = new EquatableArray<int>([1]);

        a.Equals("not an array").Should().BeFalse();
    }

    [Fact]
    public void IndexerAndCount_ReturnElements()
    {
        var a = new EquatableArray<string>(["a", "b", "c"]);

        a.Count.Should().Be(3);
        a[0].Should().Be("a");
        a[2].Should().Be("c");
    }

    [Fact]
    public void Enumeration_YieldsAllElements_AndDefaultIsEmpty()
    {
        var a = new EquatableArray<int>([10, 20]);

        a.Should().Equal(10, 20); // IEnumerable<int> path

        var viaNonGeneric = new List<object?>();
        foreach (var item in (IEnumerable)a)
        {
            viaNonGeneric.Add(item);
        }

        viaNonGeneric.Should().Equal(10, 20);

        EquatableArray<int> def = default;
        def.Should().BeEmpty();
    }

    [Fact]
    public void GetHashCode_Default_IsStable()
    {
        EquatableArray<int> def = default;

        def.GetHashCode().Should().Be(default(EquatableArray<int>).GetHashCode());
    }
}
