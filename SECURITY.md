# Security policy

## Reporting a vulnerability

Please do not open public issues for security problems. Use GitHub's private vulnerability reporting
(**Security › Report a vulnerability**) on this repository. Include the affected version, reproduction steps and
impact. We aim to acknowledge reports within five business days.

## Security model

- RealizeJSON analyzes source code with Roslyn compiler APIs only. It never compiles to binaries, loads project
  assemblies, invokes constructors, getters, converters, contract resolvers, source generators or startup code.
- It makes no network requests and collects no telemetry. Failures are logged only to the local Visual Studio
  ActivityLog, without source text or payloads.
- Inputs it parses (settings, `.realizejson.json`) are size- and depth-limited; malformed input falls back to defaults.
- The sensitive-member warning is a name-based heuristic, not a security assessment.
