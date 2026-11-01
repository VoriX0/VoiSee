using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Windows.UI;

namespace VoiSe.App;

/// <summary>
/// VoiSee 12.2.0 native theme loader and exact Default Dark copy workflow.
///
/// User themes are ordinary WinUI ResourceDictionary XAML files. The manager
/// validates a candidate dictionary before it atomically replaces the active
/// user dictionary in Application.Resources.MergedDictionaries. There is no
/// CSS parser, selector engine, visual-tree traversal, or CSS-to-XAML mapping.
/// </summary>
public sealed class ThemeManager
{
    public const string ThemeExtension = ".voiseetheme.xaml";

    private static readonly string[] RequiredSemanticKeys =
    {
        "VoiSee.AppBackgroundBrush",
        "VoiSee.PanelBackgroundBrush",
        "VoiSee.PrimaryTextBrush",
        "VoiSee.AccentBrush"
    };

    private ResourceDictionary? _activeUserDictionary;

    public ThemeManager(string dataDirectory)
    {
        ThemesDirectory = Path.Combine(dataDirectory, "themes");
        LegacyCssDirectory = Path.Combine(ThemesDirectory, "Legacy CSS");
    }

    public string ThemesDirectory { get; }
    public string LegacyCssDirectory { get; }

    public void EnsureThemesDirectory()
    {
        Directory.CreateDirectory(ThemesDirectory);
    }

