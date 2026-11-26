# Release checklist

## Blockers for the first public release

- [ ] **GitHub access**: Claude GitHub App push access for the repository (still refused on 2026-11-26), or push the branch manually so CI can run.
- [x] **License**: Apache-2.0 `LICENSE` added and referenced by the VSIX manifest; `NOTICE` added; both packaged in the VSIX.
- [ ] **VSIX build** succeeds on `windows-latest` (CI job `vsix`) and `build/inspect-vsix.ps1` passes.
- [ ] **IDE validation**: every step in `docs/TESTING.md` § IDE integration validation passes on VS 2022 17.8 (oldest),
      latest VS 2022, VS 2022 Arm64 (if advertised) and VS 2026 (if advertised). Update the compatibility matrix.
- [ ] **Marketplace publisher** ID created and confirmed; `publishManifest.json` prepared (not committed with secrets).
- [ ] **Trademark/domain** clearance for "RealizeJSON" (not verified).
- [ ] Screenshots captured from the validated build.

## Every release

1. Update `Version` in `Directory.Build.props`, the `Identity Version` in `source.extension.vsixmanifest` and
   `InstalledProductRegistration` in `RealizeJSONPackage.cs`.
2. Update `CHANGELOG.md` and `IMPLEMENTATION-STATUS.md`.
3. `pwsh ./build/build.ps1`, `pwsh ./build/test.ps1 -NoBuild` — all green.
4. `pwsh ./build/package.ps1` on Windows; run `pwsh ./build/inspect-vsix.ps1`; record the SHA-256.
5. Install the VSIX on a clean VM; run the IDE validation smoke steps 1–13 and 21.
6. Review `THIRD-PARTY-NOTICES.md` and the dependency table in `docs/DECISIONS.md`.
7. Tag `vX.Y.Z`, attach the VSIX and SHA-256 to a GitHub release.
8. Publish to the Marketplace only with explicit authorization.
