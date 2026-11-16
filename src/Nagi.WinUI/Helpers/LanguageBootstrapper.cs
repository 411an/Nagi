using System.Globalization;
using System.Text.Json;
using Nagi.Core.Helpers;

namespace Nagi.WinUI.Helpers;

/// <summary>
///     Applies the saved UI language before WinUI initializes.
/// </summary>
public static class LanguageBootstrapper
{
    public static void Bootstrap()
    {
        string? language = null;
        try
        {
            if (AppDataLocator.IsResolved)
            {
                var settingsPath = Path.Combine(AppDataLocator.Root, "settings.json");
                language = TryReadAppLanguage(settingsPath);
            }
        }
        catch (Exception)
        {
            // Datadir may not exist yet on first run.
        }

        if (!string.IsNullOrEmpty(language))
        {
            try
            {
                var culture = new CultureInfo(language);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
            }
            catch (CultureNotFoundException)
            {
                return;
            }

            TrySetWinRtLanguageOverride(language);
        }
        else
        {
            TrySetWinRtLanguageOverride(string.Empty);
        }
    }

    private static string? TryReadAppLanguage(string settingsPath)
    {
        if (!File.Exists(settingsPath)) return null;

        using var stream = File.OpenRead(settingsPath);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("AppLanguage", out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static void TrySetWinRtLanguageOverride(string language)
    {
        try
        {
            // The Windows App SDK setter also works without package identity, so the
            // portable build gets built-in control texts in the chosen language too.
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            return;
        }
        catch (Exception)
        {
            // Fall back to the packaged-only API below.
        }

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception)
        {
            // Unpackaged apps have no package identity for this WinRT setter.
        }
    }
}
