using System;
using System.Collections;
using System.Collections.Generic;

namespace MediatorLite.SourceGeneration;

/// <summary>
/// An immutable array with value (sequence) equality, for use in incremental-generator
/// pipeline models. Records that carry <see cref="List{T}"/> members compare those members
/// by reference, which defeats the incremental cache — every run produces "new" models and
/// forces full regeneration. Wrapping collections in this type restores structural equality
/// so unchanged models are recognized as cached.
/// </summary>
/// <typeparam name="T">The element type; must itself have value equality.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items)
    {
        _items = items;
    }

    /// <summary>Gets the number of elements. The default instance is empty.</summary>
    public int Count => _items?.Length ?? 0;

    /// <summary>Gets the element at the given index.</summary>
    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _items;
        var right = other._items;
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return (left?.Length ?? 0) == (right?.Length ?? 0);
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        // A default (null-backed) instance and an explicitly empty array compare equal via
        // Equals, so they must hash identically. Do not early-return a different constant for
        // null: fall through to the same seed the empty-loop produces, or hash-based caches
        // (and record hashing) can miss equal values — defeating the incremental caching this
        // type exists to support.
        unchecked
        {
            var hash = 17;
            if (_items is not null)
            {
                foreach (var item in _items)
                {
                    hash = (hash * 31) + (item?.GetHashCode() ?? 0);
                }
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
