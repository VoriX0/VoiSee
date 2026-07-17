# VoiSee 10.4.0 — Settings redesign and single-click tray restore

## Scope

This gate starts the VoiSee 10.4 Settings redesign on top of the accepted
10.3.0 buildfix 1 baseline.

## Tray behavior

The notification-area icon now restores the existing VoiSee window with one
left mouse click. Right click still opens the tray context menu with
`Open VoiSee` and `Exit VoiSee`. The former double-click-only handler was
removed so a normal single click is sufficient.

## Settings page

Settings remains a three-column page at normal application width.

### System & Audio

The first column now groups the controls that are used regularly:

- VB-CABLE status and installation/refresh actions;
- Input Device;
- Virtual Output;
- Monitor / Headphones;
- Virtual Mic Master;
- SoundBoard Delay;
- transport/global hotkey configuration and conflict priority explanation;
- actual Windows autostart state;
- one `Open Advanced Settings` action.

The manual engine controls and log launcher are no longer visible on the main
Settings page. Hidden compatibility controls keep the established engine state
code and generated XAML fields intact, but they cannot be reached by the user.

### Advanced Settings

A wide centered modal contains two independently scrollable areas:

- **Engine Manual Control**: current status, Start, Stop, Restart, Refresh
  Devices, selected Input/Virtual Output/Monitor devices, route readiness, and
  current Virtual Mic state;
- **Logs**: existing log output, automatic scrolling, Clear, Copy, and Export.

The log viewer stays synchronized while the dialog is open.

### Themes

The second column now uses only native WinUI XAML terminology. It explains
`.voiseetheme.xaml` ResourceDictionary files, live reload and invalid-XAML
fallback, shows the actual user themes directory, and can open the bundled
full theme template in Notepad.

### About me

The Telegram target is now:

```text
https://t.me/VoriXdev
```

GitHub and CloudTips links are preserved.

## Adaptive layout

At widths of 1040 DIPs or more the three columns remain side by side. Below
that threshold System & Audio, Themes and About me are placed into three
vertical rows in the same Settings ScrollViewer. Horizontal scrolling stays
disabled, preventing clipped columns on smaller windows.

## Preserved behavior

- external Explorer import overlay remains active;
- SoundBoard context-menu Move/Copy remains the category-management path;
- internal sound-to-category dragging remains removed;
- autostart, single instance, real Exit, audio routing, hotkeys, scenes,
  themes and Sound Editor behavior are otherwise unchanged.

## Validation limitation

The available environment does not contain the Windows .NET SDK or WinUI XAML
compiler. XML/XAML structure, C# syntax trees, event-handler wiring, version
synchronization and archive contents are checked statically. A real Windows
build and visual/runtime test is still required.
