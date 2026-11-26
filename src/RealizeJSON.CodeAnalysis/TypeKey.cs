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

    public override string ToString()
    {
        if (Element is not null)
        {
            return Element + "[" + new string(',', ArrayRank - 1) + "]";
        }

        var sb = new StringBuilder(MetadataName);
        if (TypeArguments.Count > 0)
        {
            sb.Append('[').Append(string.Join(",", TypeArguments.Select(a => a.ToString()))).Append(']');
        }

        if (AssemblyName is not null)
        {
            sb.Append(", ").Append(AssemblyName);
        }

        return sb.ToString();
    }

    public bool Equals(TypeKey? other) => other is not null && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TypeKey);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

}
