using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RealizeJSON.CodeAnalysis;

/// <summary>
/// Stable, compilation-independent type identity used for pinning. Re-resolved against each new compilation
/// so that no semantic model or compilation is retained.
/// </summary>
public sealed class TypeKey : IEquatable<TypeKey>
{
    private TypeKey(string metadataName, string? assemblyName, IReadOnlyList<TypeKey> typeArguments, int arrayRank, TypeKey? element)
    {
        MetadataName = metadataName;
        AssemblyName = assemblyName;
        TypeArguments = typeArguments;
        ArrayRank = arrayRank;
        Element = element;
    }

    public string MetadataName { get; }

    public string? AssemblyName { get; }

    public IReadOnlyList<TypeKey> TypeArguments { get; }

    public int ArrayRank { get; }

    public TypeKey? Element { get; }

    public static TypeKey? Create(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                var element = Create(array.ElementType);
                return element is null ? null : new TypeKey(string.Empty, null, Array.Empty<TypeKey>(), array.Rank, element);
            case INamedTypeSymbol named when named.TypeKind != TypeKind.Error:
                var args = new List<TypeKey>();
                foreach (var arg in GetAllTypeArguments(named))
                {
                    if (arg is ITypeParameterSymbol)
                    {
                        // Open generic: key the definition only.
                        args.Clear();
                        break;
                    }

                    var key = Create(arg);
                    if (key is null)
                    {
                        return null;
                    }

                    args.Add(key);
                }

                return new TypeKey(named.OriginalDefinition.GetFullMetadataName(), named.ContainingAssembly?.Name, args, 0, null);
            default:
                return null;
        }
    }

    /// <summary>Resolves the key in a compilation; returns null when the type no longer exists.</summary>
    public ITypeSymbol? Resolve(Compilation compilation)
    {
        if (Element is not null)
        {
            var element = Element.Resolve(compilation);
            return element is null ? null : compilation.CreateArrayTypeSymbol(element, ArrayRank);
        }

        var definition = FindDefinition(compilation);
        if (definition is null)
        {
            return null;
        }

        if (TypeArguments.Count == 0)
        {
            return definition;
        }

        var args = new ITypeSymbol[TypeArguments.Count];
        for (var i = 0; i < args.Length; i++)
        {
            var resolved = TypeArguments[i].Resolve(compilation);
            if (resolved is null)
            {
                return null;
            }

            args[i] = resolved;
        }

        return ConstructNested(definition, args);
    }

    private INamedTypeSymbol? FindDefinition(Compilation compilation)
    {
        if (AssemblyName is not null && string.Equals(compilation.Assembly.Name, AssemblyName, StringComparison.Ordinal))
        {
            var own = compilation.Assembly.GetTypeByMetadataName(MetadataName);
            if (own is not null)
            {
                return own;
            }
        }

        var unique = compilation.GetTypeByMetadataName(MetadataName);
        if (unique is not null)
        {
            return unique;
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                (AssemblyName is null || string.Equals(assembly.Name, AssemblyName, StringComparison.Ordinal)))
            {
                var found = assembly.GetTypeByMetadataName(MetadataName);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static IEnumerable<ITypeSymbol> GetAllTypeArguments(INamedTypeSymbol type)
    {
        var chain = new Stack<INamedTypeSymbol>();
        for (var t = type; t is not null; t = t.ContainingType)
        {
            chain.Push(t);
        }

        return chain.SelectMany(t => t.TypeArguments);
    }

    private static INamedTypeSymbol ConstructNested(INamedTypeSymbol definition, ITypeSymbol[] args)
    {
        var chain = new Stack<INamedTypeSymbol>();
        for (var t = definition; t is not null; t = t.ContainingType)
        {
            chain.Push(t);
        }

        var index = 0;
        INamedTypeSymbol? current = null;
        foreach (var level in chain)
        {
            var def = current is null ? level : current.GetTypeMembers(level.Name, level.Arity).First();
            current = level.Arity == 0 ? def : def.Construct(args.Skip(index).Take(level.Arity).ToArray());
            index += level.Arity;
        }

        return current!;
    }

    /// <summary>
    /// Serialized form: <c>#rank:element</c> for arrays, otherwise
    /// <c>Metadata.Name`1[[arg],[arg]]@Assembly</c>. Unambiguous and parseable with <see cref="Parse"/>.
    /// </summary>
    public override string ToString()
    {
        if (Element is not null)
        {
            return "#" + ArrayRank.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Element;
        }

        var sb = new StringBuilder(MetadataName);
        if (TypeArguments.Count > 0)
        {
            sb.Append('[').Append(string.Join(",", TypeArguments.Select(a => "[" + a + "]"))).Append(']');
        }

        if (AssemblyName is not null)
        {
            sb.Append('@').Append(AssemblyName);
        }

        return sb.ToString();
    }

    /// <summary>Parses the output of <see cref="ToString"/>; returns null for malformed input.</summary>
    public static TypeKey? Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 4096)
        {
            return null;
        }

        var position = 0;
        var key = ParseKey(text, ref position, 0);
        return key is not null && position == text.Length ? key : null;
    }

    private static TypeKey? ParseKey(string text, ref int position, int depth)
    {
        if (depth > 32 || position >= text.Length)
        {
            return null;
        }

        if (text[position] == '#')
        {
            var colon = text.IndexOf(':', position);
            if (colon < 0 || !int.TryParse(text.Substring(position + 1, colon - position - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rank) || rank < 1)
            {
                return null;
            }

            position = colon + 1;
            var element = ParseKey(text, ref position, depth + 1);
            return element is null ? null : new TypeKey(string.Empty, null, Array.Empty<TypeKey>(), rank, element);
        }

        var start = position;
        while (position < text.Length && text[position] != '[' && text[position] != '@' && text[position] != ']' && text[position] != ',')
        {
            position++;
        }

        var name = text.Substring(start, position - start);
        if (name.Length == 0)
        {
            return null;
        }

        var args = new List<TypeKey>();
        if (position < text.Length && text[position] == '[')
        {
            position++;
            while (true)
            {
                if (position >= text.Length || text[position] != '[')
                {
                    return null;
                }

                position++;
                var arg = ParseKey(text, ref position, depth + 1);
                if (arg is null || position >= text.Length || text[position] != ']')
                {
                    return null;
                }

                position++;
                args.Add(arg);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }

                if (position < text.Length && text[position] == ']')
                {
                    position++;
                    break;
                }

                return null;
            }
        }

        string? assembly = null;
        if (position < text.Length && text[position] == '@')
        {
            var end = position + 1;
            while (end < text.Length && text[end] != ']' && text[end] != ',')
            {
                end++;
            }

            assembly = text.Substring(position + 1, end - position - 1);
            position = end;
        }

        return new TypeKey(name, assembly, args, 0, null);
    }

    public bool Equals(TypeKey? other) => other is not null && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TypeKey);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

}
