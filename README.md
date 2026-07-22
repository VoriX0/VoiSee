# VoiSee 12.1.0

## VoiSee 12.1.0 — Dual noise suppression

- Keeps the validated RNNoise microphone cleanup introduced in VoiSee 12.0.0.
- Adds **DeepFilterNet** as a second real-time 48 kHz noise-suppression engine.
- Replaces separate cleanup cards with one compact engine selector and one Strength slider.
- Splits the Voice Changer page into three clear sections: **Noise suppression**, **Effects**, and **Presets**.
- Keeps noise suppression global and independent from voice-effect presets.
- Processes only physical microphone audio; SoundBoard, scene sounds, and Media Bridge audio remain untouched.
- Removes the experimental Low-frequency cleanup from VoiSee 12.0.1.
- Keeps the validated Discord screen-share isolation introduced in VoiSee 11.3.0.

Available cleanup modes:

- `Off` — no neural noise suppression;
- `RNNoise` — lightweight low-latency cleanup;
- `DeepFilterNet` — full-band neural speech enhancement with higher CPU use and latency.

The first Windows build downloads the official DeepFilterNet 0.5.6 x64 LADSPA library through `scripts\fetch-deepfilternet.ps1` and places it in the application output. The installer build includes the downloaded library automatically.

License texts are copied as ordinary files instead of Windows PRI resources. This also removes the `PRI249 ... Invalid qualifier: NET-MIT` warning caused by the previous license filename/resource treatment.

## VoiSee 11.3.0 — Discord screen-share voice isolation

- Restores the Settings and Advanced Settings interface to the VoiSee 11.2.5 layout.
- Permanently enables the validated protection against duplicated processed voice in Discord screen sharing.
- Automatically mutes only Discord render sessions attached to the normal VB-CABLE `CABLE Input` endpoint.
- Does not mute VoiSee on `CABLE Input`, Discord on physical headphones, or the `CABLE Output` microphone endpoint.
- Re-applies the protection when Discord recreates its screen-share audio session.
- Keeps the original hard Voice Monitor route disconnect and the full-width SoundBoard category buttons from 11.2.5.
- Removes the temporary WASAPI report, route switches, snapshot controls, and public isolation checkbox used in 11.2.8–11.2.9.

VoiSee is a WinUI 3 application for real-time voice processing, SoundBoard playback into a virtual microphone, scenes, presets, global hotkeys, themes, and non-destructive sound editing.

The screen-share protection is internal and always active while VoiSee is running. When VoiSee exits, it restores the original mute state of Discord sessions that it changed.

## VoiSee 11.0.0 — Media Bridge Core

VoiSee 11 introduces **Media Bridge**: select an application window, preview it, and mix that process audio into the virtual microphone without adding a duplicate headphone monitor. The first core stage includes one selected source, Start/Pause/Resume/Stop, source duration and level, a dedicated virtual-mic volume, saved descriptive profile data, and a global Pause/Resume hotkey. Media Bridge does not reconnect automatically after VoiSee restarts.

Media Bridge is provider-independent: Yandex Music is the first target scenario, while the same process-capture route can be used with other desktop media applications and browsers. The SoundBoard toolbar remains limited to `Add Track` and `Delete Track`; sound editing stays in the track context menu.

## VoiSee 11.2 — Scene Media Backgrounds

Scenes can now choose either `Looped Sound` or `Media Source` as their background. A Media Source uses a profile created when a window is selected on the Media Bridge tab. The scene editor only selects the profile, launches its application from the source card, and optionally starts capture when the scene is applied. Volume, Pause, Stop, meters, and all audio processing remain exclusively on the Media Bridge tab. Scene-owned capture stops with the scene; a Media Bridge broadcast that was already running manually is never replaced or stopped by scene deactivation.

Known service profiles provide browser fallback for Yandex Music, Spotify, and YouTube. VoiSee stores descriptive profile data, never a PID or HWND.

## Sound Editor highlights

