using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocaleGameHub.Services;

public enum StorageRecoveryOutcome
{
    /// <summary>The damaged file was set aside and its previous version (.bak) was restored.</summary>
    RestoredFromBackup,

    /// <summary>The damaged file was set aside and no usable backup existed.</summary>
    StartedEmpty
}

public sealed record StorageRecovery(
    string FileName,
    StorageRecoveryOutcome Outcome,
    string QuarantinedPath,
    string Error);

/// <summary>
/// Crash-safe JSON persistence for VNAR's data files.
/// Writes go to a temporary file and replace the target atomically, keeping the
/// previous version as "&lt;file&gt;.bak". A file that cannot be parsed is never
/// overwritten: it is moved aside, the backup is restored when possible, and the
/// event is reported so the UI can tell the user.
/// This class has no WPF dependencies on purpose.
/// </summary>
public static class JsonStore
{
    // Same namespace for every VNAR process in the session: the library UI and
    // the short-lived processes started by game shortcuts.
    private const string DataMutexName = @"Local\JAVCIF.VNAR.Data";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly object RecoveriesSync = new();
    private static readonly List<StorageRecovery> Recoveries = [];

    public static string BackupPathFor(string path) => path + ".bak";

    /// <summary>
    /// Recovery events not yet shown to the user: those from this process plus any
    /// recorded by another VNAR process (e.g. a game shortcut) that had no window to
    /// show them. Each event is returned once.
    /// </summary>
    public static IReadOnlyList<StorageRecovery> DrainRecoveries(string dataDirectory)
    {
        var result = new List<StorageRecovery>();
        lock (RecoveriesSync)
        {
            result.AddRange(Recoveries);
            Recoveries.Clear();
        }

        try
        {
            WithDataLock(() =>
            {
                var pending = PendingFilePath(dataDirectory);
                if (!File.Exists(pending)) return;
                foreach (var line in File.ReadAllLines(pending))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var item = JsonSerializer.Deserialize<StorageRecovery>(line);
                        if (item is not null) result.Add(item);
                    }
                    catch (JsonException)
                    {
                        // Skip a damaged notice line; recovery.log still has it.
                    }
                }
                File.Delete(pending);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            // Show what we have; the pending file will be read next time.
        }

        return result
            .GroupBy(r => r.QuarantinedPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static string PendingFilePath(string dataDirectory) => Path.Combine(dataDirectory, "recovery-pending.jsonl");

    public static bool HasPendingRecoveries
    {
        get { lock (RecoveriesSync) return Recoveries.Count > 0; }
    }

    /// <summary>
    /// Runs <paramref name="action"/> while holding a lock shared by all VNAR
    /// processes, so a read-merge-write sequence cannot interleave with another one.
    /// </summary>
    public static T WithDataLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, DataMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // A previous VNAR process died while holding the lock. Ownership
                // passes to us; every write is atomic, so the data is consistent.
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("Timed out waiting for another VNAR process to finish saving.");

            return action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    public static void WithDataLock(Action action) => WithDataLock(() => { action(); return true; });

    /// <summary>
    /// Loads a JSON file. Returns null when the file does not exist, or when it was
    /// damaged and no backup could be restored (in that case a recovery is recorded).
    /// </summary>
    public static T? Load<T>(string path, JsonSerializerOptions options) where T : class
    {
        if (!File.Exists(path)) return null;

        string text;
        try
        {
            text = ReadAllTextShared(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        try
        {
            return Deserialize<T>(text, options);
        }
        catch (JsonException ex)
        {
            return RecoverDamagedFile<T>(path, options, ex);
        }
    }

    /// <summary>
    /// Reads and parses a file without any recovery side effects.
    /// Returns false if the file is missing or unreadable.
    /// </summary>
    public static bool TryReadQuietly<T>(string path, JsonSerializerOptions options, out T? value, out byte[] rawBytes) where T : class
    {
        value = null;
        rawBytes = [];
        try
        {
            if (!File.Exists(path)) return false;
            rawBytes = ReadAllBytesShared(path);
            value = Deserialize<T>(Utf8NoBom.GetString(StripBom(rawBytes)), options);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Serializes and writes atomically. Returns the bytes written.</summary>
    public static byte[] Save<T>(string path, T value, JsonSerializerOptions options)
    {
        var bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(value, options));
        WriteAllBytesAtomic(path, bytes);
        return bytes;
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string HashFileOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? Hash(ReadAllBytesShared(path)) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        // Unique temp name: two processes must never share a temp file.
        var temp = Path.Combine(directory, $"{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RetryOnSharingViolation(() =>
            {
                if (File.Exists(path))
                    File.Replace(temp, path, BackupPathFor(path), ignoreMetadataErrors: true);
                else
                    File.Move(temp, path);
            });
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static T? RecoverDamagedFile<T>(string path, JsonSerializerOptions options, JsonException error) where T : class
    {
        var quarantined = Quarantine(path);
        var backup = BackupPathFor(path);
        T? restored = null;

        if (TryReadQuietly<T>(backup, options, out var fromBackup, out var backupBytes) && fromBackup is not null)
        {
            try
            {
                // Put the good version back in place so every reader (including other
                // LibraryService instances in this process) sees it. The .bak itself is
                // left untouched: the damaged file was already moved aside.
                WriteAllBytesAtomic(path, backupBytes);
                restored = fromBackup;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still use the backup in memory; the next save will write it.
                restored = fromBackup;
            }
        }

        var recovery = new StorageRecovery(
            Path.GetFileName(path),
            restored is null ? StorageRecoveryOutcome.StartedEmpty : StorageRecoveryOutcome.RestoredFromBackup,
            quarantined,
            error.Message);

        lock (RecoveriesSync) Recoveries.Add(recovery);
        WriteRecoveryLog(path, recovery);
        return restored;
    }

    private static string Quarantine(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var target = Path.Combine(directory, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
        for (var i = 2; File.Exists(target); i++)
            target = Path.Combine(directory, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{i}{extension}");

        try
        {
            RetryOnSharingViolation(() => File.Move(path, target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If it cannot be moved, at least keep a copy before anything overwrites it.
            File.Copy(path, target, overwrite: false);
        }

        return target;
    }

    private static void WriteRecoveryLog(string path, StorageRecovery recovery)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        try
        {
            var log = Path.Combine(directory, "recovery.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {recovery.FileName}: {recovery.Outcome}. " +
                       $"Damaged copy: {recovery.QuarantinedPath}. Parse error: {recovery.Error}{Environment.NewLine}";
            File.AppendAllText(log, line);
        }
        catch
        {
            // Logging must never hide the recovery itself.
        }

        try
        {
            // Callers hold the data lock (Load runs inside it), so this append is safe.
            File.AppendAllText(PendingFilePath(directory), JsonSerializer.Serialize(recovery) + Environment.NewLine);
        }
        catch
        {
            // The in-memory list still lets this process report it.
        }
    }

    private static T? Deserialize<T>(string text, JsonSerializerOptions options) where T : class
    {
        // An empty or whitespace file is what a write interrupted by a crash leaves
        // behind; System.Text.Json reports it as JsonException, which is what we want.
        return JsonSerializer.Deserialize<T>(text, options);
    }

    private static string ReadAllTextShared(string path)
        => Utf8NoBom.GetString(StripBom(ReadAllBytesShared(path)));

    private static byte[] ReadAllBytesShared(string path)
    {
        byte[] result = [];
        RetryOnSharingViolation(() =>
        {
            // Share Delete so a concurrent atomic replace is not blocked by a reader.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            result = memory.ToArray();
        });
        return result;
    }

    private static byte[] StripBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private static void RetryOnSharingViolation(Action action)
    {
        const int attempts = 6;
        for (var i = 1; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException ex) when (i < attempts && ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                // Antivirus, indexers or another VNAR process may hold the file briefly.
                Thread.Sleep(40 * i);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A leftover .tmp file is harmless.
        }
    }
}
