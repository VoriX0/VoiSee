# Static smoke report — VoiSee 10.1.5

## Result

**PASS — 44/44 checks** before packaging, followed by ZIP integrity verification.

## Verified

- version 10.1.5 is synchronized across the application, project, installer and build script;
- `DefaultDark.xaml`, the editable user-theme template, application XAML and project XML are well formed;
- all C# sources parse without syntax errors;
- the default theme and new-theme template contain no duplicate resource keys;
- the complete VoiSee 10.1 editable resource catalogue remains present;
- every semantic `VoiSee.Color.*` resource is connected to a brush, style or state;
- the new-theme template remains an exact editable copy of Default Dark apart from its explanatory header;
- all former opaque blue-grey input and ComboBox surface colors were removed;
- TextBox and ComboBox normal, hover, pressed, focused and disabled surfaces use neutral WinUI-dark values;
- borderless buttons and inputs, white slider hover thumb, square CheckBox, rounded ComboBox items and popup, and neutral dropdown states from 10.1.4 remain intact;
- intentional accent selection for the scene list remains intact;
- `Open Theme File` remains disabled for protected Default Dark;
- no `bin`, `obj` or user-generated settings, libraries, presets, scenes or sounds are included.

## Changed runtime scope

- version metadata;
- `DefaultDark.xaml`;
- `UserThemeTemplate.voiseetheme.template`;
- documentation only.

No audio, SoundBoard, Voice Changer DSP, scene behavior, hotkey, Sound Editor, scrolling or persistence logic was changed.

## Windows validation

A full WinUI build and visual runtime test still require Windows:

```powershell
dotnet clean .\src\VoiSe.App\VoiSe.App.csproj
dotnet build .\src\VoiSe.App\VoiSe.App.csproj -c Release
dotnet run --project .\src\VoiSe.App
```
