# Third-party notices

CrashScope includes or builds upon third-party open-source software. This file is intended to make the major direct dependencies and their upstream license locations easy to identify. The corresponding upstream projects and package metadata remain authoritative for their complete license and attribution terms.

## Runtime dependencies

### LibreHardwareMonitorLib 0.9.6

- Project: LibreHardwareMonitor
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: Mozilla Public License 2.0 (MPL-2.0)
- CrashScope uses LibreHardwareMonitor through an adapter layer and does not expose LibreHardwareMonitor types from CrashScope.Core.
- LibreHardwareMonitor also documents third-party components under its own `THIRD-PARTY-LICENSES` material; downstream redistributors should preserve the terms applicable to the packaged library.

### Microsoft.Data.Sqlite 10.0.11

- Project family: .NET / EF Core
- Source: https://github.com/dotnet/efcore
- License: MIT

### System.Diagnostics.EventLog 10.0.11

- Project family: .NET Runtime
- Source: https://github.com/dotnet/runtime
- License: MIT

### System.Management 10.0.11

- Project family: .NET Runtime
- Source: https://github.com/dotnet/runtime
- License: MIT

### Self-contained .NET runtime

CrashScope's Windows portable package includes the .NET runtime components required to run without a separate .NET installation.

- Source: https://github.com/dotnet/runtime
- License information: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- Additional .NET notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

## Dashboard dependencies

### React / React DOM 19.2.8

- Source: https://github.com/facebook/react
- License: MIT

### Vite 8.2.2 and @vitejs/plugin-react 6.1.1

- Source: https://github.com/vitejs/vite
- License: MIT
- Vite publishes additional notices for its bundled dependencies in its package license material.

### TypeScript 7.0.2

- Source: https://github.com/microsoft/TypeScript
- License: Apache License 2.0

### React type definitions

- Project family: DefinitelyTyped
- Source: https://github.com/DefinitelyTyped/DefinitelyTyped
- License: MIT for the relevant type packages unless otherwise stated in the package metadata.

## Bundled diagnostic provider

### ConfigTrace 1.0.1

CrashScope 1.1 bundles the standalone ConfigTrace executable as an optional, process-isolated configuration-evidence provider.

- Component: ConfigTrace
- Version: 1.0.1
- License: MIT
- Copyright: Copyright (c) 2026 Marcelo
- Bundled license: `providers/ConfigTrace/LICENSE.txt`
- Frozen source commit used for this integration: `b629c970dfc14fca5df1e0ef2b0d1d07d0d8c56c`
- Frozen Windows executable SHA-256: `fe1c470a58402e82e97ee529c6a6b02822430da70e65ffc5fc5a71359ad4e521`

CrashScope launches ConfigTrace only when the user has opted in, configured an existing root directory, and a workload is being monitored. ConfigTrace reads source configuration files without modifying them, redacts sensitive-looking structured values, and emits nearby configuration changes as correlation evidence. CrashScope treats those changes as **Context** evidence; proximity does not establish causation.
## CrashScope license

CrashScope itself is distributed under the MIT License. See [`LICENSE`](LICENSE).

This notice is maintained as part of the release process. When dependencies change, update this file before publishing the next binary release.
