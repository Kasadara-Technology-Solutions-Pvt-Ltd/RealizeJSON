using System;
using System.Collections.Generic;

namespace RealizeJSON.Core.Contracts;

/// <summary>Host-independent identity of a CLR type as seen by the analyzer.</summary>
/// <param name="DisplayName">Short C# display name, e.g. <c>CreateDTO&lt;CaseDTO&gt;</c>.</param>
/// <param name="FullName">Fully qualified C# display name.</param>
/// <param name="Kind">class, record, struct, record struct, interface, enum, array, …</param>
/// <param name="AssemblyName">Containing assembly name, if any.</param>
/// <param name="IsValueType">True for structs, enums and primitives that are value types.</param>
public sealed record TypeIdentity(string DisplayName, string FullName, string Kind, string? AssemblyName, bool IsValueType)
{
    /// <summary>Generic type arguments (empty for non-generic types).</summary>
    public IReadOnlyList<TypeIdentity> TypeArguments { get; init; } = Array.Empty<TypeIdentity>();

    public override string ToString() => FullName;
}
