# Code signing policy

CrashScope is an open-source project distributed under the MIT License.

## Current signing status

CrashScope's currently published v1.2.0 Windows release is unsigned.

CrashScope intends to use **SignPath Foundation** for future public Windows code signing if the project is accepted into the free open-source program. No SignPath Foundation signing should be assumed for a release unless the published release notes and artifact signatures explicitly show it.

When SignPath Foundation signing is active for a release, the project will use the attribution required by the Foundation:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation

## Source and build policy

Only CrashScope-owned binaries built from the public CrashScope source repository and its committed build configuration are eligible for CrashScope release signing.

Repository:

- https://github.com/marcelosofficial-ctrl/CrashScope

Release signing is approval-gated. A signing request must come from the declared automated release workflow and must be traceable to an exact source commit and release version.

Third-party or upstream binaries that may be included in a CrashScope package are not to be signed as CrashScope-owned code with the project's SignPath Foundation certificate.

## Roles

CrashScope is currently maintained as a single-maintainer project.

- **Authors / committers:** `marcelosofficial-ctrl`
- **Reviewers:** `marcelosofficial-ctrl` reviews external contributions before merge.
- **Approvers:** `marcelosofficial-ctrl` is responsible for approving public release signing requests.

These assignments may be expanded if the project gains additional maintainers. Any future signing setup must preserve explicit manual approval for public release signing.

## Release approval

Public releases are not automatically signed or published.

For a Foundation-signed release, the intended sequence is:

1. build from the declared public source commit using the approved release workflow;
2. verify the unsigned artifact's source/build origin;
3. submit the signing request through the approved SignPath integration;
4. manually approve the signing request;
5. verify the resulting Authenticode signature and signed artifact contents;
6. publish only the exact approved artifacts after final release approval.

## Privacy

See the [CrashScope privacy policy](privacy-policy.md).

CrashScope is designed to be local-first. It does not require a CrashScope account, does not use a CrashScope cloud backend, and does not automatically upload diagnostic telemetry or crash evidence to the developer.

## Security

Questions or vulnerability reports should follow the repository's [security policy](../SECURITY.md).
