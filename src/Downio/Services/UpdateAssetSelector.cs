using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Downio.Services;

public static class UpdateAssetSelector
{
    public static ReleaseAsset? SelectPreferredAsset(ReleaseInfo release)
    {
        var version = release.TagName.TrimStart('v');
        var arch = RuntimeInformation.ProcessArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var runtime = arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var exact = $"Downio_{version}.{runtime}.zip";
            return release.Assets.FirstOrDefault(a => a.Name.Equals(exact, StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith($".{runtime}.zip", StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var runtime = arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            var zipExact = $"Downio_{version}.{runtime}.zip";
            var dmgExact = $"Downio_{version}.{runtime}.dmg";

            return release.Assets.FirstOrDefault(a => a.Name.Equals(dmgExact, StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a => a.Name.Equals(zipExact, StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith($".{runtime}.dmg", StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith($".{runtime}.zip", StringComparison.OrdinalIgnoreCase));
        }

        var linuxRuntime = arch == Architecture.Arm64 ? "aarch64" : "x86_64";
        var appImageExact = $"Downio-{version}.linux.{linuxRuntime}.AppImage";
        return release.Assets.FirstOrDefault(a => a.Name.Equals(appImageExact, StringComparison.OrdinalIgnoreCase))
               ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith($".linux.{linuxRuntime}.AppImage", StringComparison.OrdinalIgnoreCase))
               ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
    }
}
