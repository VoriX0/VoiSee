# Gate 9.1 — CSS-like Theme Engine

Gate 9.1 adds a safe CSS-like theme system for VoiSee.

## Version

- App display version: `VoiSee Version 9.1.1`
- Installer output: `VoiSee-Setup-9.1-x64.exe`
- Portable output: `VoiSee-Portable-9.1-x64.zip`

## User workflow

Themes live in:

```text
%LOCALAPPDATA%\VoiSe\themes\
```

Settings now contains an `Appearance / Themes` panel:

- Current theme selector
- Create New Theme
- Open Theme File
- Import Theme
- Export Current Theme
- Reset

When the user clicks `Create New Theme`, VoiSee creates a `.voiseetheme.css` template file, applies it, opens it in the default editor, and watches the file. Every save reloads the theme without restarting the app.

## Theme format

Theme files use the extension:

```text
*.voiseetheme.css
```

This is not a full browser CSS engine. It is a safe subset parsed by VoiSee.

Supported selectors:

- `:root` variables
- `#ElementName`
- `.class`
- control type names, for example `Button`, `TextBlock`, `Border`

Supported properties:

- `background`
- `foreground`
- `border-color`
- `border-thickness`
- `corner-radius`
- `opacity`
- `font-size`
- `font-weight`

Supported colors:

- `#RRGGBB`
- `#AARRGGBB`
- `black`
- `white`
- `transparent`

Unsupported on purpose:

- scripts
- `url()`
- external files
- arbitrary layout CSS
- animations
- web/network loading

## Important UX decision

The theme engine is CSS-like but not browser CSS. This keeps themes shareable and editable while preventing theme files from executing code or loading external resources.
