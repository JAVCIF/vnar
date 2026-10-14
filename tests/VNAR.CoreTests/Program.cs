using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Xml.Linq;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace VNAR.CoreTests;

internal static class Program
{
    private static int _failures;
    private static int _passes;

    private static async Task<int> Main(string[] args)
    {
        // Child mode for the cross-process test (see CrossProcessSaves).
        if (args.Length == 3 && args[0] == "--writer")
            return RunWriter(args[1], int.Parse(args[2]));

        // Redirect ALL VNAR data to a fresh temp folder before anything touches
        // AppDataPaths (its root is fixed on first use).
        var dataRoot = Path.Combine(Path.GetTempPath(), "VNAR CoreTests " + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppDataPaths.DataDirOverrideVariable, dataRoot);
        if (!AppDataPaths.Root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ABORT: data folder is not a temp folder: {AppDataPaths.Root}");
            return 2;
        }

        try
        {
            Section("Storage: atomic saves and .bak", AtomicSaves);
            Section("Storage: damaged files are recovered, never overwritten", Recovery);
            Section("Storage: window and shortcut processes do not lose each other's changes", WindowShortcutMerge);
            Section("Storage: concurrent writers from separate processes", CrossProcessSaves);
            Section("Library: bulk import saves once", BulkImport);
            Section("Library: exe index and developers dirty flag", IndexAndDevelopers);
            Section("Library: SerpApi key is never stored in plain text", SecretMigration);
            Section("Covers: decode size always covers the card", CoverDecodeSizes);
            Section("Covers: cleanup keeps what is used and only trashes the rest", CoverCleanup);
            Section("Icons: stored once per game and content, generic copy, orphan cleanup", Icons);
            Section("Scanner: results, progress and cancellation", Scanner);
            await SectionAsync("Downloads: size limits and no partial files", Downloads);
            Section("Links: vnar:// parsing", VnarLinks);
            Section("Version: single source in Directory.Build.props", Version);
            if (OperatingSystem.IsWindows())
                Section("DPAPI: real Windows encryption round trip", Dpapi);
            else
                Console.WriteLine("SKIP DPAPI section (Windows only)");
        }
        finally
        {
            try { Directory.Delete(dataRoot, recursive: true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} FAILED, {_passes} passed");
        return _failures == 0 ? 0 : 1;
    }

    // ----------------------------------------------------------------- storage

    private static void AtomicSaves()
    {
        ResetData();
        var lib = new LibraryService();
        Check(!lib.LibraryFileExisted, "fresh data folder: library.json did not exist");
        lib.Add(Game("Uno"));
        Check(File.Exists(AppDataPaths.LibraryFile) && !File.Exists(Bak), "first save creates library.json without .bak");
        lib.Add(Game("Dos"));
        Check(File.ReadAllText(Bak).Contains("Uno") && !File.ReadAllText(Bak).Contains("Dos"), "second save keeps the previous version in .bak");
        Check(Directory.GetFiles(AppDataPaths.Root, "*.tmp").Length == 0, "no leftover temp files");
        Check(new LibraryService().LibraryFileExisted, "after saving, library.json is reported as existing");
    }

    private static void Recovery()
    {
        ResetData();
        var lib = new LibraryService();
        lib.Add(Game("Uno"));
        lib.Add(Game("Dos"));

        // Truncated file (what a crash mid-write used to leave) with a valid .bak.
        File.WriteAllText(AppDataPaths.LibraryFile, File.ReadAllText(AppDataPaths.LibraryFile)[..25]);
        var damaged = File.ReadAllText(AppDataPaths.LibraryFile);
        var restored = new LibraryService();
        Check(restored.Games.Count == 1 && restored.Games[0].Name == "Uno", "loads the previous version from .bak");
        Check(Corrupt().Length == 1 && File.ReadAllText(Corrupt()[0]) == damaged, "damaged file is preserved byte for byte");
        Check(new LibraryService().Games.Count == 1, "library.json is rewritten valid for later readers");
        var notices = JsonStore.DrainRecoveries(AppDataPaths.Root);
        Check(notices.Count == 1 && notices[0].Outcome == StorageRecoveryOutcome.RestoredFromBackup, "one 'restored from backup' notice");
        Check(JsonStore.DrainRecoveries(AppDataPaths.Root).Count == 0, "the notice is shown only once");
        Check(File.Exists(Path.Combine(AppDataPaths.Root, "recovery.log")), "recovery.log written");

        // Empty file and no .bak.
        ResetData();
        Directory.CreateDirectory(AppDataPaths.Root);
        File.WriteAllText(AppDataPaths.LibraryFile, "");
        var empty = new LibraryService();
        Check(empty.Games.Count == 0 && Corrupt().Length == 1, "empty file is set aside and VNAR starts empty");
        var quarantined = Corrupt()[0];
        empty.Add(Game("Nuevo"));
        Check(File.Exists(quarantined) && File.ReadAllText(quarantined) == "", "later saves never touch the set-aside copy");
        Check(JsonStore.DrainRecoveries(AppDataPaths.Root).Single().Outcome == StorageRecoveryOutcome.StartedEmpty, "one 'started empty' notice");

        // A notice raised by a process without a window survives on disk.
        ResetData();
        Directory.CreateDirectory(AppDataPaths.Root);
        File.WriteAllText(AppDataPaths.LibraryFile, "{ broken");
        _ = new LibraryService();
        Check(File.ReadAllLines(Path.Combine(AppDataPaths.Root, "recovery-pending.jsonl")).Length == 1, "notice persisted for the next library window");
        JsonStore.DrainRecoveries(AppDataPaths.Root);
    }

    private static void WindowShortcutMerge()
    {
        ResetData();
        var window = new LibraryService();
        var a = Game("A");
        var b = Game("B");
        window.AddRange([a, b]);

        var played = new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);
        new LibraryService().RecordLaunch(a.Id, played);       // shortcut process
        window.Games.Single(g => g.Id == b.Id).Favorite = true;
        window.SaveGames();                                    // stale window snapshot
        var disk = new LibraryService();
        Check(disk.Games.Single(g => g.Id == a.Id).LastPlayedUtc == played, "window save keeps the shortcut's last-played time");
        Check(disk.Games.Single(g => g.Id == b.Id).Favorite, "window edit is saved");

        var staleShortcut = new LibraryService();
        window.Games.Single(g => g.Id == a.Id).Name = "A renamed";
        window.SaveGames();
        staleShortcut.RecordLaunch(b.Id, played.AddHours(1));
        disk = new LibraryService();
        Check(disk.Games.Single(g => g.Id == a.Id).Name == "A renamed", "a shortcut with a stale snapshot does not undo window edits");
        Check(disk.Games.Single(g => g.Id == b.Id).LastPlayedUtc == played.AddHours(1), "and still records its launch");
    }

    private static void CrossProcessSaves()
    {
        ResetData();
        var lib = new LibraryService();
        lib.AddRange(Enumerable.Range(0, 30).Select(i => Game($"G{i}")));
        var ids = lib.Games.Select(g => g.Id).ToList();
        var idsFile = Path.Combine(Path.GetTempPath(), $"vnar-ids-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(idsFile, ids.Select(i => i.ToString()));

        try
        {
            // Three real child processes record launches while this process keeps
            // saving edits from its own (stale) snapshot.
            var children = Enumerable.Range(0, 3).Select(_ => StartWriter(idsFile, 25)).ToList();
            for (var i = 0; i < 40; i++)
            {
                lib.Games[i % lib.Games.Count].Favorite ^= true;
                lib.SaveGames();
            }
            foreach (var child in children)
            {
                if (!child.WaitForExit(120_000)) { child.Kill(); Check(false, "writer process finished in time"); }
            }

            Check(children.All(c => c.ExitCode == 0), "all writer processes succeeded");
            var final = new LibraryService();
            Check(final.Games.Count == 30, "library is valid and complete after concurrent multi-process saves");
            Check(final.Games.All(g => g.LastPlayedUtc is not null), "no launch recorded by another process was lost");
            Check(JsonStore.DrainRecoveries(AppDataPaths.Root).Count == 0, "no recovery was ever needed");
        }
        finally
        {
            File.Delete(idsFile);
        }
    }

    private static int RunWriter(string idsFile, int iterations)
    {
        // Inherits VNAR_DATA_DIR from the parent test process.
        var ids = File.ReadAllLines(idsFile).Select(Guid.Parse).ToList();
        var lib = new LibraryService();
        var random = new Random(Environment.ProcessId);
        for (var i = 0; i < iterations; i++)
            lib.RecordLaunch(ids[random.Next(ids.Count)], DateTime.UtcNow);
        // Make sure every game gets at least one launch from some writer.
        foreach (var id in ids) lib.RecordLaunch(id, DateTime.UtcNow);
        return 0;
    }

    private static Process StartWriter(string idsFile, int iterations)
    {
        var self = Environment.ProcessPath!;
        var info = Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo(self) { ArgumentList = { Assembly.GetEntryAssembly()!.Location, "--writer", idsFile, iterations.ToString() } }
            : new ProcessStartInfo(self) { ArgumentList = { "--writer", idsFile, iterations.ToString() } };
        info.UseShellExecute = false;
        return Process.Start(info)!;
    }

    // ----------------------------------------------------------------- library

    private static void BulkImport()
    {
        ResetData();
        var lib = new LibraryService();
        lib.AddRange([Game("Existing")]);

        var batch = Enumerable.Range(0, 400).Select(i => Game($"Imported {i}")).ToList();
        batch.Add(new GameEntry { Name = "Duplicate in batch", ExePath = batch[0].ExePath });
        batch.Add(new GameEntry { Name = "Already in library", ExePath = lib.Games[0].ExePath });
        var added = lib.AddRange(batch);

        Check(added.Count == 400 && lib.Games.Count == 401, "400 new games added; duplicates skipped");
        // One save means the .bak still holds the library as it was before the import.
        var bak = File.ReadAllText(Bak);
        Check(bak.Contains("Existing") && !bak.Contains("Imported"), "the whole import was a single write");
        Check(lib.AddRange([Game("Dup", batch[5].ExePath)]).Count == 0, "re-adding a known exe adds nothing");
    }

    private static void IndexAndDevelopers()
    {
        ResetData();
        var lib = new LibraryService();
        lib.Games.Add(Game("A", Path.Combine(Path.GetTempPath(), "games", "A", "a.exe")));
        lib.Games.Add(new GameEntry { Name = "Bad", ExePath = "bad\0path.exe" });
        var equivalent = Path.Combine(Path.GetTempPath(), "games", "A", "..", "A", "a.exe");
        Check(lib.BuildExeIndex().Contains(LibraryService.NormalizeExePath(equivalent)), "index matches equivalent paths");
        Check(lib.ContainsExe(equivalent) && !lib.ContainsExe("/other/b.exe"), "duplicate check survives a malformed stored path");

        lib.EnsureDeveloper("p1", "Studio One");
        lib.SaveDevelopersIfDirty();
        var stamp = File.GetLastWriteTimeUtc(AppDataPaths.DevelopersFile);
        Thread.Sleep(50);
        for (var i = 0; i < 20; i++) { lib.EnsureDeveloper("p1", "Studio One"); lib.SaveDevelopersIfDirty(); }
        Check(File.GetLastWriteTimeUtc(AppDataPaths.DevelopersFile) == stamp, "20 refreshes without changes do not write developers.json");
        lib.EnsureDeveloper("p1", "Studio Renamed");
        lib.SaveDevelopersIfDirty();
        Check(File.ReadAllText(AppDataPaths.DevelopersFile).Contains("Studio Renamed"), "a rename is saved");
    }

    private static void SecretMigration()
    {
        ResetData();
        Directory.CreateDirectory(AppDataPaths.Root);
        File.WriteAllText(AppDataPaths.SettingsFile,
            """{ "LocaleEmulatorPath": "C:\\LE\\LEProc.exe", "SerpApiKey": "secret-abc-123", "Language": "es", "PageSize": 20 }""");
        var lib = new LibraryService();
        Check(lib.Settings.SerpApiKey == "secret-abc-123", "an old plain-text key keeps working");
        var saved = File.ReadAllText(AppDataPaths.SettingsFile);
        Check(!saved.Contains("secret-abc-123") && saved.Contains("SerpApiKeyProtected") && !saved.Contains("\"SerpApiKey\""), "settings.json no longer contains it in clear");
        Check(!File.ReadAllText(JsonStore.BackupPathFor(AppDataPaths.SettingsFile)).Contains("secret-abc-123"), "settings.json.bak does not keep it either");
        Check(lib.Settings.LocaleEmulatorPath == @"C:\LE\LEProc.exe", "other settings preserved");

        lib.Settings.SerpApiKey = "  new-key  ";
        lib.SaveSettings();
        Check(new LibraryService().Settings.SerpApiKey == "new-key", "a new key round-trips (trimmed)");
        lib.Settings.SerpApiKey = "";
        lib.SaveSettings();
        Check(new LibraryService().Settings.SerpApiKey == "", "clearing the key works");
    }

    // ------------------------------------------------------------------ covers

    private static void CoverDecodeSizes()
    {
        Check(CoverDecodeMath.ForFill(1240, 1720, 230, 300, 1.0) == (230, 0), "editor HQ cover at 100%: 230 px wide");
        Check(CoverDecodeMath.ForFill(620, 860, 230, 300, 1.5) == (345, 0), "editor cover at 150%: 345 px wide");
        Check(CoverDecodeMath.ForFill(1920, 1080, 230, 300, 1.0) == (0, 300), "landscape image limited by height");
        Check(CoverDecodeMath.ForFill(200, 280, 230, 300, 1.0) == (0, 0), "small images are never upscaled");
        Check(CoverDecodeMath.ForFill(0, 0, 230, 300, 1.0) == (0, 0), "unknown size decodes at full size");

        var random = new Random(7);
        var scales = new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };
        var allCover = true;
        for (var i = 0; i < 5000 && allCover; i++)
        {
            int w = random.Next(50, 6000), h = random.Next(50, 6000);
            var dpi = scales[random.Next(scales.Length)];
            var (dw, dh) = CoverDecodeMath.ForFill(w, h, 230, 300, dpi);
            if (dw == 0 && dh == 0) continue;
            double outW = dw > 0 ? dw : w * (dh / (double)h);
            double outH = dh > 0 ? dh : h * (dw / (double)w);
            allCover = outW >= Math.Ceiling(230 * dpi) - 0.5 && outH >= Math.Ceiling(300 * dpi) - 0.5;
        }
        Check(allCover, "5000 random sizes and scales: never smaller than the card on screen");
    }

    private static void CoverCleanup()
    {
        var covers = Path.Combine(Path.GetTempPath(), "vnar-covers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(covers);
        var now = DateTime.UtcNow;
        string Make(string name, double ageHours)
        {
            var path = Path.Combine(covers, name);
            File.WriteAllText(path, name);
            File.SetLastWriteTimeUtc(path, now.AddHours(-ageHours));
            return path;
        }

        try
        {
            Guid a = Guid.NewGuid(), b = Guid.NewGuid(), dev = Guid.NewGuid(), removed = Guid.NewGuid();
            var aCover = Make($"{a:N}_rendered_hq_1.png", 100);
            var aSource = Make($"{a:N}_vndb_2.jpg", 100);
            var aOldRender = Make($"{a:N}_rendered_3.png", 100);
            var aOldOriginal = Make($"{a:N}_web_4.jpg", 100);
            var aRecent = Make($"{a:N}_rendered_5.png", 2);
            var bCover = Make($"{b:N}_local_6.png", 100);
            var bOldOriginal = Make($"{b:N}_vndb_7.jpg", 100);
            var bOldRender = Make($"{b:N}_edited_8.png", 100);
            var devCover = Make($"{dev:N}_rendered_9.png", 100);
            var orphan1 = Make($"{removed:N}_vndb_10.jpg", 100);
            var orphan2 = Make($"{removed:N}_rendered_11.png", 100);
            var tmp = Make($"{a:N}_rendered_12.png.tmp", 100);
            var foreign = Make("my-wallpaper.png", 100);

            var owners = new List<CoverOwner>
            {
                new(a, [aCover, aSource], HasEditSource: true),
                new(b, [bCover, ""], HasEditSource: false),
                new(dev, [devCover, ""], HasEditSource: false),
            };
            var result = CoverCleanupService.Run(covers, owners, now);
            Check(new[] { aCover, aSource, bCover, devCover }.All(File.Exists), "covers and editor sources in use are kept");
            Check(File.Exists(aRecent), "files younger than 24 h are kept");
            Check(File.Exists(bOldOriginal), "originals kept for entries without an edit source (editor fallback)");
            Check(File.Exists(foreign), "files not named by VNAR are left alone");
            Check(new[] { aOldRender, aOldOriginal, bOldRender, orphan1, orphan2, tmp }.All(p => !File.Exists(p)), "unused renders, originals, orphans and .tmp are moved out");
            var trashDay = Path.Combine(covers, CoverCleanupService.TrashFolderName, now.ToLocalTime().ToString("yyyyMMdd"));
            Check(result.MovedToTrash == 6 && Directory.GetFiles(trashDay).Length == 6, "they go to _trash, nothing is deleted yet");
            Check(CoverCleanupService.Run(covers, owners, now) == new CoverCleanupResult(0, 0), "a second run does nothing");
            Check(CoverCleanupService.Run(covers, owners, now.AddDays(31)).PurgedFromTrash == 6 && !Directory.Exists(trashDay), "trash is emptied after 30 days");
            Check(File.Exists(aCover) && File.Exists(bOldOriginal) && File.Exists(foreign), "purging never touches the covers folder itself");

            // Library emptied on purpose: no owners at all, every VNAR cover is unused.
            var emptied = CoverCleanupService.Run(covers, [], now.AddDays(32));
            // aRecent was already trashed by the run 31 days later (by then it was an old,
            // unused render), so 5 VNAR covers remain here.
            Check(emptied.MovedToTrash == 5 && Directory.GetFiles(covers).Single() == foreign, "with an emptied library every old VNAR cover goes to _trash");
        }
        finally
        {
            Directory.Delete(covers, recursive: true);
        }
    }

    private static void Icons()
    {
        ResetData();
        var src = Path.Combine(Path.GetTempPath(), $"vnar-icon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(src);
        try
        {
            var icoA = Path.Combine(src, "a.ico");
            var icoB = Path.Combine(src, "b.ico");
            File.WriteAllBytes(icoA, [0, 0, 1, 0, 1, 2, 3]);
            File.WriteAllBytes(icoB, [0, 0, 1, 0, 9, 9, 9]);
            var game = Guid.NewGuid();

            var first = IconStore.StoreCustomIcon(icoA, game);
            var again = IconStore.StoreCustomIcon(icoA, game);
            Check(first.Created && !again.Created && first.Path == again.Path, "the same icon for the same game is stored once");
            Check(Path.GetFileName(first.Path).StartsWith(game.ToString("N") + "_") && first.Path.StartsWith(AppDataPaths.IconsDir), "named <game id>_<hash>.ico in the data folder");
            var other = IconStore.StoreCustomIcon(icoB, game);
            Check(other.Created && other.Path != first.Path, "a different icon gets its own file");
            Check(Directory.GetFiles(AppDataPaths.IconsDir).Length == 2, "no temp or backup files left");

            IconStore.DiscardIfCreated(again);
            Check(File.Exists(first.Path), "discarding a reused icon keeps it");
            IconStore.DiscardIfCreated(other);
            Check(!File.Exists(other.Path), "discarding a just-created icon removes it");

            var generic = IconStore.EnsureGenericIconCopy(icoA);
            Check(generic == Path.Combine(AppDataPaths.IconsDir, IconStore.GenericIconFileName) && File.ReadAllBytes(generic).SequenceEqual(File.ReadAllBytes(icoA)),
                "generic icon copied to a stable path in the data folder");
            var stamp = File.GetLastWriteTimeUtc(generic);
            Thread.Sleep(50);
            Check(IconStore.EnsureGenericIconCopy(icoA) == generic && File.GetLastWriteTimeUtc(generic) == stamp, "unchanged generic icon is not rewritten");
            File.WriteAllBytes(icoA, [0, 0, 1, 0, 7, 7]);
            IconStore.EnsureGenericIconCopy(icoA);
            Check(File.ReadAllBytes(generic).SequenceEqual(File.ReadAllBytes(icoA)), "a new generic icon from VNAR replaces the copy");
            Check(IconStore.EnsureGenericIconCopy(Path.Combine(src, "VNAR.exe")) == Path.Combine(src, "VNAR.exe"), "a non-.ico fallback is used as is");

            // Orphans-only cleanup: icons of removed games go, the rest stays.
            var removed = Guid.NewGuid();
            var now = DateTime.UtcNow;
            string Old(string name) { var p = Path.Combine(AppDataPaths.IconsDir, name); File.WriteAllBytes(p, [1]); File.SetLastWriteTimeUtc(p, now.AddDays(-3)); return p; }
            var kept = Old($"{game:N}_unusedbutkept.ico");
            var orphan = Old($"{removed:N}_0123456789abcdef.ico");
            var recentOrphan = Path.Combine(AppDataPaths.IconsDir, $"{removed:N}_fresh.ico");
            File.WriteAllBytes(recentOrphan, [1]);
            File.SetLastWriteTimeUtc(generic, now.AddDays(-3));
            var result = CoverCleanupService.RunOrphansOnly(AppDataPaths.IconsDir, [game], now);
            Check(File.Exists(kept) && File.Exists(first.Path), "icons of existing games are never removed");
            Check(!File.Exists(orphan) && result.MovedToTrash == 1, "icons of removed games go to _trash");
            Check(File.Exists(recentOrphan) && File.Exists(generic), "recent files and the generic icon are kept");
        }
        finally
        {
            Directory.Delete(src, recursive: true);
        }
    }

    // ----------------------------------------------------------------- scanner

    private static void Scanner()
    {
        var root = Path.Combine(Path.GetTempPath(), "vnar-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var d = 0; d < 120; d++)
            {
                var dir = Path.Combine(root, $"Game {d}", "bin");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"game{d}.exe"), new byte[40 * 1024 + d]);
                File.WriteAllBytes(Path.Combine(dir, "unins000.exe"), new byte[50 * 1024]);
                File.WriteAllBytes(Path.Combine(dir, "tiny.exe"), new byte[100]);
            }

            var reports = new List<ScannerService.ScanProgress>();
            var found = ScannerService.Scan(root, 400, new SyncProgress<ScannerService.ScanProgress>(reports.Add));
            Check(found.Count == 120 && found.All(c => Path.GetFileName(c.ExePath).StartsWith("game")), "finds the games, skips uninstallers and tiny exes");
            Check(found.Zip(found.Skip(1)).All(p => p.First.SizeBytes >= p.Second.SizeBytes), "largest first");
            Check(reports.Count > 1 && reports[^1] == new ScannerService.ScanProgress(241, 120), "progress reported with final totals");

            using var cts = new CancellationTokenSource();
            var seen = 0;
            var cancelled = false;
            try { ScannerService.Scan(root, 400, new SyncProgress<ScannerService.ScanProgress>(_ => { if (++seen == 2) cts.Cancel(); }), cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancellation stops the scan");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // --------------------------------------------------------------- downloads

    private static async Task Downloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vnar-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ok = Path.Combine(dir, "ok.jpg");
            await DownloadGuard.CopyToNewFileLimitedAsync(Response(1000, true), ok, 2000, default);
            Check(new FileInfo(ok).Length == 1000, "a download within the limit is saved completely");

            var declared = Path.Combine(dir, "declared.jpg");
            Check(await Throws(() => DownloadGuard.CopyToNewFileLimitedAsync(Response(5000, true), declared, 2000, default)) && !File.Exists(declared),
                "an announced oversize is rejected before writing");
            var undeclared = Path.Combine(dir, "undeclared.jpg");
            Check(await Throws(() => DownloadGuard.CopyToNewFileLimitedAsync(Response(5000, false), undeclared, 2000, default)) && !File.Exists(undeclared),
                "an unannounced oversize is stopped and the partial file deleted");
            Check(await Throws(() => DownloadGuard.ReadAllLimitedAsync(Response(5000, false), 2000, default)), "in-memory downloads are limited too");
            Check((await DownloadGuard.ReadAllLimitedAsync(Response(2000, true), 2000, default)).Length == 2000, "exactly at the limit is allowed");

            var cancelledPath = Path.Combine(dir, "cancelled.jpg");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelled = false;
            try { await DownloadGuard.CopyToNewFileLimitedAsync(Response(1000, true), cancelledPath, 2000, cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !File.Exists(cancelledPath), "a cancelled download leaves no file");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static HttpResponseMessage Response(int size, bool declareLength)
    {
        var content = new StreamContent(new MemoryStream(new byte[size]));
        content.Headers.ContentLength = declareLength ? size : null;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task<bool> Throws(Func<Task> action)
    {
        try { await action(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    // ------------------------------------------------------------ links/version

    private static void VnarLinks()
    {
        var id = Guid.Parse("64f78cd1-0b00-4271-a9e1-89abc55f66cc");
        var link = VnarUri.BuildLaunch(id);
        Check(link == "vnar://launch/64f78cd1-0b00-4271-a9e1-89abc55f66cc", "builds vnar://launch/<id>");
        Check(VnarUri.TryParseLaunch(link, out var parsed) && parsed == id, "round trip");
        Check(VnarUri.TryParseLaunch(link + "/", out parsed) && parsed == id, "trailing slash accepted");
        Check(VnarUri.TryParseLaunch("\"" + link + "\"", out parsed) && parsed == id, "quoted argument accepted");
        Check(VnarUri.TryParseLaunch(link.ToUpperInvariant(), out parsed) && parsed == id, "case-insensitive");

        string[] rejected =
        [
            "vnar://delete/64f78cd1-0b00-4271-a9e1-89abc55f66cc",
            "vnar://launch/not-a-guid",
            "vnar://launch/",
            "vnar://launch/00000000-0000-0000-0000-000000000000",
            "vnar://launch/64f78cd1-0b00-4271-a9e1-89abc55f66cc?exe=C:\\evil.exe",
            "vnar://launch/64f78cd1-0b00-4271-a9e1-89abc55f66cc#x",
            "vnar://launch/64f78cd1-0b00-4271-a9e1-89abc55f66cc/extra",
            "steam://launch/64f78cd1-0b00-4271-a9e1-89abc55f66cc",
            "vnar:garbage",
        ];
        Check(rejected.All(r => !VnarUri.TryParseLaunch(r, out _)), $"rejects {rejected.Length} malformed or foreign links");
        Check(VnarUri.IsVnarUri("vnar:garbage") && !VnarUri.IsVnarUri("--launch"), "tells links from command-line flags");
    }

    private static void Version()
    {
        var props = FindUpwards("Directory.Build.props");
        var expected = props is null ? null : XDocument.Load(props).Descendants("Version").FirstOrDefault()?.Value.Trim();
        Check(!string.IsNullOrEmpty(expected) && AppInfo.Version == expected, $"AppInfo.Version equals Directory.Build.props ({expected})");
        Check(!AppInfo.Version.Contains('+'), "no build metadata in the version");
        var probe = new HttpRequestMessage();
        Check(probe.Headers.UserAgent.TryParseAdd(AppInfo.UserAgent) && probe.Headers.UserAgent.TryParseAdd(AppInfo.BrowserLikeUserAgent),
            "both User-Agents are valid header values");
    }

    private static void Dpapi()
    {
        var cipher = SecretProtector.Protect("clave-de-prueba-ñ-日本");
        Check(!string.IsNullOrEmpty(cipher) && !cipher.Contains("clave"), "DPAPI output is not the plain text");
        Check(SecretProtector.Unprotect(cipher) == "clave-de-prueba-ñ-日本", "DPAPI round trip (Unicode)");
        Check(SecretProtector.Unprotect("not base64!") == "" && SecretProtector.Unprotect(Convert.ToBase64String(new byte[40])) == "",
            "garbage decrypts to empty instead of throwing");
    }

    // ----------------------------------------------------------------- helpers

    private static string Bak => JsonStore.BackupPathFor(AppDataPaths.LibraryFile);

    private static string[] Corrupt() => Directory.GetFiles(AppDataPaths.Root, "library.corrupt-*.json");

    private static GameEntry Game(string name, string? exe = null)
        => new() { Name = name, ExePath = exe ?? Path.Combine(Path.GetTempPath(), "games", Guid.NewGuid().ToString("N"), name + ".exe") };

    private static void ResetData()
    {
        if (Directory.Exists(AppDataPaths.Root)) Directory.Delete(AppDataPaths.Root, recursive: true);
        JsonStore.DrainRecoveries(AppDataPaths.Root);
    }

    private static string? FindUpwards(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void Section(string name, Action body)
    {
        Console.WriteLine($"-- {name}");
        try { body(); }
        catch (Exception ex) { Check(false, $"unexpected {ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task SectionAsync(string name, Func<Task> body)
    {
        Console.WriteLine($"-- {name}");
        try { await body(); }
        catch (Exception ex) { Check(false, $"unexpected {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void Check(bool ok, string description)
    {
        if (ok) _passes++; else _failures++;
        Console.WriteLine($"   {(ok ? "PASS" : "FAIL")} {description}");
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
