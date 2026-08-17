using System.IO;
using DanbooruTagGen.Core.Persistence;

namespace DanbooruTagGen.App.Services;

public static class SettingsStore
{
    public static Settings Load() => JsonStore.LoadOrDefault(AppPaths.SettingsFile, new Settings());

    public static void Save(Settings settings)
    {
        AppPaths.EnsureAppDataDir();
        JsonStore.SaveAtomic(settings, AppPaths.SettingsFile);
    }

    public static IReadOnlyList<string> ResolveCsvPaths(Settings s)
        => !string.IsNullOrWhiteSpace(s.CustomCsvPath) && File.Exists(s.CustomCsvPath)
            ? new[] { s.CustomCsvPath }
            : AppPaths.BundledCsvFiles;
}
