using System;
using System.IO;

namespace Downio.Helpers;

/// <summary>
/// Where the read-only data that ships with Downio lives (CA bundle, ED2K bootstrap lists, licenses).
/// Inside a macOS bundle that is Contents/Resources; everywhere else it is the "resources"
/// folder next to the executable, which is also how build output and Windows/Linux packages are laid out.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> ResourcesDirectoryLazy = new(ResolveResourcesDirectory);

    public static string ResourcesDirectory => ResourcesDirectoryLazy.Value;

    public static string Resource(params string[] parts) => Path.Combine([ResourcesDirectory, .. parts]);

    private static string ResolveResourcesDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsMacOS() && Path.GetFileName(baseDirectory) == "MacOS")
        {
            var contents = Path.GetDirectoryName(baseDirectory);
            if (Path.GetFileName(contents) == "Contents" && contents != null)
            {
                return Path.Combine(contents, "Resources");
            }
        }

        return Path.Combine(baseDirectory, "resources");
    }
}
