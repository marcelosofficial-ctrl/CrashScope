# Privacy policy

CrashScope is designed as a local-first Windows diagnostics application.

## Data handling

CrashScope collects and processes diagnostic information on the user's own computer so that the user can inspect application, workload, hardware, and Windows failure evidence.

CrashScope does **not** require a CrashScope account and does **not** use a CrashScope-operated cloud backend.

CrashScope itself does not automatically upload diagnostic telemetry, crash evidence, support bundles, local databases, or other collected diagnostic information to the developer.

In the terminology used by the SignPath Foundation requirements:

> This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

User-requested actions, such as opening an external link, downloading software through a browser, or deliberately sharing a generated support bundle, are outside that automatic local-processing boundary.


## Microsoft WebView2 and Windows diagnostics

CrashScope's native desktop shell uses Microsoft Edge WebView2 to display the local CrashScope dashboard.

CrashScope does not use WebView2 as a CrashScope cloud telemetry channel, and CrashScope does not automatically upload its incident database, support bundles, crash evidence, or application telemetry to the CrashScope developer.

However, **WebView2 itself is a Microsoft component** and may collect required or optional diagnostic data under Microsoft Edge/Windows diagnostic-data practices. Microsoft documents that WebView2 diagnostic data can include WebView2/API usage, creation failures, browser diagnostic events, and data required to maintain performance and reliability. These controls are governed by Windows diagnostic/privacy settings where applicable.

CrashScope does not disable Microsoft Defender SmartScreen in WebView2. Microsoft documents that SmartScreen may collect and send information to Microsoft under the Microsoft Privacy Statement.

Microsoft documentation:
- WebView2 data and privacy: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/data-privacy
- Microsoft Privacy Statement: https://www.microsoft.com/en-us/privacy/privacystatement

CrashScope's embedded WebView2 navigation is restricted to its local dashboard. Navigation outside the allowed local origin is handed to the user's normal external browser rather than being loaded inside CrashScope's embedded dashboard.


## Local interfaces

CrashScope's application API and dashboard are intended to be served on the local machine through loopback/localhost interfaces rather than exposed as a public network service.

## Local storage

CrashScope may store diagnostic and workload information locally on the user's computer, including its application database and evidence associated with captured incidents.

Uninstall and data-retention behavior is documented in the project's release/install documentation.

## Support bundles

Support bundles are generated for the user to inspect or share deliberately. CrashScope does not automatically transmit them.

Users should review a support bundle before sharing it because diagnostic evidence can contain machine-specific or application-specific information.

## Third-party components

CrashScope includes open-source and third-party components. Their licensing information is documented in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

The Windows desktop application uses Microsoft WebView2 for its embedded user interface. Users may also interact with external services such as GitHub or SignPath through their browser or release workflow; those services operate under their own privacy terms.

## Changes

Material changes to CrashScope's network or data-handling behavior should be reflected in this policy and in the relevant release documentation before publication.
