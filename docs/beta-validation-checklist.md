# Public beta validation checklist

Use this checklist before promoting a CrashScope build from internal development to a downloadable public beta.

## CI / release artifact

- [ ] Dashboard dependencies install successfully in CI.
- [ ] Dashboard production build succeeds.
- [ ] .NET restore/build succeeds with zero errors.
- [ ] Full automated test suite passes.
- [ ] `win-x64` self-contained publish succeeds.
- [ ] Published package contains `CrashScope.exe`.
- [ ] Published package contains `wwwroot/index.html` and dashboard assets.
- [ ] Published executable starts when the current working directory is **not** the publish directory.
- [ ] `/api/status` reaches `status = running`.
- [ ] `/` serves the bundled dashboard.
- [ ] Portable ZIP is generated from the same tested publish output.
- [ ] SHA-256 checksum is generated and published beside the ZIP.

## Local primary-machine smoke

- [ ] Run as a normal user, not Administrator.
- [ ] Dashboard opens on `http://localhost:5077`.
- [ ] CPU/RAM/GPU telemetry updates.
- [ ] Missing CPU sensor values remain explicitly unavailable rather than zero.
- [ ] Workload discovery can identify a real workload candidate.
- [ ] Attach to a real workload and confirm Active sampling.
- [ ] Stop monitoring and confirm Background sampling resumes.
- [ ] Session persists after restarting CrashScope.
- [ ] Environment snapshot persists with OS/CPU/GPU/driver/runtime/version data where available.
- [ ] Safe diagnostic marker completes without crashing or writing fake Windows evidence.
- [ ] Marker incident appears in incident list and survives restart.
- [ ] Incident drill-down shows telemetry, process context when available, and evidence role labels.
- [ ] Existing historical Windows evidence is not incorrectly replayed as a fresh incident on startup.

## Performance smoke

Run with a representative background workload and the dashboard connected.

- [ ] No sustained intentional GPU load attributable to CrashScope.
- [ ] Agent average CPU remains comfortably below the 0.5% product budget on the primary Ryzen 5 7500F system.
- [ ] Working set remains below the 100 MB product budget in the validated dashboard-open scenario, or any regression is explained before release.
- [ ] WebSocket diagnostics show no unexpected delivery misses.
- [ ] Dashboard does not create an additional hardware-polling loop.

## Networking / privacy

- [ ] Agent listens only on loopback.
- [ ] No account/login required.
- [ ] No analytics or automatic telemetry upload.
- [ ] No external network dependency is required for normal runtime use.
- [ ] Persistent data remains under `%LOCALAPPDATA%\CrashScope`.

## Secondary-machine beta gate

Before adding an installer or presenting the beta as broadly validated:

- [ ] Download the release ZIP from GitHub rather than copying a development build.
- [ ] Verify the published SHA-256 checksum.
- [ ] Extract to a normal user-writable folder.
- [ ] Launch `CrashScope.exe` without installing .NET or Node.
- [ ] Confirm the dashboard/API starts successfully.
- [ ] Record CPU/GPU/Windows/driver information.
- [ ] Record which hardware metrics are available/unavailable.
- [ ] Exercise workload attach/stop and safe diagnostic marker capture.
- [ ] Record any SmartScreen/antivirus friction separately from functional failures.

## Release notes

Every beta release should state:

- validated hardware/platforms
- important known limitations
- whether the binary is code-signed
- that evidence correlation is not definitive root-cause proof
- where persistent data is stored
- checksum verification instructions
