# CrashScope privacy-safe public publication runbook

This runbook describes the final publication transaction for CrashScope. It is preparation only. Do not execute the irreversible/account-level parts without explicit maintainer approval.

## Goal

Preserve the complete CrashScope engineering history privately while publishing a clean, privacy-safe source snapshot and a reproducible Windows beta under stable public URLs.

The full engineering repository contains valuable development evidence: PRs, issues, CI runs, historical validation SHAs, release hardening, and private continuity records. It should remain private rather than being history-rewritten solely for publication.

## Non-negotiable rules

- Never expose the historical personal commit email.
- Use GitHub `users.noreply.github.com` identity for every new public Git commit.
- Do not put the real employer-contact email in repository source or Git commit metadata. Employer contact belongs on intentional profile/portfolio/contact surfaces.
- Never delete or overwrite the private engineering repository as part of publication.
- Fail closed: if verification is incomplete, keep both repositories private.
- Never claim a clean public root commit is the exact validated private product SHA when it is not.
- Preserve the exact private validation evidence separately from public-snapshot provenance.

## Authoritative private validation evidence

Exact product-code validation head:

`9afb5418db9293a7fc9be0cc68be018ee4d89ff0`

Exact validated CI:

- CI #393
- workflow run `34391659724`
- SUCCESS

Exact validated portable artifact SHA256:

`4e37376e7a26e59b82dcc30a38638d65fb425c9c4a653189895cca05c4b4d493`

Reconciled automated baseline: 185 .NET tests, 0 failed.

## Preconditions

Before the repository transaction begins:

1. PR #59 has an explicit merge decision.
2. The exact PR head, mergeability, and CI are refreshed immediately before any merge.
3. Sanitized official screenshots in #37 have been reviewed if they are intended for the first public snapshot.
4. Second-PC validation in #35 is completed if it is required for the chosen release scope.
5. README/release documentation has had a final public-facing read-through.
6. `scripts/Test-PublicSnapshotPrivacy.ps1` has been run on the intended tracked tree.
7. Git global/local author identity is verified as GitHub noreply.
8. Repository metadata, topics, intended public description, and release notes are captured.
9. No tag, release, rename, visibility change, history rewrite, or deletion is performed without the explicit approval required by issue #39.

## Phase 1: freeze the intended public source

1. Resolve the exact private commit intended to supply the public source tree.
2. Record its commit SHA and tree SHA.
3. Export/copy tracked source without copying `.git` history.
4. Build a deterministic manifest of relative paths + SHA-256 file hashes.
5. Run the publication privacy preflight against the source repository.
6. Scan the staged snapshot independently for:
   - personal email addresses that are not deliberately approved public contact data;
   - `C:\Users\...` personal profile paths;
   - `.env`, credential, private-key, dump, ETL, database, raw WER, or similar sensitive artifacts;
   - GitHub/AWS/Google credential signatures;
   - generated build output that should not be source-controlled.
7. Stop if any unexplained finding remains.

## Phase 2: preserve engineering history

The safest topology is:

- private full-history repository: `CrashScope-development` or another explicitly chosen private engineering name;
- clean public-facing repository: `CrashScope`.

Before renaming anything:

1. confirm the destination private engineering name is free;
2. capture the current repository ID and metadata;
3. verify repository visibility is PRIVATE;
4. confirm PR #59/issues/CI evidence are accessible;
5. record the final pre-transaction repository state in private checkpoint #95.

If approved, rename the existing engineering repository while it remains private. Immediately verify that the renamed repository is still private and that PR/issues/history remain accessible.

## Phase 3: create the replacement repository privately

Create the replacement `CrashScope` repository as **PRIVATE first**, not public.

Then:

1. initialize a new Git repository in the staged clean snapshot;
2. set `user.name` to the GitHub login;
3. set `user.email` to the account's GitHub noreply address;
4. create a single public-snapshot root commit;
5. push only the intended default branch;
6. verify raw author and committer emails are noreply;
7. verify history depth is exactly what was intended;
8. clone the replacement repository and compare its file manifest with the staged manifest;
9. restore safe description/topics/homepage/settings;
10. keep the replacement PRIVATE until every verification passes.

## Phase 4: provenance

A clean public root commit will have a different commit SHA from the original private validated commit.

Public documentation must therefore say something equivalent to:

> The v0.1 beta product was validated from private engineering commit `9afb5418...` and exact CI artifact SHA-256 `4e37376e...`. The public repository was published from a privacy-sanitized source snapshot. Source-tree equivalence or public rebuild evidence is recorded separately.

Preferred provenance options, strongest first:

1. run full public CI/rebuild from the clean public repository and publish that newly validated artifact;
2. prove Git tree/source equivalence between the intended private source tree and the clean public snapshot, then retain the exact private artifact as historical validation evidence;
3. do both.

Never imply the public root commit itself was the exact private validation SHA.

## Phase 5: release

Only after explicit release approval:

1. verify final release notes;
2. verify the clean repository is still private while release setup is prepared, if possible;
3. create the intended version tag according to the approved provenance model;
4. let the release workflow build/test/package the Windows artifact;
5. verify workflow success;
6. verify artifact filename/version;
7. verify SHA-256 checksum;
8. verify bundled provenance and notices;
9. download the published asset and round-trip hash it when practical;
10. verify download instructions against the actual release.

## Phase 6: public visibility

Only after explicit visibility approval and all earlier checks pass:

1. change the clean replacement repository to PUBLIC;
2. verify the old engineering repository remains PRIVATE;
3. verify the public default branch contains only intended history;
4. verify no unintended branches/tags expose old history;
5. verify public README/screenshots/rendering;
6. verify release/download visibility;
7. run public repository/source searches for personal email, private profile paths and known credential signatures as a final external-facing check.

## Phase 7: portfolio/profile wiring

Only after stable public URLs exist:

1. update the portfolio CrashScope repository CTA;
2. add the actual Windows beta download CTA if a release exists;
3. update the GitHub profile project link;
4. optionally pin CrashScope;
5. confirm employer-contact email is exposed only through intentional contact/profile/portfolio UI, not repository source or Git commit metadata.

## Failure behavior

At any point before final visibility:

- do not delete anything;
- do not rewrite the private engineering history;
- make/keep the replacement repository private;
- record the failure and exact state in checkpoint #95;
- reconcile before retrying.

If a failure occurs after public visibility, immediately contain the replacement repository by making it private before diagnosing, unless doing so would create a larger verified risk.

## Completion evidence to record in checkpoint #95

- final private engineering repository name + repository ID
- final clean public repository name + repository ID
- private source commit SHA used for snapshot
- private source tree SHA
- public root commit SHA
- staged/public manifest equality result
- publication privacy-preflight result
- public author/committer noreply verification
- CI/rebuild run IDs
- release tag/release ID
- release artifact filename + SHA-256
- visibility verification for both repositories
- stable portfolio/profile/download URLs
- remaining roadmap items after v0.1 publication
