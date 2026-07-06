# VoiSee 8.2.6 — VB-CABLE installer package and restart flow fix

This buildfix improves the bundled VB-CABLE install flow.

## Fixes

- Manual `Install VB-CABLE` now prefers the fully extracted VB-CABLE package folder.
- VoiSee no longer launches a copied `VBCABLE_Setup_x64.exe` without its INF/CAT/SYS driver files.
- The release build script always extracts the bundled VB-CABLE ZIP into `ThirdParty\VB-CABLE\_extracted` before enabling the installer checkbox.
- The installer no longer auto-launches VoiSee after the VB-CABLE installation task is selected, because VB-CABLE usually needs a Windows restart before the devices appear.
- The installer requests/recommends restart when the VB-CABLE installation task is selected.
- Settings text now explains that a Windows restart may be required after installing VB-CABLE.
