using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.UI;

namespace VoiSe.App;

public sealed class ThemeManager
{
    private static readonly Regex RuleRegex = new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    public ThemeManager(string dataDirectory)
    {
        ThemesDirectory = Path.Combine(dataDirectory, "themes");
    }

    public string ThemesDirectory { get; }

    public void EnsureThemesDirectory()
    {
        Directory.CreateDirectory(ThemesDirectory);
    }

    public IReadOnlyList<string> GetThemeFiles()
    {
        EnsureThemesDirectory();
        return Directory.GetFiles(ThemesDirectory, "*.voiseetheme.css", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string CreateNewThemeFile()
    {
        EnsureThemesDirectory();
        var basePath = Path.Combine(ThemesDirectory, "MyTheme.voiseetheme.css");
        var path = basePath;
        var index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(ThemesDirectory, $"MyTheme {index}.voiseetheme.css");
            index++;
        }

        File.WriteAllText(path, CreateTemplateCss("My Theme"), Encoding.UTF8);
        return path;
    }

    public string ImportTheme(string sourcePath)
    {
        EnsureThemesDirectory();
        var fileName = Path.GetFileName(sourcePath);
        if (!fileName.EndsWith(".voiseetheme.css", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName) + ".voiseetheme.css";
        }

        var targetPath = Path.Combine(ThemesDirectory, fileName);
        if (File.Exists(targetPath))
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
            var index = 2;
            do
            {
                targetPath = Path.Combine(ThemesDirectory, $"{name} {index}.voiseetheme.css");
                index++;
            }
            while (File.Exists(targetPath));
        }

        File.Copy(sourcePath, targetPath);
        return targetPath;
    }

    public string ExportTheme(string? activeThemePath, string targetPath)
    {
        var css = !string.IsNullOrWhiteSpace(activeThemePath) && File.Exists(activeThemePath)
            ? File.ReadAllText(activeThemePath)
            : CreateTemplateCss("Default Dark");
        File.WriteAllText(targetPath, css, Encoding.UTF8);
        return targetPath;
    }

