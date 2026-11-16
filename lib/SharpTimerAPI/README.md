# SharpTimer API build reference

`SharpTimerAPI.dll` is the unmodified .NET 8 assembly from the official [SharpTimer v0.3.1x release](https://github.com/SharpTimer/SharpTimer/releases/tag/v0.3.1x), archive `SharpTimer_0.3.1x.zip`.

- Assembly identity: `SharpTimerAPI, Version=1.0.0.0`.
- SHA-256: `0d3bb24c42f0a9d89c09ed33e6da335bf15d574dc49946998a487006e818ce36`.
- Corresponding source: [SharpTimerAPI at v0.3.1x](https://github.com/SharpTimer/SharpTimer/tree/v0.3.1x/SharpTimerAPI).
- License: upstream GPL-3.0; the upstream license text is included in `LICENSE`.

This reference keeps MovementHud buildable on .NET 8. The `ISharpTimerManager` contract used here is unchanged in SharpTimer v0.4.2/current main. It is excluded from MovementHud's publish output (`Private=false`). Do not install this build reference over the server's API: use the shared `SharpTimerAPI.dll` supplied with the server's own SharpTimer version.
