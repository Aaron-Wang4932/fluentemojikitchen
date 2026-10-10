using System;
using System.IO;
namespace FluentEmojiKitchen.Services;

public static class CachePolicy
{
    public static bool MonthlyExpired(DateTime writtenUtc, DateTime nowUtc) =>
        writtenUtc == default || nowUtc >= writtenUtc.AddMonths(1);
    public static bool MonthlyExpired(string path) =>
        !File.Exists(path) || MonthlyExpired(File.GetLastWriteTimeUtc(path), DateTime.UtcNow);
    public static bool ImageExpired(string path) =>
        !File.Exists(path) || DateTime.UtcNow >= File.GetLastWriteTimeUtc(path).AddDays(7);
}
