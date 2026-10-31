using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TrayAppDotNETCommon.XmlSourceGenerator;

// Equatable, symbol-free pipeline models. The incremental pipeline caches on these, so an edit that does not change
// any [XmlRoot] type's serializable shape skips code generation entirely; Roslyn symbols are never cached.

internal enum ValueKind
{
    String,
    Boolean,
    Int32,
    UInt32,
    Int64,
    Double,
    DateTime,
    Enum,
    Complex
}

internal enum MemberShape
{
    Element,
    Attribute,
    Array
}

/// <summary>A serializable value type. Nullable value types carry their underlying shape.</summary>
internal sealed record TypeShape(
    string TypeName,
    ValueKind Kind,
    bool IsValueType,
    bool IsNullableReference,
    bool IsFlags,
    TypeShape? NullableUnderlying)
{
    public bool IsNullableValue => NullableUnderlying != null;
    public bool IsComplex => Kind == ValueKind.Complex && !IsNullableValue;
}

internal sealed record MemberModel(
    string PropertyName,
    string Name,
    string ItemName,
    MemberShape Shape,
    TypeShape Type,
    TypeShape? ItemType)
{
    public bool IsCollection => ItemType != null;
}

internal sealed record TypeModel(
    string TypeName,
    string RootName,
    bool IsDisposable,
    EquatableArray<MemberModel> Members);

internal sealed record LocationModel(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationModel? From(Location? location) =>
        location is { IsInSource: true }
            ? new LocationModel(
                location.SourceTree!.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span)
            : null;
}

internal sealed record DiagnosticModel(string PropertyDisplay, string TypeDisplay, LocationModel? Location);

/// <summary>One <c>[XmlRoot]</c> type with every type reachable from it, in discovery order (root first).</summary>
internal sealed record RootModel(
    string TypeName,
    EquatableArray<TypeModel> Types,
    EquatableArray<DiagnosticModel> Diagnostics);

/// <summary>Immutable array with value equality, so pipeline records compare by content.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    public int Count => Items.Length;

    public bool Equals(EquatableArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (T item in Items) hash.Add(item);
        return hash.ToHashCode();
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
