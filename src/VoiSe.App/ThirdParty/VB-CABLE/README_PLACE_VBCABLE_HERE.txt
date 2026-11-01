Place the official complete VB-CABLE package here before building a release.
Alternatively, put a single vendor ZIP or a complete extracted package in
third_party/VB-CABLE at the repository root; the release script prefers it.

scripts/build-installer.ps1 publishes a version-specific, self-contained app.
It stages the full vendor package, verifies setup + INF/CAT/SYS, then removes
only the redundant ZIP from the published payload. Vendor notices remain.
The installer and VoiSee's driver repair button use that extracted package.
A copied setup EXE alone is not a valid driver package.

dotnet run keeps the source ZIP in its build output for offline driver repair.
Use the release script for a complete distributable, including driver files.
Do not put user settings, categories, presets, scenes or sounds here.
