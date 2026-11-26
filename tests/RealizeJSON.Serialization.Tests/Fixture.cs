using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RealizeJSON.Tests.Shared;

namespace RealizeJSON.Serialization.Tests;

/// <summary>Shared compiled fixture (compiled once per test run).</summary>
public static class Fixture
{
    private static readonly Lazy<CSharpCompilation> LazyCompilation = new(() =>
        TestCompilation.Create(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contracts.cs"))));

    public static CSharpCompilation Compilation => LazyCompilation.Value;

    public static INamedTypeSymbol Type(string name) => TestCompilation.GetType(Compilation, "Fixtures." + name);
}
