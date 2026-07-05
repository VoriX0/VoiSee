Place the official VB-CABLE package here before building the VoiSee installer.

Supported:
- original official VB-CABLE ZIP archive
- extracted package with VBCABLE_Setup_x64.exe

Build script behavior:
- copies files from third_party\VB-CABLE into src\VoiSe.App\ThirdParty\VB-CABLE before publish
- if ZIP is present, extracts it inside publish output to make the installer checkbox work
- if VBCABLE_Setup_x64.exe is detected, Inno Setup receives /DVBCABLE_BUNDLED and shows the optional checkbox

Do not place user-generated VoiSee sounds, categories, presets, scenes, or settings here.
