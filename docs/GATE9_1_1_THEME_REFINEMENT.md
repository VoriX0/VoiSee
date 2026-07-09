# Gate 9.1.1 — Theme refinement

## Scope

This update refines the CSS-like theme system from Gate 9.1.

## UI changes

- Settings uses three columns across the full width:
  - main settings;
  - themes;
  - About me.
- Theme panel now contains only:
  - Create New Theme;
  - Open Theme File;
  - Open Theme Folder.
- Export, Import, and Reset buttons were removed from the panel.
- Engine status was moved from the top header into Settings / Engine manual control.
- Header mute status is horizontal and takes less vertical space.

## Theme engine changes

- Creating a new theme no longer changes the visual design by default.
- The generated `.voiseetheme.css` file contains selector blocks with commented declarations.
- Direct ids and friendly ids are supported, including examples such as:
  - `#MainSoundboard`, `#MainVoiceChanger`, `#MainScenes`, `#MainSettings`, `#MainThemes`;
  - `#SoundboardNext`, `#SoundboardTimeline`, `#SoundboardVirtualMic`;
  - `#SettingsMute`, `#SettingsStartEngine`, `#SettingsStopEngine`;
  - `#ScenesApply`, `#ScenesDisable`, `#VoicechangerMonitor`.
- Tab classes are supported:
  - `.soundboard-button`, `.soundboard-panel`, `.soundboard-slider`, `.soundboard-sound`;
  - `.voicechanger-button`, `.voicechanger-slider`;
  - `.scenes-button`, `.scenes-slider`;
  - `.settings-button`, `.settings-panel`, `.settings-slider`.
- Pseudo states are supported:
  - `:hover`;
  - `:pressed` / `:onclick`;
  - `:checked` / `:on`.
- New brush/color support:
  - `rgb(r,g,b)`;
  - `rgba(r,g,b,a)`;
  - `linear-gradient(angle, color1, color2, ...)`.

## Safety

The theme format remains a safe CSS-like subset. It does not execute scripts, load external files, or support `url()`.
