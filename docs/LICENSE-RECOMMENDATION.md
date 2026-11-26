# License recommendation (decision pending)

No license has been applied. Without a `LICENSE` file, the code is "all rights reserved" and the project is not open
source, so this decision blocks external contributions and publishing.

## Recommendation: MIT

| Criterion | MIT | Apache-2.0 |
|---|---|---|
| Ecosystem fit | Matches Roslyn, .NET runtime, Newtonsoft.Json | Matches xUnit, many Microsoft OSS projects |
| Patent grant | Implicit only | Explicit patent license and termination |
| Contributor friction | Minimal | Slightly more (NOTICE handling) |
| Commercial reuse | Permitted | Permitted |

MIT is recommended for simplicity and alignment with the .NET ecosystem. Choose Apache-2.0 if an explicit patent grant
is important to Kasadara Technology Solutions Private Limited.

## Steps once decided

1. Add `LICENSE` at the repository root with the chosen text and the copyright line chosen by the copyright holder.
2. Add `<License>LICENSE</License>` to `source.extension.vsixmanifest` and include the file in the VSIX.
3. Update the README License section and `CONTRIBUTING.md` (inbound = outbound, or a CLA/DCO policy).
4. Confirm third-party license compatibility (all runtime-shipped code is first-party; build/test dependencies are MIT,
   Apache-2.0 or Microsoft SDK licenses and are not redistributed).
