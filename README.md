# VoiSee 10.2.1

VoiSee is a WinUI 3 application for real-time voice processing, SoundBoard playback into a virtual microphone, scenes, presets, global hotkeys, themes, and non-destructive sound editing.

## Sound Editor highlights

- Centered timeline editor opened from `Edit Track` or the sound context menu.
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

VoiSee 10.2.1 also supports direct category drag-and-drop:

- drag an existing track toward the category ComboBox;
- after a short hover the category list opens automatically;
- drop on another category to move the track;
- hold `Ctrl` while dropping to create an independent copy;
- the active target category is highlighted;
- dropping on the source category is ignored safely.

Buildfix 1 starts the internal drag through explicit pointer movement detection
and `StartDragAsync`, avoiding dependence on WinUI automatic drag recognition
through the transparent SoundBoard input overlay.

Dragging WAV, MP3 or OGG files from Explorer now shows a large centered import
panel covering approximately 75% of the VoiSee window. Internal track dragging
does not display the file-import panel.

## Build

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-installer.ps1
```

Expected installer:

```text
artifacts\installer\VoiSee-Setup-10.2.1-x64.exe
```

Portable build:

```text
artifacts\installer\VoiSee-Portable-10.2.1-x64.zip
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
