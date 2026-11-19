using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LopForge;

// Finds the player's Lies of P and bbport-windows folders for the stand-alone installer (no Melty to pass them).
public static class Installer
{
    static bool IsLop(string? d) => !string.IsNullOrWhiteSpace(d) && Directory.Exists(Path.Combine(d, "LiesofP", "Content", "Paks"));
    // The bbport-windows program folder: it holds CUSA03173 itself, or it is the port (Play Bloodborne.exe)
    // and the launcher's game folder setting points to the game files elsewhere.
    static bool IsBb(string? d) => !string.IsNullOrWhiteSpace(d) && (Directory.Exists(Path.Combine(d, "CUSA03173", "dvdroot_ps4"))
        || ((File.Exists(Path.Combine(d, "Play Bloodborne.exe")) || File.Exists(Path.Combine(d, "Bloodborne.exe"))) && GameData.Find(d) != null));

    public static (string lop, string bb)? Find(string? lop, string? bb)
    {
        Console.WriteLine("Pale Knight Cataclysm installer");
        Console.WriteLine("Builds the Pale Knight and Cataclysm from your own Lies of P into your own Bloodborne (bbport-windows).");
        Console.WriteLine();
        bb = IsBb(bb) ? bb : FindBb();
        lop = IsLop(lop) ? lop : FindLop();
        bb ??= Ask("bbport-windows folder (the one that holds Play Bloodborne.exe)", IsBb);
        if (bb == null) return null;
        lop ??= Ask("Lies of P folder (the one that holds LiesofP)", IsLop);
        if (lop == null) return null;
        Console.WriteLine("Bloodborne: " + bb);
        Console.WriteLine("Lies of P:  " + lop);
        Console.WriteLine("Building the mod (a few minutes)...");
        return (lop, bb);
    }

    static string? Ask(string what, Func<string?, bool> ok)
    {
        for (int tries = 0; tries < 3; tries++)
        {
            Console.WriteLine($"Couldn't find your {what}. Paste its full path and press Enter:");
            var s = Console.ReadLine()?.Trim().Trim('"');
            if (s == null) return null;
            if (ok(s)) return s;
            // Also accept the game's inner folder pasted by mistake (e.g. ...\CUSA03173 or ...\LiesofP).
            var up = Path.GetDirectoryName(s.TrimEnd('\\', '/'));
            if (ok(up)) return up;
            Console.WriteLine("That folder doesn't look right.");
        }
        Console.WriteLine("Giving up: run the installer again once you know the folder.");
        return null;
    }

    // A bbport-windows program folder (Play Bloodborne.exe) whose game files can be found: the folder Melty passes as {game},
    // where the port keeps mods.json (which mods are enabled).
    static bool IsPort(string? d) => !string.IsNullOrWhiteSpace(d)
        && (File.Exists(Path.Combine(d, "Play Bloodborne.exe")) || File.Exists(Path.Combine(d, "Bloodborne.exe"))) && GameData.Find(d) != null;

    // The port folder first (beside this program, near the launcher's game folder setting, then the usual places a few
    // folders deep); only if there is none, a folder that merely holds CUSA03173.
    public static string? FindBb()
    {
        var gameDir = GameData.LauncherGameDir()?.TrimEnd('\\', '/');
        var near = new List<string>();
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent) near.Add(d.FullName);
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameDir))
            for (var d = Path.GetDirectoryName(gameDir); d != null && roots.Count < 3; d = Path.GetDirectoryName(d)) roots.Add(d);
        foreach (var sf in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile })
            roots.Add(Environment.GetFolderPath(sf));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        try { foreach (var dr in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed)) roots.Add(dr.RootDirectory.FullName); } catch { }
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Windows", "$Recycle.Bin", "ProgramData", "System Volume Information", "Recovery", "AppData", "node_modules", ".git", "CUSA03173", "dvdroot_ps4" };
        var rs = roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var test in new Func<string?, bool>[] { IsPort, IsBb })
        {
            foreach (var d in near) if (test(d)) return d;
            if (!string.IsNullOrWhiteSpace(gameDir)) { if (test(gameDir)) return gameDir; if (test(Path.GetDirectoryName(gameDir))) return Path.GetDirectoryName(gameDir); }
            foreach (var r in rs) if (Scan(r, 3, skip, test) is { } hit) return hit;
        }
        return null;
    }

    static string? Scan(string dir, int depth, HashSet<string> skip, Func<string?, bool> test)
    {
        if (test(dir)) return dir;
        if (depth == 0) return null;
        string[] subs;
        try { subs = Directory.GetDirectories(dir); } catch { return null; }
        foreach (var s in subs)
        {
            if (skip.Contains(Path.GetFileName(s))) continue;
            if (Scan(s, depth - 1, skip, test) is { } hit) return hit;
        }
        return null;
    }

    // Steam's library folders (Lies of P is Steam app 1627720), then common install places.
    public static string? FindLop()
    {
        var roots = new List<string>();
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (steam != null)
            {
                roots.Add(steam);
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        catch { }
        foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var d = Path.Combine(r, "steamapps", "common", "Lies of P");
            if (IsLop(d)) return d;
        }
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady))
            foreach (var rel in new[] { @"SteamLibrary\steamapps\common\Lies of P", @"Program Files (x86)\Steam\steamapps\common\Lies of P", @"XboxGames\Lies of P\Content", @"Games\Lies of P" })
            {
                var d = Path.Combine(drive.RootDirectory.FullName, rel);
                if (IsLop(d)) return d;
            }
        return null;
    }
}
