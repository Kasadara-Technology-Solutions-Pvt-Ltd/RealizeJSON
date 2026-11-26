# Release checklist

## Blockers for the first public release

- [x] **GitHub access**: branch pushed; CI runs on push and pull requests.
- [x] **License**: Apache-2.0 `LICENSE` added and referenced by the VSIX manifest; `NOTICE` added; both packaged in the VSIX.
- [x] **VSIX build** succeeds on `windows-latest` (CI job `vsix`) and `build/inspect-vsix.ps1` passes.
- [ ] **IDE validation**: every step in `docs/TESTING.md` § IDE integration validation passes on VS 2022 17.8 (oldest),
      latest VS 2022, VS 2022 Arm64 (if advertised) and VS 2026 (if advertised). Update the compatibility matrix.
- [ ] **Marketplace publisher** ID created and confirmed; `publishManifest.json` prepared (not committed with secrets).
- [ ] **Trademark/domain** clearance for "RealizeJSON" (not verified).
- [ ] Screenshots captured from the validated build.

## Every release

1. Update `Version` in `Directory.Build.props`, the `Identity Version` in `source.extension.vsixmanifest` and
   `InstalledProductRegistration` in `RealizeJSONPackage.cs` (`pwsh ./build/check-version.ps1` verifies they match).
2. Move the `## [Unreleased]` entries in `CHANGELOG.md` to `## [X.Y.Z] - YYYY-MM-DD` (used as the release notes) and
   update `IMPLEMENTATION-STATUS.md`. Merge to `main` through a pull request.
3. Optional dry run: run the **Release** workflow manually (*Actions › Release › Run workflow*) and download the
   `release-X.Y.Z` artifact.
4. Install that VSIX on a clean VM; run the IDE validation smoke steps 1–13 and 21.
5. Review `THIRD-PARTY-NOTICES.md` and the dependency table in `docs/DECISIONS.md`.
6. Tag the merge commit and push the tag:
   `git tag -a vX.Y.Z -m "RealizeJSON X.Y.Z"` then `git push origin vX.Y.Z`.
   The **Release** workflow verifies the version against the tag, builds, tests, packages and inspects the VSIX, and
   publishes a GitHub Release with `RealizeJSON-X.Y.Z.vsix` and `SHA256SUMS.txt`. 0.x versions and `-suffix` tags
   are published as pre-releases.
7. Publish to the Marketplace only with explicit authorization.
