using LocaleGameHub.Models;

namespace LocaleGameHub.Services;

public static class ScannerService
{
    private static readonly string[] IgnoreFragments =
    [
        "unins", "uninstall", "setup", "install", "vcredist", "dxsetup", "crashreport",
        "crashpad", "unitycrashhandler", "bugreport", "configtool", "config.exe", "updater",
        "update.exe", "patcher", "register", "regsvr"
    ];

    /// <summary>Folders visited and candidate executables found so far.</summary>
    public readonly record struct ScanProgress(int FoldersVisited, int ExecutablesFound);

    /// <summary>
    /// Scans <paramref name="root"/> for game executables. Safe to run on a worker
    /// thread; honours <paramref name="cancellationToken"/> between folders.
    /// </summary>
    public static List<ScanCandidate> Scan(
        string root,
        int maxResults = 400,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];

        var result = new List<ScanCandidate>();
        var folders = 0;
        foreach (var exe in SafeEnumerateExecutables(root, () =>
                 {
                     folders++;
                     if (folders % 20 == 0) progress?.Report(new ScanProgress(folders, result.Count));
                 }, cancellationToken))
        {
            if (result.Count >= maxResults) break;

            var fileName = Path.GetFileName(exe).ToLowerInvariant();
            if (IgnoreFragments.Any(x => fileName.Contains(x))) continue;

            FileInfo info;
            try { info = new FileInfo(exe); }
            catch { continue; }
            if (info.Length < 32 * 1024) continue;

            var folder = info.Directory?.Name ?? Path.GetFileNameWithoutExtension(exe);
            result.Add(new ScanCandidate
            {
                Name = CleanFolderName(folder),
                ExePath = exe,
                SizeBytes = info.Length,
                Selected = true
            });
        }

        progress?.Report(new ScanProgress(folders, result.Count));
        return result
            .GroupBy(x => x.ExePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.SizeBytes)
            .ToList();
    }

    private static IEnumerable<string> SafeEnumerateExecutables(string root, Action onFolder, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var visited = 0;

        while (pending.Count > 0 && visited < 10000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            visited++;
            onFolder();

            string[] files;
            try { files = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly); }
            catch { files = []; }
            foreach (var file in files) yield return file;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly); }
            catch { subdirs = []; }

            foreach (var subdir in subdirs)
            {
                try
                {
                    var attributes = File.GetAttributes(subdir);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push(subdir);
                }
                catch
                {
                    // Ignore inaccessible folders.
                }
            }
        }
    }

    public static string InferNameFromExe(string exe)
    {
        var folder = Path.GetDirectoryName(exe);
        var folderName = string.IsNullOrWhiteSpace(folder) ? string.Empty : new DirectoryInfo(folder).Name;
        return string.IsNullOrWhiteSpace(folderName)
            ? Path.GetFileNameWithoutExtension(exe)
            : CleanFolderName(folderName);
    }

    private static string CleanFolderName(string value)
    {
        var cleaned = value.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? value : cleaned;
    }
}
