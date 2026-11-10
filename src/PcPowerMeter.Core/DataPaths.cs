using System.Security.Cryptography;
using System.Text;

namespace PcPowerMeter.Core;

/// <summary>Path helpers for the data folder and the one-instance-per-history lock.</summary>
public static class DataPaths
{
    /// <summary>Characters Windows never allows in a path ('?' is left out for "\\?\" prefixes).</summary>
    private static readonly char[] InvalidChars =
        Path.GetInvalidPathChars().Concat(new[] { '"', '<', '>', '|', '*' }).Distinct().ToArray();

    /// <summary>
    /// Full path without a trailing separator, so "X", "X\" and ".\X" mean the same folder.
    /// Throws <see cref="ArgumentException"/> or <see cref="NotSupportedException"/> for an invalid path.
    /// </summary>
    public static string NormalizeFolder(string folder)
    {
        // .NET no longer rejects these itself. A '"' typically comes from a command line such
        // as --data-dir "C:\X\" where the shell reads \" as a literal quote.
        if (folder.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ArgumentException("The path contains invalid characters.", nameof(folder));
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
    }

    /// <summary>
    /// Name of the named mutex that allows one running instance per usage history file.
    /// Windows paths are case-insensitive, so the name is too: any two spellings of the same
    /// file (default location, "--data-dir" with or without a trailing '\', other case) get
    /// the same lock.
    /// </summary>
    public static string InstanceLockName(string usageFilePath)
    {
        string normalized = Path.GetFullPath(usageFilePath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..32];
        return @"Local\PcPowerMeter.History." + hash;
    }
}
