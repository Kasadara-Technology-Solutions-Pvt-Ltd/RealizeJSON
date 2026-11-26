using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RealizeJSON.CodeAnalysis;

/// <summary>Why a candidate was offered.</summary>
public enum CandidateReason
{
    Declaration,
    Reference,
    EnclosingType,
    MemberType,
    ConstructedUsage,
}

/// <summary>A type that can be previewed.</summary>
public sealed class TypeCandidate
{
    public TypeCandidate(ITypeSymbol symbol, CandidateReason reason)
    {
        Symbol = symbol;
        Reason = reason;
        Key = TypeKey.Create(symbol);
        Display = symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToShortDisplay();
    }

    public ITypeSymbol Symbol { get; }

    public CandidateReason Reason { get; }

    public TypeKey? Key { get; }

    public string Display { get; }
}

/// <summary>Result of locating types at a caret position.</summary>
public sealed class TypeLocation
{
    public TypeLocation(IReadOnlyList<TypeCandidate> candidates, TextSpan? enclosingSpan)
    {
        Candidates = candidates;
        EnclosingDeclarationSpan = enclosingSpan;
    }

    public static TypeLocation None { get; } = new(Array.Empty<TypeCandidate>(), null);

    /// <summary>Candidates ordered by relevance; the first is the primary selection.</summary>
    public IReadOnlyList<TypeCandidate> Candidates { get; }

    public TypeCandidate? Primary => Candidates.Count > 0 ? Candidates[0] : null;

    /// <summary>Span of the innermost enclosing type declaration, if any.</summary>
    public TextSpan? EnclosingDeclarationSpan { get; }
}

/// <summary>Finds eligible types at a caret position using semantic symbols (never text matching).</summary>
public static class TypeLocator
{
    private const int MaxConstructedUsages = 10;

