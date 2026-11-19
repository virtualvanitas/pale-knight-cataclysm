using SoulsFormats;

namespace LopForge;

// Dev-only reading of the player's Bloodborne dump: what params, motions and parts exist.
public static class BbInspect
{
    public static bool UseLayers;
    public static Dictionary<string, PARAMDEF> Defs(string dvd)
    {
        var bnd = BND4.Read(Path.Combine(dvd, "paramdef", "paramdef.paramdefbnd.dcx"));
        var d = new Dictionary<string, PARAMDEF>();
        foreach (var f in bnd.Files)
        {
            try { var def = PARAMDEF.Read(f.Bytes); d[def.ParamType] = def; } catch { }
        }
        return d;
    }

    public static Dictionary<string, PARAM> Params(string dvd, out BND4 bnd)
    {
        var defs = Defs(dvd);
        bnd = BND4.Read(Environment.GetEnvironmentVariable("LOP_PARAM_FILE") is { Length: > 0 } pf ? pf : UseLayers ? Layers.Source(Path.Combine("param", "gameparam", "gameparam.parambnd.dcx")) : Path.Combine(dvd, "param", "gameparam", "gameparam.parambnd.dcx"));
        var r = new Dictionary<string, PARAM>();
        foreach (var f in bnd.Files)
        {
            var p = PARAM.Read(f.Bytes);
            if (defs.TryGetValue(p.ParamType, out var def)) p.ApplyParamdef(def);
            r[Path.GetFileNameWithoutExtension(f.Name)] = p;
        }
        return r;
    }

    public static void DumpRow(PARAM p, int id, TextWriter w)
    {
        var row = p.Rows.FirstOrDefault(r => r.ID == id);
        if (row == null) { w.WriteLine($"row {id} missing"); return; }
        w.WriteLine($"== {p.ParamType} {id} {row.Name}");
        foreach (var c in row.Cells) w.WriteLine($"  {c.Def.InternalName} = {c.Value}");
    }
}


