using System;
using System.Linq;
using System.Reflection;

namespace Downio.Services;

public static class AppVersionProvider
{
    public static string GetCurrentVersion()
    {
        if (!string.IsNullOrWhiteSpace(BuildInfo.Version))
        {
            return NormalizeVersion(BuildInfo.Version);
        }

        var assembly = typeof(AppVersionProvider).Assembly;
        var info = assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(info))
        {
            return NormalizeVersion(info);
        }

        var nameVersion = assembly.GetName().Version;
        if (nameVersion is not null)
        {
            return new Version(nameVersion.Major, nameVersion.Minor, nameVersion.Build).ToString();
        }

        return "0.0.0";
    }

    private static string NormalizeVersion(string version) =>
        version.Trim().TrimStart('v', 'V').Split('+', 2)[0];
}
