# Third-party notices

## Redistributed in the VSIX

None. The VSIX contains only RealizeJSON assemblies and assets. Roslyn, the Visual Studio editor and shell assemblies
are provided by Visual Studio at runtime and are not redistributed.

Algorithms ported (not copied as code) for behavioral fidelity:

- Naming policies and the default JavaScript encoder behavior of **System.Text.Json** (dotnet/runtime, MIT License,
  Copyright (c) .NET Foundation and Contributors).
- `StringUtils.ToCamelCase`/`ToSeparatedCase` behavior of **Newtonsoft.Json** (MIT License,
  Copyright (c) 2007 James Newton-King).

## Build-time and test-only dependencies (not redistributed)

| Component | Version | License |
|---|---|---|
| Microsoft.VisualStudio.SDK | 17.8.37222 | Microsoft Visual Studio SDK license |
| Microsoft.VSSDK.BuildTools | 17.14.2142 | Microsoft license |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 4.8.0 | MIT |
| Microsoft.CodeAnalysis.EditorFeatures.Text | 4.8.0 | MIT |
| Microsoft.VisualStudio.LanguageServices | 4.8.0 | MIT |
| Microsoft.NETFramework.ReferenceAssemblies (implicit, non-Windows builds) | 1.0.3 | MIT |
| Newtonsoft.Json (tests) | 13.0.4 | MIT |
| xunit.v3 (tests) | 3.2.2 | Apache-2.0 |
| xunit.runner.visualstudio (tests) | 3.1.5 | Apache-2.0 |
| Microsoft.NET.Test.Sdk (tests) | 18.0.1 | MIT |

Brand assets in `assets/branding/` are original works created for this project. The wordmark rasters were rendered
with the Inter typeface (SIL Open Font License 1.1); the font itself is not redistributed.
