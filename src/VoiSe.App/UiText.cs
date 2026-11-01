using System;
using System.Globalization;
using System.Resources;

namespace VoiSe.App;

internal static class UiText
{
    private static readonly ResourceManager Resources = new("VoiSe.App.Resources.Interface", typeof(UiText).Assembly);
    private static readonly ResourceManager Errors = new("VoiSe.App.Resources.Errors", typeof(UiText).Assembly);
    internal static string LanguageCode { get; private set; } = "ru";
    internal static string NormalizeLanguage(string? code) => code == "en" ? "en" : "ru";
    internal static void Initialize(string? code)
    {
        LanguageCode = NormalizeLanguage(code);
        var culture = CultureInfo.GetCultureInfo(LanguageCode);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    internal static string L(string key, params object?[] args)
    {
        var value = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
    internal static string EffectName(string id) => Resources.GetString("Effect_" + id, CultureInfo.CurrentUICulture) ?? id;
    internal static string CategoryName(string id) => Resources.GetString("Category_" + id, CultureInfo.CurrentUICulture) ?? id;
    internal static string IconName(string id) => Resources.GetString("Icon_" + id, CultureInfo.CurrentUICulture) ?? id;
    // Only known application diagnostics are translated, never arbitrary user text.
    // Windows/driver exception details retain their original wording.
    internal static string ErrorMessage(string message) => Errors.GetString(message, CultureInfo.CurrentUICulture) ?? message;
}

