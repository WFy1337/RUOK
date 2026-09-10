using System.Globalization;
using System.Resources;

namespace RUOK_App.Resources;

public static class UiText
{
    private static readonly ResourceManager Manager = new("RUOK_App.Resources.UiStrings", typeof(UiText).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