- Centered timeline editor opened from the SoundBoard track context menu.
- Drag directly across the waveform to select a fragment.
- `Trim Outside` keeps the selection and removes everything outside it.
- `Cut Selection` removes the selected fragment and joins the remaining audio.
- Minimum selection and remaining sound length: 0.2 seconds.
- Preview uses the current `SoundBoard → Headphones` volume and never routes to the virtual microphone.
- External SoundBoard/Scene sounds and normal global hotkeys are isolated while the editor is open.
- Live waveform feedback for volume gain, normalize, fade in, fade out, and distortion.
- `Save File` updates the current library item.
- `Save as` creates unique names such as `[edit]`, `[edit 2]`, and `[edit 3]`.
- The editor owns mouse-wheel scrolling while open; the SoundBoard behind it does not scroll.

## SoundBoard file and category tools (VoiSee 10.2)

Right-click a SoundBoard track to use the new library actions:

- `Show in File Explorer` opens Explorer and selects the actual managed audio file.
- `Transfer to Category` contains separate `Move...` and `Copy...` actions.
- Move preserves the track ID, hotkey, usage statistics and scene references.
- Copy creates an independent physical file, a new ID, no hotkey, and a unique
  name such as `[copy]`, `[copy 2]`, and `[copy 3]`.

After a successful operation VoiSee opens the target category and selects the
resulting track.

The experimental internal track-to-category drag gesture was removed. Category
Move and Copy remain available through the reliable SoundBoard context menu.

Dragging WAV, MP3 or OGG files from Explorer shows a large centered import panel
covering approximately 75% of the VoiSee window.

## Windows integration (VoiSee 10.3)

- Closing the main window hides VoiSee in the notification area without stopping
  the audio engine, active scenes, looped sounds, or global hotkeys.
- The tray menu contains `Open VoiSee` and `Exit VoiSee`; a single left click on
  the tray icon restores the existing window.
- Only one process may own the audio engine. A second launch signals the existing
  instance and exits.
- `Start VoiSee with Windows` creates a per-user startup entry and launches
  `VoiSe.App.exe --background` hidden in the notification area.
- `Exit VoiSee` performs the real cleanup and process shutdown.


## Settings redesign (VoiSee 10.4)

- The first column is a focused `System & Audio` area with VB-CABLE status,
  input and monitor devices, SoundBoard delay, hotkeys, and autostart. The VB-CABLE
  output route is detected automatically and is no longer exposed as a user setting.
- The Advanced Settings card now sits under `About me` and opens a wide centered
  troubleshooting dialog with separate scrolling areas.
- The log viewer supports Clear, Copy, Export, and automatic scrolling.
- The Themes column documents native `.voiseetheme.xaml` ResourceDictionary files,
  shows the actual themes folder, and places its actions in two compact rows.
- The About column now links Telegram to `https://t.me/VoriXdev`.
- Settings keeps three columns on wide windows and stacks them vertically when space
  is limited, preventing horizontal clipping.

## Build

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```

Expected installer:

```text
artifacts\installer\VoiSee-Setup-12.1.0-x64.exe
```

Portable build:

```text
artifacts\installer\VoiSee-Portable-12.1.0-x64.zip
```

## Native XAML themes (VoiSee 10.1)

VoiSee themes are native WinUI `ResourceDictionary` files with the extension:

```text
*.voiseetheme.xaml
```

The application loads them directly into `Application.Resources.MergedDictionaries`.
There is no CSS parser or CSS-to-XAML conversion layer. **Settings → Themes →
Create New Theme** now creates a structured, commented catalogue of resources
that are actually connected to VoiSee: global palette and radii, buttons,
sliders, inputs, lists, and named VoiSee panels/actions. Save the file to see
live changes, or start with:

```text
sample-themes\Neon_Cyan.voiseetheme.xaml
sample-themes\Cosmic_Nebula.voiseetheme.xaml
sample-themes\Inferno.voiseetheme.xaml
sample-themes\Pastel_Dream.voiseetheme.xaml
```

Old `.voiseetheme.css` files from the user themes folder are archived once into
`themes\Legacy CSS` and are not loaded at runtime.
