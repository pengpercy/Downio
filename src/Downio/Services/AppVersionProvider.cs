using System;
using System.Linq;
using System.Reflection;

namespace Downio.Services;

public static class AppVersionProvider
{
    public static string GetCurrentVersion()
    {
        if (!string.IsNullOrWhiteSpace(BuildInfo.Version) &&
            Version.TryParse(BuildInfo.Version.TrimStart('v'), out var generated))
        {
            return generated.ToString();
        }

        var assembly = typeof(AppVersionProvider).Assembly;
        var info = assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(info))
        {
            var versionPart = info.Split('+', 2)[0];
            if (Version.TryParse(versionPart.TrimStart('v'), out var parsed))
            {
                return parsed.ToString();
            }
        }

        var nameVersion = assembly.GetName().Version;
        if (nameVersion is not null)
        {
            return new Version(nameVersion.Major, nameVersion.Minor, nameVersion.Build).ToString();
        }

        return "0.0.0";
    }
}