    public ThemeMigrationResult MigrateLegacyCssThemes(string? activeThemePath)
    {
        EnsureThemesDirectory();
        var legacyFiles = Directory.GetFiles(ThemesDirectory, "*.voiseetheme.css", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(ThemesDirectory, "*.css", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (legacyFiles.Length == 0)
        {
            return new ThemeMigrationResult(0, false, LegacyCssDirectory);
        }

        Directory.CreateDirectory(LegacyCssDirectory);
        foreach (var sourcePath in legacyFiles)
        {
            var targetPath = BuildUniquePath(LegacyCssDirectory, Path.GetFileName(sourcePath));
            File.Move(sourcePath, targetPath);
        }

        var activeWasLegacyCss = !string.IsNullOrWhiteSpace(activeThemePath)
            && activeThemePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

        return new ThemeMigrationResult(legacyFiles.Length, activeWasLegacyCss, LegacyCssDirectory);
    }

    public IReadOnlyList<string> GetThemeFiles()
    {
        EnsureThemesDirectory();
        return Directory.GetFiles(ThemesDirectory, $"*{ThemeExtension}", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreateNewThemeFile()
    {
        EnsureThemesDirectory();
        var path = BuildUniquePath(ThemesDirectory, $"MyTheme{ThemeExtension}");
        File.WriteAllText(path, CreateDefaultDarkCopyXaml("My Theme"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public string ImportTheme(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(UiText.L("Theme_file_was_not_found_64023f6d"), sourcePath);
        }

        EnsureThemesDirectory();
        var sourceFileName = Path.GetFileName(sourcePath);
        var destinationName = sourceFileName.EndsWith(ThemeExtension, StringComparison.OrdinalIgnoreCase)
            ? sourceFileName
            : Path.GetFileNameWithoutExtension(sourceFileName) + ThemeExtension;

        var targetPath = BuildUniquePath(ThemesDirectory, destinationName);
        File.Copy(sourcePath, targetPath);

        // Validate the imported copy before returning it to the UI.
        _ = LoadTheme(targetPath);
        return targetPath;
    }

    public string ExportTheme(string? activeThemePath, string targetPath)
    {
        if (!string.IsNullOrWhiteSpace(activeThemePath) && File.Exists(activeThemePath))
        {
            File.Copy(activeThemePath, targetPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(targetPath, CreateDefaultDarkCopyXaml("Default Dark copy"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return targetPath;
    }

    public VoiSeeXamlTheme LoadTheme(string? themePath)
    {
        if (string.IsNullOrWhiteSpace(themePath))
        {
            return VoiSeeXamlTheme.DefaultDark;
        }

        if (!File.Exists(themePath))
        {
            throw new FileNotFoundException(UiText.L("The_selected_XAML_theme_file_was_not_found_9258e3c7"), themePath);
        }

        if (!themePath.EndsWith(ThemeExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(UiText.L("VoiSee_themes_must_use_the_0_extension_95d956c5", ThemeExtension));
        }

        var xaml = ReadAllTextWithRetry(themePath);
        object parsed;
        try
        {
            parsed = XamlReader.Load(xaml);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(UiText.L("The_theme_is_not_valid_WinUI_XAML_0_e5691ea4", ex.Message), ex);
        }

        if (parsed is not ResourceDictionary dictionary)
        {
            throw new InvalidDataException(UiText.L("The_root_object_of_a_VoiSee_theme_must_be_Reso_e0bd892b"));
        }

        ValidateSemanticContract(dictionary);
        var name = GetDisplayName(themePath);
        return new VoiSeeXamlTheme(name, Path.GetFullPath(themePath), dictionary);
    }

    public int ApplyTheme(FrameworkElement root, VoiSeeXamlTheme theme)
    {
        var merged = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries;

        // Candidate XAML has already been parsed and validated by LoadTheme.
        // Add it before removing the previous dictionary so a failure cannot
        // leave the application without its last working theme.
        if (theme.Dictionary is not null)
        {
            merged.Add(theme.Dictionary);
        }

        if (_activeUserDictionary is not null)
        {
            merged.Remove(_activeUserDictionary);
        }

        _activeUserDictionary = theme.Dictionary;
        RefreshThemeResources(root);
        return theme.Dictionary is null ? 0 : CountResources(theme.Dictionary);
    }

    public void RefreshThemeResources(FrameworkElement root)
    {
        // ThemeResource expressions are re-evaluated when RequestedTheme
        // changes. Both assignments happen synchronously before the next frame,
        // avoiding the old CSS restore/repaint flash on tab switches.
        var targetTheme = root.RequestedTheme == ElementTheme.Light
            ? ElementTheme.Light
            : ElementTheme.Dark;
        root.RequestedTheme = targetTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
        root.RequestedTheme = targetTheme;
    }

    public bool TryGetColor(string resourceKey, out Color color)
    {
        if (_activeUserDictionary is not null && TryLookupResource(_activeUserDictionary, resourceKey, out var userValue) && TryConvertColor(userValue, out color))
        {
            return true;
        }

        if (TryLookupResource(Microsoft.UI.Xaml.Application.Current.Resources, resourceKey, out var applicationValue) && TryConvertColor(applicationValue, out color))
        {
            return true;
        }

        color = default;
        return false;
    }

    public static string GetDisplayName(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(ThemeExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ThemeExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    public static string CreateDefaultDarkCopyXaml(string themeName)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Themes", "UserThemeTemplate.voiseetheme.template");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(UiText.L("The_built_in_editable_copy_of_Default_Dark_was_5b1673e7"), templatePath);
        }

        var template = File.ReadAllText(templatePath, Encoding.UTF8);
        return template.Replace("__THEME_NAME__", string.IsNullOrWhiteSpace(themeName) ? "My Theme" : themeName.Trim(), StringComparison.Ordinal);
    }

    private static void ValidateSemanticContract(ResourceDictionary dictionary)
    {
        var missing = RequiredSemanticKeys
            .Where(key => !TryLookupResource(dictionary, key, out _))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(UiText.L("The_XAML_theme_is_missing_required_resources_826b4dcb") + string.Join(", ", missing));
        }

        foreach (var key in RequiredSemanticKeys)
        {
            if (!TryLookupResource(dictionary, key, out var value) || value is not Brush)
            {
                throw new InvalidDataException(UiText.L("Theme_resource_0_must_be_a_Brush_793a9ac8", key));
            }
        }
    }

    private static bool TryLookupResource(ResourceDictionary dictionary, string key, out object? value)
    {
        if (dictionary.ContainsKey(key))
        {
            value = dictionary[key];
            return true;
        }

        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (TryLookupResource(dictionary.MergedDictionaries[i], key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryConvertColor(object? value, out Color color)
    {
        if (value is SolidColorBrush brush)
        {
            color = brush.Color;
            return true;
        }

        if (value is Color directColor)
        {
            color = directColor;
            return true;
        }

        color = default;
        return false;
    }

    private static int CountResources(ResourceDictionary dictionary)
    {
        var count = dictionary.Count;
        foreach (var merged in dictionary.MergedDictionaries)
        {
            count += CountResources(merged);
        }

        return count;
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var extension = fileName.EndsWith(ThemeExtension, StringComparison.OrdinalIgnoreCase)
            ? ThemeExtension
            : Path.GetExtension(fileName);
        var baseName = fileName.EndsWith(ThemeExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ThemeExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        var index = 2;
        do
        {
            candidate = Path.Combine(directory, $"{baseName} {index}{extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private static string ReadAllTextWithRetry(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException ex)
            {
                lastError = ex;
                System.Threading.Thread.Sleep(35 * (attempt + 1));
            }
        }

        throw new IOException(UiText.L("The_theme_file_is_temporarily_locked_and_could_faf335d1"), lastError);
    }
}

public sealed record VoiSeeXamlTheme(string Name, string? SourcePath, ResourceDictionary? Dictionary)
{
    public static VoiSeeXamlTheme DefaultDark { get; } = new("Default Dark", null, null);
}

public sealed record ThemeMigrationResult(int MovedFileCount, bool ActiveThemeWasLegacyCss, string LegacyDirectory);
