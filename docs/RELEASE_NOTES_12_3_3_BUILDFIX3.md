# VoiSee 12.3.3 buildfix 3

Build-system corrective release.

- Reverts the buildfix 2 `BaseIntermediateOutputPath` override.
- Restores the standard .NET SDK `obj\` layout.
- Prevents duplicate generated AssemblyInfo / TargetFramework attributes (CS0579).
- Keeps a neutral root `Directory.Build.props` specifically so updating over buildfix 2 overwrites the faulty property.
- Expands `scripts/run-dev-clean.ps1` to clean App, Audio and Gate0 CLI build artifacts before development run.

Application scrolling/UI behavior is unchanged from buildfix 2.
