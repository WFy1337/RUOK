using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Storage;

namespace RUOK_App;

internal static class AppRuntime
{
    private static readonly Lazy<nint> NotificationResources = new(() =>
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.Insights.Resource.dll")));

    public static bool IsPackaged { get; } = HasPackageIdentity();
    public static string InstanceKey => IsPackaged ? "RUOK.Main" : "RUOK.Standalone.Main";
    public static string DataPath => Path.Combine(IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : StandaloneDataRoot(),
        "Data", "ruok.db");

    public static string AssetPath(params string[] segments) =>
        Path.Combine([AppContext.BaseDirectory, "Assets", .. segments]);

    public static Uri AssetUri(string folder, string file) => IsPackaged
        ? new Uri($"ms-appx:///Assets/{folder}/{file}")
        : new Uri(AssetPath(folder, file));

    public static void EnsureNotificationResources()
    {
        // Native SDK lookups do not use .NET's bundle directory; retain this module until process exit.
        if (!IsPackaged)
            _ = NotificationResources.Value;
    }

    private static string StandaloneDataRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Windows did not provide an absolute local application data directory.");
        return Path.Combine(root, "RUOK", "Standalone");
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result switch
        {
            0 or 122 => true,
            15700 => false,
            _ => throw new Win32Exception(result, "Windows could not identify this application's storage context.")
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
