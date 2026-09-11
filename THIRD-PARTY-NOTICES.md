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

## CrashScope license

CrashScope itself is distributed under the MIT License. See [`LICENSE`](LICENSE).

This notice is maintained as part of the release process. When dependencies change, update this file before publishing the next binary release.
