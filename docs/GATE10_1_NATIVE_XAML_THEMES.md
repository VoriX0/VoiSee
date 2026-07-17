# VoiSee 10.1.0 — Native WinUI XAML themes

## Base

This branch is based on the accepted VoiSee 9.2.7 release candidate. The unverified VoiSee 10.0 audio-monitor isolation experiment remains a separate test branch and is not included in 10.1.0.

## Goal

Remove the custom CSS-like parser and apply user themes directly as WinUI `ResourceDictionary` XAML.

## Theme format

```text
*.voiseetheme.xaml
```

The root object must be:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</ResourceDictionary>
```

Runtime theme files must not contain `x:Class` or XAML event handlers.

## Native loading

1. VoiSee reads the XAML file.
2. `Microsoft.UI.Xaml.Markup.XamlReader.Load` creates a runtime object tree.
3. The root is checked to be `ResourceDictionary`.
4. Required semantic resources are validated.
5. The new dictionary is added to `Application.Resources.MergedDictionaries`.
6. Only after successful loading is the previous user dictionary removed.

If parsing or validation fails, the last working theme remains active.

## Required semantic keys

- `VoiSee.AppBackgroundBrush`
- `VoiSee.PanelBackgroundBrush`
- `VoiSee.PrimaryTextBrush`
- `VoiSee.AccentBrush`

Each required key must contain a WinUI `Brush`.

## Stable optional semantic keys

- `VoiSee.TitleBarBackgroundBrush`
- `VoiSee.TitleBarForegroundBrush`
- `VoiSee.TitleBarHoverBrush`
- `VoiSee.TitleBarPressedBrush`
- `VoiSee.PanelBorderBrush`
- `VoiSee.SecondaryTextBrush`
- `VoiSee.DangerBrush`
- `VoiSee.SuccessBrush`
- `VoiSee.WarningBrush`
- `VoiSee.ButtonBackgroundBrush`
- `VoiSee.ButtonHoverBrush`
- `VoiSee.ButtonPressedBrush`
- `VoiSee.ButtonBorderBrush`
- `VoiSee.InputBackgroundBrush`
- `VoiSee.TimelineHostBrush`
- `VoiSee.TransparentHitTestBrush`
- `VoiSee.TimelineTrackBrush`
- `VoiSee.TimelineFillBrush`
- `VoiSee.TimelineThumbBrush`
- `VoiSee.MuteBannerBackgroundBrush`
- `VoiSee.MuteBannerBorderBrush`
- `VoiSee.DropOverlayBackgroundBrush`
- `VoiSee.DropOverlayBorderBrush`
- `VoiSee.DropOverlayInnerBackgroundBrush`
- `VoiSee.DropOverlayInnerBorderBrush`
- `VoiSee.CardValueBackgroundBrush`
- `VoiSee.CardValueBorderBrush`
- `VoiSee.VBCableNoticeBackgroundBrush`
- `VoiSee.VBCableNoticeBorderBrush`
- `VoiSee.CornerRadius.Small`
- `VoiSee.CornerRadius.Medium`
- `VoiSee.CornerRadius.Large`

Themes may also override ordinary WinUI resources, `Style`, `ControlTemplate`, brushes, colors and other shareable XAML resources directly.

## Panel behavior retained

- Default Dark protected entry.
- Theme list.
- Create New Theme.
- Open Theme File.
- Open Theme Folder.
- Rename Theme.
- Delete Theme.
- Live reload with debounce.

## CSS migration

On the first VoiSee 10.1 startup:

- `.voiseetheme.css` and other `.css` files in the user theme root are moved to `themes\Legacy CSS`;
- an active CSS theme is reset to Default Dark;
- the user receives one migration notice during that run;
- CSS files are not loaded at runtime;
- no automatic CSS-to-XAML converter is included.

## Performance

The previous CSS engine traversed and repainted the visual tree. VoiSee 10.1 removes that path. Tab switches no longer invoke theme repaint or selector matching; newly materialized controls resolve application resources through WinUI.

## Sample

```text
sample-themes\Neon_Cyan.voiseetheme.xaml
```

The sample documents the complete semantic contract and demonstrates direct overrides of standard WinUI resources and reusable keyed styles.

## Windows smoke tests

1. Build and launch VoiSee 10.1.0.
2. Confirm Default Dark matches the accepted 9.2.7 appearance.
3. Create a new theme and verify `.voiseetheme.xaml` is produced and opened.
4. Change `VoiSee.AppBackgroundBrush`, save, and confirm live reload.
5. Change timeline and panel brushes and verify affected UI.
6. Switch all main tabs rapidly and confirm no Default Dark flash or lag.
7. Break the active XAML intentionally and confirm the current working theme remains visible.
8. Repair the XAML and confirm live reload resumes.
9. Rename the active theme and confirm watching continues on the new path.
10. Delete the active theme and confirm fallback to Default Dark.
11. Put an old `.voiseetheme.css` file in the themes root and restart; confirm it is moved to `Legacy CSS` and not loaded.
12. Re-run the full VoiSee 9.2.7 regression checklist for audio, SoundBoard, Scenes, hotkeys and Sound Editor.
