using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;

namespace RealizeJSON.CodeAnalysis;

/// <summary>Roslyn symbol helpers. Attributes are always matched by fully qualified symbol identity.</summary>
public static class SymbolExtensions
{
    private static readonly SymbolDisplayFormat FullFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat ShortFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Full metadata name such as <c>System.Collections.Generic.List`1</c> or <c>Ns.Outer+Inner</c>.</summary>
    public static string GetFullMetadataName(this INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        ISymbol? current = type;
        while (current is INamedTypeSymbol named)
        {
            parts.Push(named.MetadataName);
            current = named.ContainingType ?? (ISymbol?)named.ContainingNamespace;
            if (current is INamedTypeSymbol)
            {
                parts.Push("+");
            }
        }

        var sb = new StringBuilder();
        if (current is INamespaceSymbol ns && !ns.IsGlobalNamespace)
        {
            sb.Append(ns.ToDisplayString()).Append('.');
        }

        foreach (var part in parts)
        {
            sb.Append(part);
        }

        return sb.ToString();
    }

    /// <summary>True when the type's original definition has the given full metadata name.</summary>
    public static bool Is(this ITypeSymbol? type, string fullMetadataName) =>
        type is INamedTypeSymbol named && string.Equals(named.OriginalDefinition.GetFullMetadataName(), fullMetadataName, StringComparison.Ordinal);

    public static AttributeData? FindAttribute(this ISymbol symbol, string fullMetadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { } cls && (cls.Is(fullMetadataName) || cls.OriginalDefinition.GetFullMetadataName() == fullMetadataName))
            {
                return attribute;
            }
        }

        return null;
    }

    public static IEnumerable<AttributeData> FindAttributes(this ISymbol symbol, string fullMetadataName) =>
        symbol.GetAttributes().Where(a => a.AttributeClass is { } cls && cls.OriginalDefinition.GetFullMetadataName() == fullMetadataName);

    /// <summary>Finds an attribute on the type or any base type (for inherited attributes).</summary>
    public static AttributeData? FindAttributeInHierarchy(this ITypeSymbol type, string fullMetadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var attribute = current.FindAttribute(fullMetadataName);
            if (attribute is not null)
            {
                return attribute;
            }
        }

        return null;
    }

    public static bool HasAttribute(this ISymbol symbol, string fullMetadataName) => symbol.FindAttribute(fullMetadataName) is not null;

    /// <summary>Gets a named argument value (e.g. <c>Condition = ...</c>).</summary>
    public static TypedConstant? GetNamedArgument(this AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == name)
            {
                return pair.Value;
            }
        }

        return null;
    }

    public static TypedConstant? GetConstructorArgument(this AttributeData attribute, int index) =>
        index < attribute.ConstructorArguments.Length ? attribute.ConstructorArguments[index] : null;

    public static string ToShortDisplay(this ITypeSymbol type) => type.ToDisplayString(ShortFormat);

    public static string ToFullDisplay(this ITypeSymbol type) => type.ToDisplayString(FullFormat);

    /// <summary>Kind label for display.</summary>
    public static string GetKindLabel(this ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol => "array",
        IPointerTypeSymbol or IFunctionPointerTypeSymbol => "pointer",
        ITypeParameterSymbol => "type parameter",
        IDynamicTypeSymbol => "dynamic",
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => "enum",
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => "interface",
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } => "delegate",
        INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        INamedTypeSymbol { IsRecord: true } => "record",
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => "struct",
        INamedTypeSymbol { TypeKind: TypeKind.Error } => "unresolved",
        _ => "class",
    };

    public static TypeIdentity ToIdentity(this ITypeSymbol type)
    {
        var args = type is INamedTypeSymbol { IsGenericType: true } named
            ? named.TypeArguments.Select(a => a.ToIdentity()).ToArray()
            : Array.Empty<TypeIdentity>();
        var unannotated = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return new TypeIdentity(
            unannotated.ToShortDisplay(),
            unannotated.ToFullDisplay(),
            type.GetKindLabel(),
            type.ContainingAssembly?.Name,
            type.IsValueType)
        {
            TypeArguments = args,
        };
    }

    /// <summary>Gets a navigable location for a symbol, distinguishing source, generated and metadata symbols.</summary>
    public static SourceLocation GetSourceLocation(this ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is null)
            {
                continue;
            }

            var span = location.GetLineSpan();
            var path = location.SourceTree.FilePath;
            return new SourceLocation(path, span.StartLinePosition.Line, span.StartLinePosition.Character, IsGeneratedPath(path) ? SourceOrigin.Generated : SourceOrigin.Source);
        }

        return SourceLocation.MetadataOnly;
    }

    /// <summary>Heuristic for generated source file names.</summary>
    public static bool IsGeneratedPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var p = path!;
        return p.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for Nullable&lt;T&gt;; returns the underlying type.</summary>
    public static bool TryGetNullableUnderlying(this ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
        {
            underlying = named.TypeArguments[0];
            return true;
        }

        underlying = type;
        return false;
    }

    /// <summary>Enumerates the type and its base types (most derived first).</summary>
    public static IEnumerable<INamedTypeSymbol> SelfAndBaseTypes(this INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
