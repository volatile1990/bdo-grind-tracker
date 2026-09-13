using System.Security;
using System.Xml;

namespace BdoGrindTracker.Ocr;

/// <summary>A discovered configuration and the result of validating that exact file.</summary>
public sealed record CompanionCalibrationCandidate(
    string GameVariablePath,
    DateTime? LastWriteUtc,
    CompanionCalibration? Calibration,
    string? ValidationError)
{
    public bool IsValid => Calibration is not null && ValidationError is null;
}

public sealed partial class CompanionCalibrationReader
{
    /// <summary>
    /// Reads the selected XML and the supplied directory's GameOption.txt. A null
    /// selection retains automatic profile selection. Explicit selection never
    /// substitutes a different profile or character XML. A rare preset fallback
    /// can only come from the selected XML itself and is reported in RareLootResolution.
    /// </summary>
    public CompanionCalibration Read(string blackDesertDirectoryPath, string? gameVariablePath)
    {
        if (gameVariablePath is null) return Read(blackDesertDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(blackDesertDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVariablePath);
        var root = Path.GetFullPath(blackDesertDirectoryPath);
        var selected = Path.GetFullPath(gameVariablePath, root);
        return ReadConfiguration(root, Path.GetDirectoryName(selected)!, selected, includeActiveCharacter: false)
            with { ProfileSelection = "manual-file" };
    }

    /// <summary>
    /// Lists every gamevariable.xml below UserCache, including nested character
    /// configurations and invalid files. Inaccessible branches are skipped;
    /// one unreadable or invalid file cannot prevent the remaining results.
    /// The scan never ranks by access time or writes game configuration files.
    /// </summary>
    public IReadOnlyList<CompanionCalibrationCandidate> ScanCandidates(string blackDesertDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blackDesertDirectoryPath);
        var root = Path.GetFullPath(blackDesertDirectoryPath);
        var userCache = Path.Combine(root, "UserCache");
        if (!Directory.Exists(userCache)) return [];
        var candidates = new List<CompanionCalibrationCandidate>();
        foreach (var path in EnumerateConfigurationPaths(userCache))
        {
            DateTime? lastWrite = null;
            try
            {
                var information = new FileInfo(path);
                information.Refresh();
                if (!information.Exists) throw new FileNotFoundException("The configuration no longer exists.", path);
                lastWrite = information.LastWriteTimeUtc;
                candidates.Add(new(path, lastWrite, Read(root, path), null));
            }
            catch (Exception exception) when (IsConfigurationReadFailure(exception))
            {
                candidates.Add(new(path, lastWrite, null, exception.Message));
            }
        }
        return Array.AsReadOnly(candidates.OrderBy(candidate => candidate.GameVariablePath,
            StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> EnumerateConfigurationPaths(string userCache)
    {
        var pending = new Stack<string>();
        pending.Push(userCache);
        while (pending.TryPop(out var directory))
        {
            string[] files;
            string[] children;
            try { files = Directory.GetFiles(directory); }
            catch (Exception exception) when (IsDirectoryReadFailure(exception)) { files = []; }
            foreach (var file in files)
                if (string.Equals(Path.GetFileName(file), "gamevariable.xml", StringComparison.OrdinalIgnoreCase))
                    yield return Path.GetFullPath(file);

            try { children = Directory.GetDirectories(directory); }
            catch (Exception exception) when (IsDirectoryReadFailure(exception)) { children = []; }
            foreach (var child in children)
            {
                try
                {
                    // Do not follow directory links into cycles or a different
                    // game installation. Linked files themselves remain candidates.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception exception) when (IsDirectoryReadFailure(exception)) { }
            }
        }
    }

    private static bool IsDirectoryReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private static bool IsConfigurationReadFailure(Exception exception) =>
        IsDirectoryReadFailure(exception) || exception is InvalidDataException or XmlException or
            ArgumentException or FormatException or OverflowException;
}
