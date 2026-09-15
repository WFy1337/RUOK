using RUOK_App;

namespace RUOK.Infrastructure.Tests;

[TestClass]
public sealed class AppRuntimeTests
{
    [TestMethod]
    public void UnpackagedProcessUsesAnIndependentInstanceAndProfile()
    {
        Assert.IsFalse(AppRuntime.IsPackaged);
        Assert.AreEqual("RUOK.Standalone.Main", AppRuntime.InstanceKey);
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RUOK", "Standalone", "Data", "ruok.db"), AppRuntime.DataPath);
        Assert.IsTrue(Path.IsPathFullyQualified(AppRuntime.DataPath));
    }

    [TestMethod]
    public void AssetPathsAreRelativeToTheApplicationRatherThanTheWorkingDirectory()
    {
        Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            AppRuntime.AssetPath("AppIcon.ico"));
        Assert.IsTrue(Path.IsPathFullyQualified(AppRuntime.AssetPath("Nebula", "cloud.png")));
    }

    [TestMethod]
    public void StandaloneAssetUrisAddressExtractedLocalFiles()
    {
        var uri = AppRuntime.AssetUri("NotificationFaces", "Mood1.png");
        Assert.IsTrue(uri.IsAbsoluteUri);
        Assert.IsTrue(uri.IsFile);
        Assert.IsFalse(uri.IsUnc);
        Assert.AreEqual(AppRuntime.AssetPath("NotificationFaces", "Mood1.png"), uri.LocalPath);
    }
}
