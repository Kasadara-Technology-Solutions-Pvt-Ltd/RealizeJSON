using System;

namespace RealizeJSON.VisualStudio;

/// <summary>GUIDs and command IDs (must match RealizeJSONPackage.vsct).</summary>
internal static class PackageIds
{
    public const string PackageGuidString = "6f3d9a7e-2c4b-4e8a-9b1d-5a7c3e2f8d10";
    public const string CommandSetGuidString = "b1c2d3e4-5f60-4a7b-8c9d-0e1f2a3b4c5d";
    public const string ToolWindowGuidString = "8e7d6c5b-4a39-4281-9f0e-1d2c3b4a5f60";

    public static readonly Guid CommandSet = new(CommandSetGuidString);

    public const int ShowToolWindowCommandId = 0x0100;
    public const int PreviewAsJsonCommandId = 0x0101;
}
