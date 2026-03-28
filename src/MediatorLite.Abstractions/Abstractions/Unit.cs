namespace MediatorLite;

/// <summary>
/// Represents an empty response for requests that don't return a value.
/// Similar to <see cref="void"/> but usable as a generic type parameter.
/// </summary>
/// <remarks>
/// Use <see cref="Value"/> to get the singleton instance.
/// </remarks>
public readonly record struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    /// <summary>
    /// Gets the singleton <see cref="Unit"/> value.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// Returns a completed <see cref="ValueTask{Unit}"/> with the default <see cref="Unit"/> value.
    /// </summary>
    public static ValueTask<Unit> CompletedTask { get; } = ValueTask.FromResult(Value);

    /// <summary>
    /// Compares this instance with another <see cref="Unit"/> instance.
    /// All <see cref="Unit"/> instances are considered equal.
    /// </summary>
    /// <param name="other">The other <see cref="Unit"/> instance.</param>
    /// <returns>Always returns 0 since all instances are equal.</returns>
    public int CompareTo(Unit other) => 0;

    /// <summary>
    /// Returns a string representation of this <see cref="Unit"/> instance.
    /// </summary>
    /// <returns>The string "()".</returns>
    public override string ToString() => "()";
}