    public static async Task<TypeLocation> LocateAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return TypeLocation.None;
        }

        return Locate(model, root, position, cancellationToken);
    }

    public static TypeLocation Locate(SemanticModel model, SyntaxNode root, int position, CancellationToken cancellationToken)
    {
        position = Math.Max(0, Math.Min(position, root.FullSpan.End));
        var candidates = new List<TypeCandidate>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        void Add(ITypeSymbol? type, CandidateReason reason, bool first = false)
        {
            if (type is null || !IsEligible(type) || !seen.Add(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
            {
                return;
            }

            var candidate = new TypeCandidate(type, reason);
            if (first)
            {
                candidates.Insert(0, candidate);
            }
            else
            {
                candidates.Add(candidate);
            }
        }

        var token = FindToken(root, position);
        TextSpan? enclosingSpan = null;
        BaseTypeDeclarationSyntax? innermost = null;

        // 1. Type declaration identifier under the caret.
        if (token.Parent is BaseTypeDeclarationSyntax declaration && token == declaration.Identifier)
        {
            Add(model.GetDeclaredSymbol(declaration, cancellationToken), CandidateReason.Declaration);
        }

        // 2. Type reference under the caret (user types become primary; framework types are secondary candidates).
        ITypeSymbol? secondaryReference = null;
        var nameNode = GetNameNode(token);
        if (nameNode is not null)
        {
            var symbol = model.GetSymbolInfo(nameNode, cancellationToken).Symbol;
            if (symbol is IAliasSymbol alias)
            {
                symbol = alias.Target;
            }

            switch (symbol)
            {
                case ITypeSymbol referenced when IsUserType(referenced):
                    Add(referenced, CandidateReason.Reference);
                    break;
                case ITypeSymbol referenced:
                    secondaryReference = referenced;
                    break;
                case IPropertySymbol property:
                    secondaryReference = property.Type;
                    break;
                case IFieldSymbol field:
                    secondaryReference = field.Type;
                    break;
                case ILocalSymbol local:
                    secondaryReference = local.Type;
                    break;
                case IParameterSymbol parameter:
                    secondaryReference = parameter.Type;
                    break;
            }
        }
        else if (token.Parent is PropertyDeclarationSyntax propertyDecl && token == propertyDecl.Identifier)
        {
            secondaryReference = model.GetDeclaredSymbol(propertyDecl, cancellationToken)?.Type;
        }

        // 3. Enclosing type declarations (innermost first).
        foreach (var ancestor in token.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
        {
            if (ancestor is BaseTypeDeclarationSyntax typeDecl && typeDecl.Span.Contains(position))
            {
                innermost ??= typeDecl;
                Add(model.GetDeclaredSymbol(typeDecl, cancellationToken), CandidateReason.EnclosingType);
            }
        }

        if (innermost is not null)
        {
            enclosingSpan = innermost.Span;
        }

        if (secondaryReference is not null)
        {
            var element = UnwrapForCandidate(secondaryReference);
            Add(secondaryReference, CandidateReason.MemberType);
            if (!SymbolEqualityComparer.Default.Equals(element, secondaryReference) && IsUserType(element))
            {
                Add(element, CandidateReason.MemberType);
            }
        }

        // 4. Constructed usages of an open generic primary.
        if (candidates.Count > 0 && candidates[0].Symbol is INamedTypeSymbol { IsGenericType: true } generic && IsOpen(generic))
        {
            foreach (var usage in FindConstructedUsages(model, root, generic.OriginalDefinition, cancellationToken))
            {
                Add(usage, CandidateReason.ConstructedUsage);
            }
        }

        return new TypeLocation(candidates, enclosingSpan);
    }

    /// <summary>True when the type can be previewed.</summary>
    public static bool IsEligible(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsEligible(array.ElementType) || array.ElementType.SpecialType != SpecialType.None,
        INamedTypeSymbol named => named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum
            && !named.IsStatic && named.SpecialType != SpecialType.System_Void,
        _ => false,
    };

    public static bool IsOpen(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
        {
            if (t.TypeArguments.Any(a => a is ITypeParameterSymbol || (a is INamedTypeSymbol n && n.IsGenericType && IsOpen(n))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>User types are declared in source or in non-framework assemblies.</summary>
    public static bool IsUserType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return false;
        }

        if (type.Locations.Any(l => l.IsInSource))
        {
            return true;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return !(ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
                 ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                 ns.StartsWith("Newtonsoft.", StringComparison.Ordinal));
    }

    private static ITypeSymbol UnwrapForCandidate(ITypeSymbol type)
    {
        if (type.TryGetNullableUnderlying(out var underlying))
        {
            return underlying;
        }

        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Length > 0)
        {
            return named.TypeArguments[named.TypeArguments.Length - 1];
        }

        return type;
    }

    private static SyntaxToken FindToken(SyntaxNode root, int position)
    {
        var token = root.FindToken(position);
        if (position > 0 && (token.Span.Start == position || !token.Span.Contains(position)))
        {
            var previous = root.FindToken(position - 1);
            if (previous.IsKind(SyntaxKind.IdentifierToken) && previous.Span.End == position)
            {
                return previous;
            }
        }

        return token;
    }

    private static SyntaxNode? GetNameNode(SyntaxToken token)
    {
        if (!token.IsKind(SyntaxKind.IdentifierToken) && !SyntaxFacts.IsPredefinedType(token.Kind()))
        {
            return null;
        }

        var node = token.Parent;
        if (node is PredefinedTypeSyntax)
        {
            return node;
        }

        if (node is not SimpleNameSyntax)
        {
            return null;
        }

        // Qualified names: bind the whole qualified name when the caret is on the right-most part.
        while (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
        {
            node = qualified;
        }

        while (node.Parent is AliasQualifiedNameSyntax aliasQualified && aliasQualified.Name == node)
        {
            node = aliasQualified;
        }

        return node;
    }

    private static IEnumerable<ITypeSymbol> FindConstructedUsages(SemanticModel model, SyntaxNode root, INamedTypeSymbol definition, CancellationToken cancellationToken)
    {
        var results = new List<ITypeSymbol>();
        foreach (var generic in root.DescendantNodes().OfType<GenericNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generic.Identifier.ValueText != definition.Name)
            {
                continue;
            }

            if (model.GetSymbolInfo(generic, cancellationToken).Symbol is INamedTypeSymbol constructed &&
                SymbolEqualityComparer.Default.Equals(constructed.OriginalDefinition, definition) &&
                !IsOpen(constructed))
            {
                results.Add(constructed);
                if (results.Count >= MaxConstructedUsages)
                {
                    break;
                }
            }
        }

        return results;
    }
}
