using System;

namespace Downio.Services.TaskbarBadge;

internal static class TaskbarBadgeTextFormatter
{
    public static string Format(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        const double gigabyte = megabyte * 1024d;

        if (bytesPerSecond < megabyte)
        {
            var kilobytes = bytesPerSecond / kilobyte;
            return kilobytes < 1000 ? $"{Math.Max(1, Math.Round(kilobytes)):0} K/s" : "1 M/s";
        }

        if (bytesPerSecond < gigabyte)
        {
            var megabytes = bytesPerSecond / megabyte;
            return megabytes < 1000
                ? megabytes < 10 ? $"{megabytes:0.#} M/s" : $"{megabytes:0} M/s"
                : "1 G/s";
        }

        var gigabytes = bytesPerSecond / gigabyte;
        return gigabytes < 10 ? $"{gigabytes:0.#} G/s" : $"{gigabytes:0} G/s";
    }

    public static string FormatOverlay(long bytesPerSecond)
    {
        if (bytesPerSecond < 1024 * 1024)
        {
            return $"{Math.Max(1, Math.Round(bytesPerSecond / 1024d)):0}K";
        }

        if (bytesPerSecond < 1024L * 1024 * 1024)
        {
            var megabytes = bytesPerSecond / (1024d * 1024d);
            return megabytes < 10 ? $"{megabytes:0.#}M" : $"{megabytes:0}M";
        }

        var gigabytes = bytesPerSecond / (1024d * 1024d * 1024d);
        return gigabytes < 10 ? $"{gigabytes:0.#}G" : $"{gigabytes:0}G";
    }

    public static string FormatDescription(long bytesPerSecond)
    {
        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;
        const double gigabyte = megabyte * 1024d;

        if (bytesPerSecond < kilobyte)
        {
            return $"{bytesPerSecond} B/s";
        }

        if (bytesPerSecond < megabyte)
        {
            return $"{bytesPerSecond / kilobyte:0.0} KB/s";
        }

        if (bytesPerSecond < gigabyte)
        {
            return $"{bytesPerSecond / megabyte:0.0} MB/s";
        }

        return $"{bytesPerSecond / gigabyte:0.0} GB/s";
    }
}
