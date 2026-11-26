using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RealizeJSON.Serialization;
using RealizeJSON.Serialization.NewtonsoftJson;
using RealizeJSON.Serialization.SystemTextJson;

namespace RealizeJSON.Tests.Shared;

/// <summary>Creates in-memory Roslyn workspaces (the same API surface VisualStudioWorkspace exposes).</summary>
public static class WorkspaceFactory
{
    public static PreviewEngine CreateEngine() => new(new IRuleProvider[] { new SystemTextJsonRuleProvider(), new NewtonsoftJsonRuleProvider() });

    public static (AdhocWorkspace Workspace, Project Project) CreateProject(string name = "Api", params (string Name, string Text)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var project = AddProject(workspace, name, documents);
        return (workspace, project);
    }

    public static Project AddProject(AdhocWorkspace workspace, string name, (string Name, string Text)[] documents, params ProjectReference[] references)
    {
        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(name),
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: TestCompilation.ParseOptions,
            metadataReferences: TestCompilation.DefaultReferences,
            projectReferences: references);
        var project = workspace.AddProject(info);
        foreach (var (docName, text) in documents)
        {
            project = workspace.AddDocument(project.Id, docName, SourceText.From(text)).Project;
        }

        return workspace.CurrentSolution.GetProject(project.Id)!;
    }

    public static int Caret(Document document, string marker, int offset = 0) =>
        document.GetTextAsync().Result.ToString().IndexOf(marker, System.StringComparison.Ordinal) + offset;
}
