using System.Text.Json;

namespace LopForge;

public static class Paths
{
    public static string Lop = "";
    public static string Bb = "";

    // "@file.json" anywhere in args loads lop/bb; "{lop}" and "{bb}" in other args expand to them.
    public static string[] Expand(string[] args)
    {
        var rest = new List<string>();
        foreach (var a in args)
        {
            if (a.StartsWith('@') && a.EndsWith(".json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(a[1..]));
                if (doc.RootElement.TryGetProperty("lop", out var l)) Lop = l.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("bb", out var b)) Bb = b.GetString() ?? "";
            }
            else rest.Add(a);
        }
        return rest.Select(a => a.Replace("{lop}", Lop).Replace("{bb}", Bb)).ToArray();
    }
}
