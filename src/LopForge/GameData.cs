using System.Text.Json;

namespace LopForge;

// Where Bloodborne's own game files (the folder holding dvdroot_ps4 and eboot.bin) are.
// The bbport-windows program folder ({game} in Melty) doesn't have to hold them: the launcher's
// game_dir setting can point anywhere (a "CUSA03173" folder elsewhere, or one named e.g. "Bloodborne GOTY v1.09").
public static class GameData
{
    static bool HasDvd(string? d) => !string.IsNullOrWhiteSpace(d) && Directory.Exists(Path.Combine(d, "dvdroot_ps4"))
        && (File.Exists(Path.Combine(d, "dvdroot_ps4", "paramdef", "paramdef.paramdefbnd.dcx")) || Directory.Exists(Path.Combine(d, "dvdroot_ps4", "chr")));

    // A folder pasted or set at any level (the game folder itself, its dvdroot_ps4, or a folder above it holding CUSA03173).
    static string? Fit(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        try
        {
            d = d.Trim().Trim('"').TrimEnd('\\', '/');
            if (d.Length == 0) return null;
            if (HasDvd(d)) return d;
            if (string.Equals(Path.GetFileName(d), "dvdroot_ps4", StringComparison.OrdinalIgnoreCase) && HasDvd(Path.GetDirectoryName(d))) return Path.GetDirectoryName(d);
            if (HasDvd(Path.Combine(d, "CUSA03173"))) return Path.Combine(d, "CUSA03173");
        }
        catch { }
        return null;
    }

    public static string? LauncherGameDir()
    {
        try
        {
            var s = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bbport-launcher", "settings.json");
            if (!File.Exists(s)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(s));
            return doc.RootElement.TryGetProperty("game_dir", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
        }
        catch { return null; }
    }

    // The game files folder for a bbport-windows program folder, or null when none can be found.
    public static string? Find(string bbDir)
    {
        if (Environment.GetEnvironmentVariable("BB_GAME_DIR") is { Length: > 0 } env && Fit(env) is { } e) return e;
        if (Fit(bbDir) is { } a) return a;
        // The launcher's own setting: this is where Play Bloodborne.exe reads the game from.
        if (Fit(LauncherGameDir()) is { } b) return b;
        // Last resort: a game folder just inside or beside the program folder.
        foreach (var root in new[] { bbDir, SafeParent(bbDir), SafeParent(SafeParent(bbDir)) })
        {
            if (root == null || !Directory.Exists(root)) continue;
            foreach (var d in SafeDirs(root))
            {
                if (Fit(d) is { } c) return c;
                foreach (var d2 in SafeDirs(d)) if (Fit(d2) is { } c2) return c2;
            }
        }
        return null;
    }

    static string? SafeParent(string? d) { try { return d == null ? null : Path.GetDirectoryName(d.TrimEnd('\\', '/')); } catch { return null; } }
    static IEnumerable<string> SafeDirs(string d) { try { return Directory.GetDirectories(d); } catch { return Array.Empty<string>(); } }
}