    public VoiSeeCssTheme LoadTheme(string? themePath)
    {
        if (string.IsNullOrWhiteSpace(themePath) || !File.Exists(themePath))
        {
            return Parse(CreateTemplateCss("Default Dark"), "Default Dark", null);
        }

        var css = File.ReadAllText(themePath);
        return Parse(css, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(themePath)), themePath);
    }

    public VoiSeeCssTheme Parse(string css, string fallbackName, string? sourcePath)
    {
        css = CommentRegex.Replace(css ?? string.Empty, string.Empty);
        var theme = new VoiSeeCssTheme(fallbackName, sourcePath);

        foreach (Match match in RuleRegex.Matches(css))
        {
            var selectorList = match.Groups["selector"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var declarations = ParseDeclarations(match.Groups["body"].Value);
            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var selector in selectorList)
            {
                var normalizedSelector = selector.Trim();
                if (string.IsNullOrWhiteSpace(normalizedSelector))
                {
                    continue;
                }

                if (normalizedSelector.Equals(":root", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var pair in declarations)
                    {
                        if (pair.Key.StartsWith("--", StringComparison.Ordinal))
                        {
                            theme.Variables[pair.Key] = pair.Value;
                        }
                    }
                }
                else
                {
                    theme.Rules.Add(new VoiSeeCssRule(normalizedSelector, new Dictionary<string, string>(declarations, StringComparer.OrdinalIgnoreCase)));
                }
            }
        }

        return theme;
    }

    private static Dictionary<string, string> ParseDeclarations(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawDeclaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = rawDeclaration.IndexOf(':');
            if (separator <= 0 || separator >= rawDeclaration.Length - 1)
            {
                continue;
            }

            var name = rawDeclaration[..separator].Trim();
            var value = rawDeclaration[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                result[name] = value;
            }
        }

        return result;
    }

    public int ApplyTheme(FrameworkElement root, VoiSeeCssTheme theme)
    {
        var applied = 0;
        foreach (var element in EnumerateVisualTree(root).OfType<FrameworkElement>())
        {
            applied += ApplyRulesToElement(element, theme);
        }

        return applied;
    }

    private int ApplyRulesToElement(FrameworkElement element, VoiSeeCssTheme theme)
    {
        var applied = 0;
        foreach (var rule in theme.Rules)
        {
            if (!IsSelectorMatch(element, rule.Selector))
            {
                continue;
            }

            foreach (var declaration in rule.Declarations)
            {
                if (ApplyDeclaration(element, declaration.Key, theme.ResolveValue(declaration.Value)))
                {
                    applied++;
                }
            }
        }

        return applied;
    }

    private static bool IsSelectorMatch(FrameworkElement element, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith("#", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(element.Name) && element.Name.Equals(selector[1..], StringComparison.OrdinalIgnoreCase);
        }

        if (selector.StartsWith(".", StringComparison.Ordinal))
        {
            return GetElementClasses(element).Contains(selector[1..], StringComparer.OrdinalIgnoreCase);
        }

        return element.GetType().Name.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetElementClasses(FrameworkElement element)
    {
        yield return "all";

        if (element is Grid or StackPanel or ScrollViewer)
        {
            yield return "layout";
        }

        if (element is Border)
        {
            yield return "panel";
        }

        if (element is TextBlock)
        {
            yield return "text";
        }

        if (element is Button)
        {
            yield return "button";
            yield return "primary-button";
        }

        if (element is ToggleButton)
        {
            yield return "button";
            yield return "toggle-button";
        }

        if (element is ComboBox)
        {
            yield return "combo-box";
        }

        if (element is Slider)
        {
            yield return "slider";
        }

        if (element is TabView)
        {
            yield return "tab-view";
        }

        if (element is TabViewItem)
        {
            yield return "tab-item";
        }

        if (element is HyperlinkButton)
        {
            yield return "link-button";
        }

        if (!string.IsNullOrWhiteSpace(element.Name))
        {
            if (element.Name.Contains("Banner", StringComparison.OrdinalIgnoreCase))
            {
                yield return "status-banner";
            }

            if (element.Name.Contains("Notice", StringComparison.OrdinalIgnoreCase) || element.Name.Contains("Panel", StringComparison.OrdinalIgnoreCase))
            {
                yield return "panel";
            }
        }
    }

    private static bool ApplyDeclaration(FrameworkElement element, string property, string value)
    {
        try
        {
            switch (property.Trim().ToLowerInvariant())
            {
                case "background":
                    return ApplyBackground(element, value);
                case "foreground":
                    return ApplyForeground(element, value);
                case "border-color":
                    return ApplyBorderBrush(element, value);
                case "border-thickness":
                    return ApplyBorderThickness(element, value);
                case "corner-radius":
                    return ApplyCornerRadius(element, value);
                case "opacity":
                    if (TryParseDouble(value, out var opacity))
                    {
                        element.Opacity = Math.Clamp(opacity, 0.0, 1.0);
                        return true;
                    }
                    return false;
                case "font-size":
                    return ApplyFontSize(element, value);
                case "font-weight":
                    return ApplyFontWeight(element, value);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool ApplyBackground(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case Panel panel:
                panel.Background = brush;
                return true;
            case Border border:
                border.Background = brush;
                return true;
            case Control control:
                control.Background = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyForeground(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Foreground = brush;
                return true;
            case Control control:
                control.Foreground = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyBorderBrush(FrameworkElement element, string value)
    {
        if (!TryParseBrush(value, out var brush)) return false;
        switch (element)
        {
            case Border border:
                border.BorderBrush = brush;
                return true;
            case Control control:
                control.BorderBrush = brush;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyBorderThickness(FrameworkElement element, string value)
    {
        if (!TryParseThickness(value, out var thickness)) return false;
        switch (element)
        {
            case Border border:
                border.BorderThickness = thickness;
                return true;
            case Control control:
                control.BorderThickness = thickness;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyCornerRadius(FrameworkElement element, string value)
    {
        if (!TryParseCornerRadius(value, out var radius)) return false;
        if (element is Border border)
        {
            border.CornerRadius = radius;
            return true;
        }

        return false;
    }

    private static bool ApplyFontSize(FrameworkElement element, string value)
    {
        if (!TryParseDouble(value, out var size) || size <= 0) return false;
        switch (element)
        {
            case TextBlock textBlock:
                textBlock.FontSize = size;
                return true;
            case Control control:
                control.FontSize = size;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyFontWeight(FrameworkElement element, string value)
    {
        var weight = value.Trim().ToLowerInvariant() switch
        {
            "bold" => Microsoft.UI.Text.FontWeights.Bold,
            "semibold" or "semi-bold" => Microsoft.UI.Text.FontWeights.SemiBold,
            "light" => Microsoft.UI.Text.FontWeights.Light,
            _ => Microsoft.UI.Text.FontWeights.Normal
        };

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.FontWeight = weight;
                return true;
            case Control control:
                control.FontWeight = weight;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseColor(string value, out Color color)
    {
        value = value.Trim();
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(0, 0, 0, 0);
            return true;
        }
        if (value.Equals("black", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(255, 0, 0, 0);
            return true;
        }
        if (value.Equals("white", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromArgb(255, 255, 255, 255);
            return true;
        }

        if (!value.StartsWith("#", StringComparison.Ordinal))
        {
            color = default;
            return false;
        }

        var hex = value[1..];
        if (hex.Length == 6)
        {
            color = Color.FromArgb(255, byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
            return true;
        }
        if (hex.Length == 8)
        {
            color = Color.FromArgb(byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber), byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber));
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryParseBrush(string value, out SolidColorBrush brush)
    {
        if (TryParseColor(value, out var color))
        {
            brush = new SolidColorBrush(color);
            return true;
        }

        brush = null!;
        return false;
    }

    private static bool TryParseDouble(string value, out double result)
    {
        return double.TryParse(value.Trim().Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseThickness(string value, out Thickness thickness)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && TryParseDouble(parts[0], out var all))
        {
            thickness = new Thickness(all);
            return true;
        }
        if (parts.Length == 4 && TryParseDouble(parts[0], out var left) && TryParseDouble(parts[1], out var top) && TryParseDouble(parts[2], out var right) && TryParseDouble(parts[3], out var bottom))
        {
            thickness = new Thickness(left, top, right, bottom);
            return true;
        }

        thickness = default;
        return false;
    }

    private static bool TryParseCornerRadius(string value, out CornerRadius radius)
    {
        if (TryParseDouble(value, out var all))
        {
            radius = new CornerRadius(all);
            return true;
        }

        radius = default;
        return false;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in EnumerateVisualTree(child))
            {
                yield return descendant;
            }
        }
    }

    public string CreateTemplateCss(string themeName)
    {
        return $$"""
/*
  VoiSee Theme CSS
  Theme: {{themeName}}

  Supported properties:
    background, foreground, border-color, border-thickness, corner-radius,
    opacity, font-size, font-weight

  Supported colors:
    #RRGGBB, #AARRGGBB, black, white, transparent

  VoiSee supports CSS-like variables in :root and a safe subset of selectors.
  It does not support scripts, url(), external resources, layout CSS, or arbitrary code.
*/

:root {
  --app-background: #000000;
  --titlebar-background: #000000;
  --panel-background: #22141414;
  --panel-background-soft: #08000000;
  --panel-border: #22FFFFFF;
  --text-primary: #FFFFFF;
  --text-secondary: #B8FFFFFF;
  --accent: #00D5FF;
  --danger: #FF4D4D;
  --warning: #D68B00;
  --success: #37D67A;
  --button-background: #202020;
  --button-border: #44FFFFFF;
  --corner-radius: 12;
}

#RootGrid {
  background: var(--app-background);
}

#CustomTitleBar {
  background: var(--titlebar-background);
}

#MainHeaderBorder,
#MainTabHostBorder,
#VBCableNoticeBorder,
#SettingsAppearancePanel,
#AboutMePanel {
  background: var(--panel-background);
  border-color: var(--panel-border);
  border-thickness: 1;
  corner-radius: var(--corner-radius);
}

#VirtualMicMutedBanner {
  background: #44220000;
  border-color: #88FF4D4D;
  border-thickness: 1;
  corner-radius: 10;
}

#MainTitle,
#TitleBarVersionTextBlock,
.text {
  foreground: var(--text-primary);
}

#VirtualMicMuteStatusTextBlock {
  foreground: var(--success);
}

.button {
  background: var(--button-background);
  foreground: var(--text-primary);
  border-color: var(--button-border);
  corner-radius: 8;
}

.link-button {
  foreground: var(--accent);
}

.combo-box {
  background: #181818;
  foreground: var(--text-primary);
  border-color: var(--button-border);
  corner-radius: 8;
}

.tab-view {
  background: transparent;
}
""";
    }
}

public sealed class VoiSeeCssTheme
{
    public VoiSeeCssTheme(string name, string? sourcePath)
    {
        Name = name;
        SourcePath = sourcePath;
    }

    public string Name { get; }
    public string? SourcePath { get; }
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<VoiSeeCssRule> Rules { get; } = new();

    public string ResolveValue(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("var(", StringComparison.OrdinalIgnoreCase) || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return value;
        }

        var name = value[4..^1].Trim();
        return Variables.TryGetValue(name, out var resolved) ? resolved.Trim() : value;
    }

    public bool TryGetVariableColor(string variableName, out Color color)
    {
        if (Variables.TryGetValue(variableName, out var value))
        {
            return ThemeManager.TryParseColor(ResolveValue(value), out color);
        }

        color = default;
        return false;
    }
}

public sealed record VoiSeeCssRule(string Selector, Dictionary<string, string> Declarations);
