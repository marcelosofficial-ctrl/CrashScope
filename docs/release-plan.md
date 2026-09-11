# CrashScope stable release plan

CrashScope's stable-release path must preserve the project's low-overhead, local-first architecture, exact validation evidence, privacy strategy, and least-privilege Windows behavior while producing a polished installer and portable package.

## Current status

Core product behavior remains frozen while the local stable-release source is now deliberately stamped version `1.0.0`. Final release artifacts, exact-candidate validation, tagging, publication, and repository migration remain separate later gates.

The project now has:

- 185 passing .NET tests
- a deterministic local Windows release-candidate builder
- a self-contained `win-x64` portable package
- versioned ZIP and SHA-256 output
- a per-user Inno Setup 7 installer
- versioned installer and SHA-256 output
- installer install/repair/uninstall lifecycle validation
- state-safe validation of the actual installed CrashScope runtime
- durable preservation and independently verified restoration of existing CrashScope user state during invasive validation
- version-aware release-workflow contracts
- manual-only private GitHub Actions while private Actions minutes are being conserved

The current engineering repository remains private.

The local source version is now stamped `1.0.0`. No stable tag, GitHub Release, repository migration, public visibility change, or publication has occurred.

## Proven release-engineering evidence

Checkpoint 3C proved the local portable release path from source commit:

`bfc0169806b7a7c8b57a205ca8c77007f8a3c0cb`

Portable candidate SHA-256:

`c5ab46d302079d8a33f4248448f6197ec9565aedbd79a7bc85ece046977148d8`

Checkpoint 4B produced the installer from installer-source commit:

`414482738155e5c78ae1292d0ae4691420193432`

Installer candidate SHA-256:

`775154d1c17f1ef02a510b7f75e165d5adadb86703fe4f6d6b5e8de7b193ce1e`

Checkpoint 4C proved:

- fresh per-user installation
- 372 packaged files hash-identical to the validated publish tree
- Start Menu shortcut and target
- optional desktop shortcut remaining off by default
- HKCU per-user uninstall registration
- user-data preservation
- Start-with-Windows non-interference
- same-version repair
- clean uninstall

Checkpoint 4D additionally proved the installed runtime itself:

- healthy normal-user launch
- loopback-only listener
- browser/localhost security
- second-instance safety
- workload attach and manual stop
- Active to Background transition
- safe incident capture
- SQLite restart persistence
- no implicit Start-with-Windows registration
- uninstall after runtime use
- independently verified restoration of original user data and startup state
- zero pending durable validation vaults after success

These are local primary-machine proofs. They do not replace genuine second-machine validation.

## Stable-release sequence

1. Finish release-facing documentation, metadata, error/first-run polish, and publication-facing presentation.
2. Ensure installer and portable artifacts are both represented by version-independent release tooling and regression contracts.
3. **Completed locally:** perform the deliberate stable `1.0.0` metadata/version stamp.
4. Build a fresh local `1.0.0` release candidate from the exact clean committed source.
5. Produce both:
   - `CrashScope-v1.0.0-win-x64.zip`
   - `CrashScope-Setup-1.0.0.exe`
6. Verify both SHA-256 sidecars and BUILD-INFO/provenance.
7. Run portable and installed-runtime validation against the exact final `1.0.0` candidate.
8. Perform genuine second-Windows-PC validation when another appropriate machine is available.
9. Refresh PR #59 and merge the integration branch into private `main` only after explicit user approval.
10. Preserve the full engineering repository privately, including issues, PRs, CI evidence, historical SHAs, and validation history.
11. Create the intended public CrashScope repository from a clean current-source snapshot only after explicit migration/publication approval.
12. Run public-snapshot privacy and Git-identity checks before publication.
13. Establish truthful provenance between the private validated source and clean public snapshot.
14. Restore final public README, screenshots, hardware limitations, privacy documentation, roadmap, and stable download links.
15. Create the stable version tag only after separate explicit user approval.
16. Publish the GitHub Release only after separate explicit user approval.
17. Verify the exact public installer, portable ZIP, checksums, and installation instructions after publication.
18. Only after stable public URLs exist, update portfolio and profile links.

## Stable 1.0 requirements

- Windows x64-compatible target.
- Normal installation and operation without Administrator elevation.
- Per-user install under `%LOCALAPPDATA%\Programs\CrashScope`.
- Start Menu integration.
- Desktop shortcut optional rather than forced.
- No installer-controlled Start-with-Windows enablement.
- Existing CrashScope startup preference remains authoritative.
- No Windows service.
- Localhost-only UI/API.
- Browser-origin protection for localhost HTTP/WebSocket access.
- No account, mandatory cloud backend, analytics, or automatic diagnostic upload.
- No mandatory internet connection for normal runtime behavior.
- Self-contained .NET runtime.
- Bundled dashboard assets.
- SQLite/user state under `%LOCALAPPDATA%\CrashScope`.
- Uninstall preserves user data unless a user deliberately removes it separately.
- Portable distribution remains supported.
- SHA-256 checksum provided for installer and portable artifacts.
- BUILD-INFO/provenance retained.
- Hardware-support wording distinguishes architecture readiness from real-machine validation.
- Public Git metadata uses the intended GitHub noreply identity.
- Unsigned-build/SmartScreen behavior is documented accurately if v1 remains unsigned.

## Current hardware-validation wording

Primary real-hardware validation:

- AMD Ryzen 5 7500F
- AMD Radeon RX 9070 XT
- Windows 11

Architecture targets AMD/NVIDIA GPUs and AMD/Intel CPUs.

Broader NVIDIA/Intel real-machine validation remains roadmap work until evidence is recorded.

## Privacy-safe public repository model

The full engineering repository should remain private because its history intentionally preserves development evidence, PRs, issues, CI runs, and historical validation SHAs.

The future public repository should contain a clean current-source snapshot.

Before it becomes public:

1. run the repository's public-snapshot privacy checks against the intended tree;
2. verify no forbidden artifacts, credential/key signatures, personal Windows profile paths, or unintended email addresses are present;
3. commit the public snapshot using the intended GitHub noreply identity;
4. verify author and committer metadata;
5. compare private staged/public file manifests;
6. document provenance honestly.

A clean public root commit necessarily has a different commit SHA from the private engineering repository. Do not describe two different commits as the same commit merely because their source trees are equivalent.

## Remaining hardening after installer validation

- release-facing documentation and screenshots
- final application metadata polish
- final stable-version stamp
- fresh exact-candidate validation
- genuine second-PC validation
- broader NVIDIA/Intel real-hardware coverage
- optional code signing when justified
- continued native WER report-store research without promoting it into live triggering until evidence supports that change

## Approval boundaries

The following remain separate explicit user decisions:

- merge PR #59
- create the stable v1 tag
- publish a GitHub release
- rename the private historical repository
- create/migrate to the clean public repository
- change repository visibility
- rewrite Git history
- delete repositories or release assets

Passing local validation does not authorize any of those operations.
