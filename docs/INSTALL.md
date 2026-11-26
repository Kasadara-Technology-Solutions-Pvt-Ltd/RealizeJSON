# Installing RealizeJSON

> Download `RealizeJSON-<version>.vsix` and `SHA256SUMS.txt` from
> [GitHub Releases](https://github.com/Kasadara-Technology-Solutions-Pvt-Ltd/RealizeJSON/releases), or build one with
> `pwsh ./build/package.ps1` on Windows. 0.x releases are pre-releases that have not yet been validated in every IDE.

Verify the download (PowerShell, in the download folder):

```bash
pwsh -c "(Get-FileHash RealizeJSON-0.1.0.vsix -Algorithm SHA256).Hash.ToLower()"
```

The value must equal the hash in `SHA256SUMS.txt`.

## Requirements

- Visual Studio 2022 version 17.8 or later (Community, Professional or Enterprise), x64 or Arm64.
- C# language services (installed with any .NET development workload).
- Visual Studio 2026 accepts Visual Studio 2022 extensions, but RealizeJSON has not been validated there yet.

## Install

1. Close all Visual Studio instances.
2. Double-click `RealizeJSON.VisualStudio.vsix` (runs `VSIXInstaller.exe`), select the Visual Studio instances and
   choose **Install**.
3. Start Visual Studio and open **View › Other Windows › RealizeJSON**.

Command line (silent, for a specific instance):

```bash
"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe" /quiet RealizeJSON.VisualStudio.vsix
```

## Try it in the experimental instance (contributors)

1. Open `RealizeJSON.sln` in Visual Studio 2022 with the *Visual Studio extension development* workload.
2. Set `RealizeJSON.VisualStudio` as the startup project and press **F5**. Visual Studio launches
   `devenv.exe /rootsuffix Exp` with the extension deployed to the experimental hive.
3. Reset the experimental instance if needed: *Developer Command Prompt* →
   `"%VSINSTALLDIR%VSSDK\VisualStudioIntegration\Tools\Bin\CreateExpInstance.exe" /Reset /VSInstance=17.0 /RootSuffix=Exp`.

## Uninstall

- **Extensions › Manage Extensions › Installed**, select *RealizeJSON - Live JSON Serialization Preview*, choose
  **Uninstall**, then restart Visual Studio; or
- `VSIXInstaller.exe /uninstall:RealizeJSON.VisualStudio.6f3d9a7e-2c4b-4e8a-9b1d-5a7c3e2f8d10`

User settings are stored in the Visual Studio settings store under the `RealizeJSON` collection; the optional
`.realizejson.json` solution profile is a normal file in the solution folder.

## Troubleshooting

| Symptom | Check |
|---|---|
| Window shows *Unsupported* | The active document must be a C# file in a loaded project (not a miscellaneous file). |
| *No type selected* | Place the caret inside a class/record/struct/interface/enum declaration or on a type name. |
| Preview is *Stale* | The file has syntax errors; the last valid preview is shown until they are fixed. |
| Names are not camelCase | The *Library defaults* preset keeps declared names; choose *ASP.NET Core web defaults* or set Naming. |
| Detected settings are not applied | Only literal assignments inside `AddJsonOptions`/`ConfigureHttpJsonOptions`/`Configure<JsonOptions>`/`AddNewtonsoftJson` are recognized; see the Diagnostics tab (RJ1014/RJ1021). |
| The extension fails to load | *Help › About*; then `%APPDATA%\Microsoft\VisualStudio\17.0_<id>\ActivityLog.xml` (start VS with `/log`) and search for "RealizeJSON". |
| Shortcut does nothing | *Tools › Options › Environment › Keyboard*, search `RealizeJSON.PreviewAsJSON`, check for conflicts. |
