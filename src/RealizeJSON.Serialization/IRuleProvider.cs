using System.Threading;
using Microsoft.CodeAnalysis;
using RealizeJSON.Core;
using RealizeJSON.Core.Contracts;
using RealizeJSON.Core.Json;

namespace RealizeJSON.Serialization;

/// <summary>Serializer-specific rule provider. Providers are stateless and thread-safe.</summary>
public interface IRuleProvider
{
    SerializerKind Serializer { get; }

    /// <summary>Scalar formatter matching the serializer's default writer.</summary>
    IJsonValueFormatter Formatter { get; }

    /// <summary>Builds the resolved contract for a root type. Never executes user code.</summary>
    ResolvedContract BuildContract(ITypeSymbol root, Compilation compilation, SerializationProfile profile, CancellationToken cancellationToken);
}
