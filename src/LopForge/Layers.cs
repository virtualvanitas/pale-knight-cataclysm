using System.Text.Json;

namespace LopForge;

// Where the port loads mods from, and which version of a game file the game would see before this mod:
// the last enabled mod that replaces it (so this mod builds on top of the player's other mods), else the game's own.
public static class Layers
{
    public const string ModName = "Lies of P Arsenal";
    static string dvd = "";
    static readonly List<string> enabled = new();   // mod folders, in load order (excluding this one)
    public static string ModsDir { get; private set; } = "";

    public static void Init(string bbDir)
    {
        dvd = Path.Combine(GameData.Find(bbDir) ?? Path.Combine(bbDir, "CUSA03173"), "dvdroot_ps4");
        ModsDir = Path.Combine(bbDir, "mods");
        // The launcher's own setting wins (bbport-launcher settings.json: mods_dir), as Play Bloodborne.exe uses it.
        try
        {
            var s = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bbport-launcher", "settings.json");
            if (File.Exists(s))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(s));
                if (doc.RootElement.TryGetProperty("mods_dir", out var md) && md.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(md.GetString()))
                    ModsDir = md.GetString()!;
            }
        }
        catch { }
        if (Environment.GetEnvironmentVariable("BB_MODS_DIR") is { Length: > 0 } env) ModsDir = env;

        enabled.Clear();
        if (!Directory.Exists(ModsDir)) return;
        var available = Directory.GetDirectories(ModsDir).Select(Path.GetFileName).Where(n => n != null && n != ModName)
            .Select(n => n!).OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var order = new List<string>(); var disabled = new HashSet<string>();
        try
        {
            var cfg = Path.Combine(bbDir, "mods.json");
            if (File.Exists(cfg))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.TryGetProperty("order", out var o)) order.AddRange(o.EnumerateArray().Select(x => x.GetString() ?? ""));
                if (doc.RootElement.TryGetProperty("disabled", out var d)) foreach (var x in d.EnumerateArray()) disabled.Add(x.GetString() ?? "");
            }
        }
        catch { }
        foreach (var n in order.Concat(available).Distinct())
            if (available.Contains(n) && !disabled.Contains(n)) enabled.Add(n);
    }

    // A game file (path under dvdroot_ps4) as the game sees it with the player's other mods.
    public static string Source(string rel)
    {
        for (int i = enabled.Count - 1; i >= 0; i--)
        {
            var root = Path.Combine(ModsDir, enabled[i]);
            foreach (var c in new[] { Path.Combine(root, "dvdroot_ps4", rel), Path.Combine(root, rel) })
                if (File.Exists(c)) return c;
        }
        return Path.Combine(dvd, rel);
    }

    public static IEnumerable<string> Enabled => enabled;
    public static string Dvd => dvd;
}
