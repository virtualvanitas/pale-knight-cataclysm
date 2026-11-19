using LopForge;
using SoulsFormats;

// Paths can come from a json file ("@paths.json"): {"lop": "...", "bb": "..."}
args = Paths.Expand(args);
var cmd = args.Length > 0 ? args[0] : "";
// install [lop] [bb] : the stand-alone (Nexus) way in: finds both games itself (asking only for one it can't find),
// then builds the mod into the launcher's mods folder exactly as Melty's setup does.
if (cmd == "find")
{
    // find : where the installer would build from (for the windowed installer to fill in its folders)
    Console.WriteLine("bb=" + (Installer.FindBb() ?? ""));
    Console.WriteLine("lop=" + (Installer.FindLop() ?? ""));
    return 0;
}
if (cmd == "install")
{
    var found = Installer.Find(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
    if (found == null) return 2;
    args = new[] { "forge", found.Value.lop, found.Value.bb };
    cmd = "forge";
}
switch (cmd)
{
    case "key":
    {
        var lop = args[1];
        var exe = Path.Combine(lop, "LiesofP", "Binaries", "Win64", "LOP-Win64-Shipping.exe");
        var pak = Path.Combine(lop, "LiesofP", "Content", "Paks", "pakchunk0-WindowsNoEditor.pak");
        var key = KeyFinder.Find(exe, pak);
        Console.WriteLine(key == null ? "NO KEY" : "KEY 0x" + Convert.ToHexString(key));
        return key == null ? 2 : 0;
    }
    case "list":
    {
        // list <lop> <out.txt> : every file path in the archives
        var p = Lop.Open(args[1]);
        File.WriteAllLines(args[2], p.Files.Keys.OrderBy(k => k));
        Console.WriteLine($"{p.Files.Count} files");
        return 0;
    }
    case "json":
    {
        // json <lop> <outdir> <asset path>... : dump assets as JSON for reading (dev only)
        var p = Lop.Open(args[1]);
        Directory.CreateDirectory(args[2]);
        foreach (var a in args.Skip(3))
        {
            var exports = p.LoadPackage(a).GetExports();
            var txt = Newtonsoft.Json.JsonConvert.SerializeObject(exports, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Path.Combine(args[2], Path.GetFileNameWithoutExtension(a) + ".json"), txt);
            Console.WriteLine("ok " + a);
        }
        return 0;
    }
    case "barrelpic":
    {
        // barrelpic <partsbnd> <out.partsbnd> : a copy of the held model posed as its buffed-shot animation has it 0.5 s in
        // (each point moved by its weight on B_Barrel times the animated offset), for preview side by side (dev)
        var pb = BND4.Read(args[1]);
        var ff = pb.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) && !Path.GetFileNameWithoutExtension(f.Name).EndsWith("_1"));
        var fl = FLVER2.Read(ff.Bytes);
        var ab = BND4.Read(pb.Files.First(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)).Bytes);
        var an = BbAnim.Read(ab.Files.First(f => f.Name.EndsWith("_051010.hkx", StringComparison.OrdinalIgnoreCase)).Bytes);
        var rest = BbAnim.Read(ab.Files.First(f => f.Name.EndsWith("a999_000000.hkx", StringComparison.OrdinalIgnoreCase)).Bytes);
        int barrelNode = fl.Nodes.FindIndex(n => n.Name == "B_Barrel");
        var d = an.Pose[15, 1].Translation - rest.Pose[0, 1].Translation;
        int moved = 0;
        foreach (var v in fl.Meshes[0].Vertices)
        {
            float w = 0; for (int k = 0; k < 4; k++) if (v.BoneIndices[k] == barrelNode) w += v.BoneWeights[k];
            if (w > 0) { v.Position += d * w; moved++; }
        }
        Console.WriteLine($"barrel offset at 0.5 s: {d} ({d.Length():0.000} m), {moved} points moved");
        ff.Bytes = fl.Write();
        pb.Write(args[2]);
        return 0;
    }
    case "wptae":
    {
        // wptae <partsbnd> : a weapon animation package's TAE slots (header, file, events) and each hkx's tracks (dev)
        foreach (var f in BND4.Read(args[1]).Files.Where(x => x.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)))
        {
            var ab = BND4.Read(f.Bytes);
            Console.WriteLine($"anibnd format {ab.Format} unicode {ab.Unicode} ext {ab.Extended} version {ab.Version} ; outer file id {f.ID} flags {f.Flags} name {f.Name}");
            foreach (var x in ab.Files) Console.WriteLine($"  file {x.ID} flags {x.Flags} {x.Name}");
            var tf = ab.Files.First(x => x.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase));
            var t = TAE.Read(tf.Bytes);
            Console.WriteLine($"  TAE id {t.ID} format {t.Format} bigendian {t.BigEndian} flags {string.Join(",", t.Flags)} skel {t.SkeletonName} sib {t.SibName} eventbank {t.EventBank}");
            foreach (var a in t.Animations)
            {
                string hdr = a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io ? $"imports {io.ImportFromAnimID}" : a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st ? $"std importsHkx {st.ImportsHKX} src {st.ImportHKXSourceAnimID} loop {st.IsLoopByDefault} unk {st.AllowDelayLoad}" : "?";
                Console.WriteLine($"  {a.ID} {hdr} file {a.AnimFileName} events {a.Events.Count} groups {a.EventGroups.Count}");
            }
            foreach (var x in ab.Files.Where(x => x.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) && !x.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)))
            {
                var an = BbAnim.Read(x.Bytes);
                Console.WriteLine($"  {Path.GetFileName(x.Name)} frames {an.Frames} dt {an.FrameDuration} tracks {an.Tracks} map [{string.Join(",", an.TrackToBone)}]");
                for (int tr = 0; tr < an.Tracks; tr++) Console.WriteLine($"     track {tr}: f0 T {an.Pose[0, tr].Translation} R {an.Pose[0, tr].Rotation} | last T {an.Pose[an.Frames - 1, tr].Translation}");
                var ty = BbHavok.Find<HKX2.hkaAnimation>(BbHavok.Read(x.Bytes))!;
                Console.WriteLine($"     type {ty.GetType().Name} duration {ty.m_duration} motion {ty.m_extractedMotion?.GetType().Name}");
            }
            var sk = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(ab.Files.First(x => x.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
            for (int i = 0; i < sk.m_bones.Count; i++) Console.WriteLine($"  bone {i} {sk.m_bones[i].m_name} parent {sk.m_parentIndices[i]} ref {sk.m_referencePose[i].M11},{sk.m_referencePose[i].M12},{sk.m_referencePose[i].M13}");
        }
        return 0;
    }
    case "flvinfo":
    {
        // flvinfo <partsbnd> : each model's nodes, meshes, how vertices bind to nodes (dev)
        foreach (var f in BND4.Read(args[1]).Files.Where(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
        {
            var fl = FLVER2.Read(f.Bytes);
            Console.WriteLine($"== {Path.GetFileName(f.Name)} unk68 {fl.Header.Unk68} version {fl.Header.Version:X}");
            for (int i = 0; i < fl.Nodes.Count; i++) { var n = fl.Nodes[i]; Console.WriteLine($"  node {i} {n.Name} parent {n.ParentIndex} child {n.FirstChildIndex} T {n.Translation} R {n.Rotation} flags {n.Flags}"); }
            for (int i = 0; i < fl.Meshes.Count; i++)
            {
                var me = fl.Meshes[i];
                var v0 = me.Vertices.FirstOrDefault();
                var used = me.Vertices.Select(v => v.BoneIndices[0]).Distinct().OrderBy(x => x).Take(10);
                Console.WriteLine($"  mesh {i} node {me.NodeIndex} dynamic {me.UseBoneWeights} boneIdx [{string.Join(",", me.BoneIndices)}] verts {me.Vertices.Count} mat {me.MaterialIndex} buffers {me.VertexBuffers.Count} layout {string.Join(",", me.VertexBuffers.Select(b => b.LayoutIndex))} v0 bi [{(v0 == null ? "" : string.Join(",", Enumerable.Range(0, 4).Select(k => v0.BoneIndices[k])))}] bw [{(v0 == null ? "" : string.Join(",", Enumerable.Range(0, 4).Select(k => v0.BoneWeights[k])))}] bi0 used {string.Join(",", used)}");
            }
            foreach (var d in fl.Dummies.Take(40)) Console.WriteLine($"  dummy {d.ReferenceID} attach {d.AttachBoneIndex} parent {d.ParentBoneIndex} pos {d.Position}");
            foreach (var bl in fl.BufferLayouts) Console.WriteLine($"  layout: {string.Join(",", bl.Select(m => m.Semantic + ":" + m.Type))}");
        }
        return 0;
    }
    case "fmg":
    {
        // fmg <msgbnd> <fmgId> <from> <to> : text entries with ids in [from, to] (dev)
        var mb = BND4.Read(args[1]);
        int fid = int.Parse(args[2]); long lo = long.Parse(args[3]), hi = long.Parse(args[4]);
        foreach (var f in mb.Files.Where(x => x.ID == fid))
            foreach (var e in FMG.Read(f.Bytes).Entries.Where(e => e.ID >= lo && e.ID <= hi))
                Console.WriteLine($"{e.ID}\t{e.Text?.Replace("\n", "\\n")}");
        return 0;
    }
    case "locres":
    {
        // locres <lop> <out.txt> <grep>
        var p = Lop.Open(args[1]);
        using var ar = p.CreateReader("LiesofP/Content/Localization/LOP_Game/en/LOP_Game.locres");
        var loc = new CUE4Parse.UE4.Localization.FTextLocalizationResource(ar);
        using var w = new StreamWriter(args[2]);
        foreach (var ns in loc.Entries)
            foreach (var kv in ns.Value)
                w.WriteLine($"{ns.Key.Str}\t{kv.Key.Str}\t{kv.Value.LocalizedString.Replace("\n", "\\n")}");
        return 0;
    }
    case "bbparams":
    {
        // bbparams <bb> <out.txt> [param] [ids...] : list params, or dump rows
        var dvd = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4");
        var ps = BbInspect.Params(dvd, out _);
        using var w = new StreamWriter(args[2]);
        if (args.Length == 3)
            foreach (var kv in ps) w.WriteLine($"{kv.Key}\t{kv.Value.ParamType}\t{kv.Value.Rows.Count}");
        else if (args.Length == 4)
            foreach (var r in ps[args[3]].Rows) w.WriteLine($"{r.ID}\t{r.Name}");
        else
            foreach (var id in args.Skip(4)) BbInspect.DumpRow(ps[args[3]], int.Parse(id), w);
        return 0;
    }
    case "dummies":
    {
        // dummies <bb> <id...> : the Hunter body model's dummy points (where weapons attach), with their bones (dev)
        var chrbnd = BND4.Read(Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "chr", "c0000.chrbnd.dcx"));
        var fl = FLVER2.Read(chrbnd.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var id in args.Skip(2).Select(short.Parse))
            foreach (var d in fl.Dummies.Where(d => d.ReferenceID == id))
                Console.WriteLine($"{id}: pos {d.Position} fwd {d.Forward} up {d.Upward} useUp {d.UseUpwardVector} parent {(d.ParentBoneIndex >= 0 ? fl.Nodes[d.ParentBoneIndex].Name : "-")} attach {(d.AttachBoneIndex >= 0 ? fl.Nodes[d.AttachBoneIndex].Name : "-")}");
        return 0;
    }
    case "dummyvsbone":
    {
        // dummyvsbone <bb> <id> <bone> : how far the body dummy's frame is turned from the bone's rest frame (dev)
        var chr = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "chr");
        var sk = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(chr, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        var (P, R) = Retarget.Model(sk.Parent, i => sk.T[i], i => sk.R[i]);
        var (dp, dr) = ForgeModels.BodyDummy(short.Parse(args[2]), Path.Combine(chr, "c0000.chrbnd.dcx"));
        int b = sk.Index(args[3]);
        var rel = System.Numerics.Quaternion.Inverse(R[b]) * dr;
        float ang = 2 * MathF.Acos(Math.Clamp(MathF.Abs(rel.W), 0, 1)) * 180 / MathF.PI;
        Console.WriteLine($"bone {args[3]} at {P[b]}, dummy at {dp}, turned {ang:0.0} deg; bone axes X {System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, R[b])} Y {System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, R[b])} Z {System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, R[b])}");
        return 0;
    }
    case "fieldscan":
    {
        // fieldscan <bb> <out.txt> <param> <field> : the field's description and every row's value where it isn't 0
        var dvd = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4");
        var ps = BbInspect.Params(dvd, out _);
        using var w = new StreamWriter(args[2], false, new System.Text.UTF8Encoding(false));
        var p = ps[args[3]];
        var fd = p.AppliedParamdef.Fields.First(f => f.InternalName == args[4]);
        w.WriteLine($"{fd.InternalName} type={fd.InternalType} display={fd.DisplayName} desc={fd.Description}");
        foreach (var r in p.Rows) { var v = r[args[4]].Value; if (Convert.ToDouble(v) != 0) w.WriteLine($"{r.ID}\t{v}\t{r.Name}"); }
        return 0;
    }
    case "bbfmg":
    {
        // bbfmg <bb> <out.txt> [lang] : every text in the item/menu msgbnds with its FMG name
        var lang = args.Length > 3 ? args[3] : "engus";
        var dir = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "msg", lang);
        using var w = new StreamWriter(args[2]);
        foreach (var f in Directory.GetFiles(dir, "*.msgbnd.dcx"))
        {
            var bnd = BND4.Read(f);
            foreach (var bf in bnd.Files)
            {
                var fmg = SoulsFormats.FMG.Read(bf.Bytes);
                foreach (var e in fmg.Entries)
                    if (!string.IsNullOrEmpty(e.Text))
                        w.WriteLine($"{Path.GetFileName(f)}|{bf.ID}|{Path.GetFileName(bf.Name)}\t{e.ID}\t{e.Text.Replace("\n", "\\n")}");
            }
        }
        return 0;
    }
    case "bnd":
    {
        // bnd <file> [filter] : list a BND4 (dcx) archive
        var bnd = BND4.Read(args[1]);
        Console.WriteLine($"format={bnd.Format} version={bnd.Version} files={bnd.Files.Count}");
        foreach (var f in bnd.Files.Where(f => args.Length < 3 || f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"{f.ID}\t{f.Bytes.Length}\t{f.Name}");
        return 0;
    }
    case "tae":
    {
        // tae <anibnd> <taeName> [animId] : list a TAE's animations, or one animation's events
        var bnd = BND4.Read(args[1]);
        var bf = bnd.Files.First(f => f.Name.EndsWith("\\" + args[2]));
        var tae = TAE.Read(bf.Bytes);
        Console.WriteLine($"tae id={tae.ID} format={tae.Format} anims={tae.Animations.Count} skel={tae.SkeletonName} sib={tae.SibName}");
        foreach (var a in tae.Animations)
        {
            if (args.Length > 3 && a.ID.ToString() != args[3]) continue;
            var mh = Newtonsoft.Json.JsonConvert.SerializeObject(a.MiniHeader);
            Console.WriteLine($"{a.ID}\t{a.AnimFileName}\tev={a.Events.Count}\t{mh}");
            if (args.Length > 3)
                foreach (var e in a.Events)
                    Console.WriteLine($"   t={e.StartTime:0.000}-{e.EndTime:0.000} type={e.Type} {Convert.ToHexString(e.GetParameterBytes(false))}");
        }
        return 0;
    }
    case "parts":
    {
        // parts <partsbnd> : textures and flver summary
        var bnd = BND4.Read(args[1]);
        foreach (var f in bnd.Files)
        {
            if (f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
            {
                var tpf = TPF.Read(f.Bytes);
                Console.WriteLine($"TPF platform={tpf.Platform} enc={tpf.Encoding} flag2={tpf.Flag2}");
                foreach (var t in tpf.Textures)
                    Console.WriteLine($"  {t.Name} fmt={t.Format} type={t.Type} mips={t.Mipmaps} flags1={t.Flags1} bytes={t.Bytes.Length} hdr={(t.Header == null ? "-" : $"{t.Header.Width}x{t.Header.Height} c={t.Header.TextureCount} u1={t.Header.Unk1} u2={t.Header.Unk2} dxgi={t.Header.DXGIFormat}")} fl={t.FloatStruct != null}");
            }
            else if (f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
            {
                var fl = FLVER2.Read(f.Bytes);
                Console.WriteLine($"FLVER {Path.GetFileName(f.Name)} ver={fl.Header.Version:X} bones={fl.Nodes.Count} dummies={fl.Dummies.Count} mats={fl.Materials.Count} meshes={fl.Meshes.Count} layouts={fl.BufferLayouts.Count}");
                foreach (var m in fl.Materials)
                    Console.WriteLine($"  mat {m.Name} mtd={m.MTD} tex=[{string.Join(", ", m.Textures.Select(t => t.ParamName + ":" + t.Path))}]");
                foreach (var b in fl.Nodes) Console.WriteLine($"  bone {b.Name} parent={b.ParentIndex}");
                foreach (var dm in fl.Dummies) Console.Write($" d{dm.ReferenceID}");
                Console.WriteLine();
                foreach (var me in fl.Meshes)
                    Console.WriteLine($"  mesh mat={me.MaterialIndex} verts={me.Vertices.Count} node={me.NodeIndex} dyn={me.Dynamic} layouts=[{string.Join(",", me.VertexBuffers.Select(v => v.LayoutIndex))}] bones=[{string.Join(",", me.BoneIndices)}]");
                for (int i = 0; i < fl.BufferLayouts.Count; i++)
                    Console.WriteLine($"  layout{i}: {string.Join(" ", fl.BufferLayouts[i].Select(l => l.Semantic + "/" + l.Type))}");
            }
        }
        return 0;
    }
    case "mesh":
    {
        // mesh <lop> <asset path> : summary of a skinned mesh
        var p = Lop.Open(args[1]);
        var m = LopMesh.Load(p, args[2]);
        Console.WriteLine($"verts={m.Verts.Count} sections={m.Sections.Count} bones={m.Bones.Count}");
        var min = new System.Numerics.Vector3(1e9f); var max = -min;
        foreach (var v in m.Verts) { min = System.Numerics.Vector3.Min(min, v.P); max = System.Numerics.Vector3.Max(max, v.P); }
        Console.WriteLine($"bounds {min} .. {max}");
        for (int i = 0; i < m.Bones.Count; i++) Console.WriteLine($"  bone{i} {m.Bones[i].Name} parent={m.Bones[i].Parent} t={m.Bones[i].T} r={m.Bones[i].R}");
        foreach (var g in m.Verts.GroupBy(v => { int b = 0; for (int k = 1; k < 4; k++) if (v.Bw[k] > v.Bw[b]) b = k; return v.Bi[b]; }).OrderBy(g => g.Key))
        {
            var mn = new System.Numerics.Vector3(1e9f); var mx = -mn;
            foreach (var v in g) { mn = System.Numerics.Vector3.Min(mn, v.P); mx = System.Numerics.Vector3.Max(mx, v.P); }
            Console.WriteLine($"  dom {g.Key} {(g.Key < m.Bones.Count ? m.Bones[g.Key].Name : "?")} n={g.Count()} {mn:0} .. {mx:0}");
        }
        foreach (var s in m.Sections) Console.WriteLine($"  section mat={s.Material} tris={s.Tris.Length / 3}");
        foreach (var s in m.Materials) Console.WriteLine($"  material {s}");
        var used = m.Verts.SelectMany(v => v.Bi.Zip(v.Bw).Where(z => z.Second > 0).Select(z => z.First)).GroupBy(b => b).Select(g => $"{g.Key}:{g.Count()}");
        Console.WriteLine("  weights " + string.Join(" ", used));
        return 0;
    }
    case "anim":
    {
        // anim <lop> <asset path> : summary of an animation sequence
        var p = Lop.Open(args[1]);
        var a = LopAnim.Load(p, args[2]);
        Console.WriteLine($"frames={a.Frames} fps={a.Fps} tracks={a.Tracks.Count} notifies={a.Notifies.Count} skeleton={a.Skeleton}");
        foreach (var t in a.Tracks.Take(80)) Console.WriteLine($"  {t.Bone}: t0={t.T[0]} r0={t.R[0]}");
        foreach (var n in a.Notifies) Console.WriteLine($"  notify {n}");
        return 0;
    }
    case "bbanim":
    {
        // bbanim <anibnd> <name> [bone names,...] : decoded frames of a Bloodborne animation (dev)
        var bnd = BND4.Read(args[1]);
        var bf = bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
        var a = BbAnim.Read(bf.Bytes);
        Console.WriteLine($"frames={a.Frames} dt={a.FrameDuration} tracks={a.Tracks} rootmotion={a.RootMotion.Count} up={a.Up} fwd={a.Forward}");
        var skel = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(Path.GetDirectoryName(args[1])!, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        var want = args.Length > 3 ? args[3].Split(',') : new[] { "Master", "Root", "Pelvis", "UpperPos", "UpperTurn", "Spine", "R_Hand", "R_Weapon" };
        for (int t = 0; t < a.Tracks; t++)
        {
            int bone = a.TrackToBone.Length > t ? a.TrackToBone[t] : t;
            var name = skel.m_bones[bone].m_name;
            if (!want.Contains(name)) continue;
            Console.WriteLine($"{name} (track {t}, bone {bone})");
            for (int f = 0; f < a.Frames; f += Math.Max(1, a.Frames / 6))
                Console.WriteLine($"   f{f} T={a.Pose[f, t].Translation:0.###} R={a.Pose[f, t].Rotation:0.###}");
        }
        for (int i = 0; i < a.RootMotion.Count; i += Math.Max(1, a.RootMotion.Count / 8)) Console.WriteLine($"  rm{i} {a.RootMotion[i]:0.###}");
        return 0;
    }
    case "animconv":
    {
        // animconv <lop> <bb> <outdir> [bbAnimId...] : convert moves-sheet rows, write hkx + stick-figure previews (dev)
        var p = Lop.Open(args[1]);
        var chr = Path.Combine(args[2], "CUSA03173", "dvdroot_ps4", "chr");
        var skelHk = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(chr, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        var bb = Retarget.BbSkel.From(skelHk);
        var a9x = BND4.Read(Path.Combine(chr, "c0000_a9x.anibnd.dcx"));
        var tmpl = a9x.Files.First(f => f.Name.EndsWith("a112_030000.hkx")).Bytes;
        Directory.CreateDirectory(args[3]);
        var cache = new Dictionary<string, LopAnim>();
        var want = args.Skip(4).Select(int.Parse).ToHashSet();
        foreach (var row in Sheets.Load<MoveRow>("moves").Where(r => want.Count == 0 || want.Contains(r.bbAnimId)))
        {
            var r = ForgeAnims.Convert(p, row, bb, tmpl, cache);
            var name = $"a{row.bbCategory}_{row.bbAnimId:D6}";
            File.WriteAllBytes(Path.Combine(args[3], name + ".hkx"), r.Hkx);
            var back = BbAnim.Read(r.Hkx);
            AnimPreview.Render(back, bb, Path.Combine(args[3], name + ".png"));
            Compare.Render(cache[r.Montage.Sequence.Split('.')[0]], r.Montage, back, bb, Path.Combine(args[3], name + "_cmp.png"));
            Console.WriteLine($"{name}: {row.lopMontage} {r.Frames} frames {r.Duration:0.00}s, travel {back.RootMotion.Last():0.00}");
            { int wb = bb.Index("R_Weapon"), wt = Array.IndexOf(back.TrackToBone, (short)wb); float mx = 0; for (int q = 0; q < back.Frames; q++) { var lr = System.Numerics.Quaternion.Normalize(back.Pose[q, wt].Rotation); mx = MathF.Max(mx, 2 * MathF.Acos(Math.Min(1f, MathF.Abs(System.Numerics.Quaternion.Dot(lr, bb.R[wb])))) * 57.3f); } Console.WriteLine($"   weapon bone turns up to {mx:0} deg against the hand"); }
            Console.WriteLine("   limb direction error mean/max deg: " + Compare.Measure(cache[r.Montage.Sequence.Split('.')[0]], r.Montage, back, bb));
        }
        if (args.Length > 4 && want.Count == 0) { }
        return 0;
    }
    case "animref":
    {
        // animref <anibnd> <name> <out.png> : stick-figure preview of one of the game's animations (dev)
        var bnd = BND4.Read(args[1]);
        var a = BbAnim.Read(bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Bytes);
        var skelHk = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(Path.GetDirectoryName(args[1])!, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        AnimPreview.Render(a, Retarget.BbSkel.From(skelHk), args[3]);
        return 0;
    }
    case "rtprobe":
    {
        // rtprobe <lop> <bb> <anim path> : rest poses of both rigs side by side (dev)
        var p = Lop.Open(args[1]);
        var a = LopAnim.Load(p, args[3]);
        var skel = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(args[2], "CUSA03173", "dvdroot_ps4", "chr", "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        Retarget.Probe(a, Retarget.BbSkel.From(skel), Console.WriteLine);
        ForgeAnims.GripProbe(a, Retarget.BbSkel.From(skel), "R", Console.WriteLine);
        ForgeAnims.GripProbe(a, Retarget.BbSkel.From(skel), "L", Console.WriteLine);
        return 0;
    }
    case "montanim":
    {
        // montanim <lop> <montage path> : the montage's sequence and its skeleton's bones (dev)
        var p = Lop.Open(args[1]);
        var m = LopMontage.Load(p, args[2]);
        Console.WriteLine($"montage len={m.Length} seq={m.Sequence} [{m.SeqStart}-{m.SeqEnd} x{m.Rate}]");
        var seqPath = m.Sequence.Split('.')[0];
        var a = LopAnim.Load(p, seqPath);
        Console.WriteLine($"frames={a.Frames} fps={a.Fps} tracks={a.Tracks.Count} skeleton={a.Skeleton}");
        foreach (var t in a.Tracks) Console.WriteLine($"  {t.Bone}: t0={t.T[0]} r0={t.R[0]}");
        return 0;
    }
    case "behscan":
    {
        // behscan <behavior.hkx> [category] : classes, and the selectors holding a category's clips (dev)
        var pf = new Packfile(File.ReadAllBytes(args[1]));
        Console.WriteLine($"loc={pf.LocOff} glob={pf.GlobOff} virt={pf.VirtOff} exp={pf.ExpOff} imp={pf.ImpOff} end={pf.EndOff} data={pf.Data.Count}");
        var objs = pf.Objects();
        foreach (var g in objs.GroupBy(o => o.cls).OrderByDescending(g => g.Count()).Take(25)) Console.WriteLine($"  {g.Key} x{g.Count()} size~{g.Min(o => o.end - o.start)}..{g.Max(o => o.end - o.start)}");
        var cat = args.Length > 2 ? args[2] : "143";
        var localBySrc = pf.Local.ToDictionary(l => l.src, l => l.dst);
        string NameOf((uint start, uint end, string cls, uint n) o) { foreach (var l in pf.Local) if (l.src >= o.start && l.src < o.end) { try { var s = pf.Str(l.dst); if (s.Length > 2 && s.All(c => c >= 32 && c < 127)) return s + $"@+{l.src - o.start}"; } catch { } } return "?"; }
        var ccg = objs.Where(o => o.cls == "hkbClipGenerator").ToList();
        var mine = ccg.Where(o => NameOf(o).StartsWith("a" + cat + "_")).ToList();
        Console.WriteLine($"CustomClipGenerator a{cat}: {mine.Count}");
        foreach (var o in mine.Take(3))
        {
            Console.WriteLine($"  {NameOf(o)} at {o.start} size {o.end - o.start}: {Convert.ToHexString(pf.Data.GetRange((int)o.start, (int)(o.end - o.start)).ToArray())}");
            foreach (var l in pf.Local.Where(l => l.src >= o.start && l.src < o.end)) Console.WriteLine($"    local +{l.src - o.start} -> {l.dst}");
            foreach (var gfx in pf.Global.Where(gg => gg.src >= o.start && gg.src < o.end)) Console.WriteLine($"    global +{gfx.src - o.start} -> {gfx.dst}");
        }
        // Who points at them
        var mineStarts = mine.Select(o => o.start).ToHashSet();
        var parents = pf.Global.Where(gg => mineStarts.Contains(gg.dst)).ToList();
        Console.WriteLine($"pointers to them: {parents.Count}");
        var objAt = objs.ToArray();
        (uint start, uint end, string cls, uint n)? Owner(uint at) { foreach (var o in objAt) if (at >= o.start && at < o.end) return o; return null; }
        // Array storage is outside objects: find the local fixup whose dst range contains the pointer slot.
        int shown = 0;
        foreach (var p in parents.Take(400))
        {
            var arr = pf.Local.Where(l => l.dst <= p.src && p.src - l.dst < 8 * 64).OrderByDescending(l => l.dst).FirstOrDefault();
            var own = Owner(arr.src);
            if (shown++ < 6 && own != null) Console.WriteLine($"  slot {p.src}: array@{arr.dst} hdr@{arr.src} size={pf.I32(arr.src + 8)} in {own.Value.cls} {NameOf(own.Value)} hdr+{arr.src - own.Value.start} offsetType={pf.I32(arr.src + 16)} animId={pf.I32(arr.src + 20)}");
        }
        var sel = parents.Select(p => { var arr = pf.Local.Where(l => l.dst <= p.src && p.src - l.dst < 8 * 64).OrderByDescending(l => l.dst).First(); return Owner(arr.src)?.cls ?? "none"; }).GroupBy(x => x);
        foreach (var g in sel) Console.WriteLine($"  parent class {g.Key} x{g.Count()}");
        return 0;
    }
    case "localprobe":
    {
        // localprobe <lop> <bb> : per mapped bone, the two rigs' rest LOCAL rotations (Lies of P mirrored back to Max) (dev)
        var p = Lop.Open(args[1]);
        var skel = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(args[2], "CUSA03173", "dvdroot_ps4", "chr", "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        var bb = Retarget.BbSkel.From(skel);
        var row = Sheets.Load<MoveRow>("moves").First();
        var mt = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + row.lopMontage);
        var a = LopAnim.Load(p, mt.Sequence.Split('.')[0]);
        foreach (var (bn, ln) in Retarget.Map)
        {
            int b = bb.Index(bn), l = a.RefBones.FindIndex(x => x.name == ln);
            if (b < 0 || l < 0) { Console.WriteLine($"{bn}: missing ({b},{l})"); continue; }
            var lr = Retarget.Rh(a.RefBones[l].r); var br = bb.R[b];
            float ang(System.Numerics.Quaternion x, System.Numerics.Quaternion y) => 2 * MathF.Acos(Math.Min(1, MathF.Abs(System.Numerics.Quaternion.Dot(System.Numerics.Quaternion.Normalize(x), System.Numerics.Quaternion.Normalize(y))))) * 180 / MathF.PI;
            var lp = a.RefBones[l].parent >= 0 ? a.RefBones[a.RefBones[l].parent].name : "-";
            var bp = bb.Parent[b] >= 0 ? bb.Names[bb.Parent[b]] : "-";
            Console.WriteLine($"{bn,-12} parent bb={bp,-12} lop={lp,-18} localAngle={ang(lr, br),6:0.0}  bbT={bb.T[b]:0.000} lopT={Retarget.Rh(a.RefBones[l].t) / 100:0.000}");
        }
        return 0;
    }
    case "restdev":
    {
        // restdev <anibnd> <name> : per track, how far it moves from the skeleton's rest local pose (max angle, max offset) (dev)
        var bnd = BND4.Read(args[1]);
        var a = BbAnim.Read(bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Bytes);
        var skel = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(Path.GetDirectoryName(args[1])!, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        for (int t = 0; t < a.Tracks; t++)
        {
            int b = a.TrackToBone[t]; float ma = 0, mt = 0;
            for (int f = 0; f < a.Frames; f++)
            {
                ma = Math.Max(ma, 2 * MathF.Acos(Math.Min(1, MathF.Abs(System.Numerics.Quaternion.Dot(System.Numerics.Quaternion.Normalize(a.Pose[f, t].Rotation), skel.R[b])))) * 57.3f);
                mt = Math.Max(mt, (a.Pose[f, t].Translation - skel.T[b]).Length());
            }
            var n = skel.Names[b];
            if (n.Contains("Finger")) continue;
            Console.WriteLine($"{n,-28} rot<= {ma,6:0.0}  move<= {mt:0.000}");
        }
        return 0;
    }
    case "iktest":
    {
        // iktest <anibnd> <name> : in a game animation, where the foot targets sit relative to the feet (dev)
        var bnd = BND4.Read(args[1]);
        var a = BbAnim.Read(bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Bytes);
        var skel = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(Path.GetDirectoryName(args[1])!, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        for (int f = 0; f < a.Frames; f += Math.Max(1, a.Frames / 5))
        {
            var (P, R) = Retarget.Model(skel.Parent,
                i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? a.Pose[f, t].Translation : skel.T[i]; },
                i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? a.Pose[f, t].Rotation : skel.R[i]; });
            foreach (var s in new[] { "L", "R" })
            {
                int ft = skel.Index($"{s}_Foot"), tg = skel.Index($"{s}_Foot_Target"), toe = skel.Index($"{s}_Toe0");
                float ang = 2 * MathF.Acos(Math.Min(1, MathF.Abs(System.Numerics.Quaternion.Dot(R[ft], R[tg])))) * 57.3f;
                Console.WriteLine($"f{f} {s}: foot={P[ft]:0.000} target={P[tg]:0.000} d={(P[ft] - P[tg]).Length():0.000} rotdiff={ang:0.0} toeY={P[toe].Y:0.000}");
            }
        }
        return 0;
    }
    case "selnames":
    {
        // selnames <behavior.hkx> : every weapon-category selector's name and anim id (dev)
        var pf = new Packfile(File.ReadAllBytes(args[1]));
        var rows = new SortedSet<string>();
        foreach (var o in pf.Objects().Where(o => o.cls == "CustomManualSelectorGenerator"))
        {
            string name = "?";
            var nl = pf.Local.FirstOrDefault(l => l.src == o.start + 56);
            if (nl.src != 0) name = pf.Str(nl.dst);
            uint hdr = o.start + 136;
            int off = pf.I32(hdr + 16), id = pf.I32(hdr + 20);
            if (off == 13 || off == 14) rows.Add($"{id:D6} {(off == 13 ? "R" : "L")} {name}");
        }
        foreach (var r in rows) Console.WriteLine(r);
        return 0;
    }
    case "bndget":
    {
        // bndget <bnd> <name filter> <out> : extract one file (dev)
        var bnd = BND4.Read(args[1]);
        File.WriteAllBytes(args[3], bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Bytes);
        return 0;
    }
    case "charnames":
    {
        // charnames <anibnd> : how the Hunter's animation name list is laid out (dev)
        var bnd = BND4.Read(args[1]);
        var which = args.Length > 2 ? args[2] : @"Characters\c0000.hkx"; var keyName = args.Length > 3 ? args[3] : "a000_000000";
        var bf = bnd.Files.First(f => f.Name.EndsWith(which, StringComparison.OrdinalIgnoreCase));
        CharNames.Debug(bf.Bytes, Console.WriteLine, keyName);
        var names = CharNames.Read(bf.Bytes, keyName);
        if (args.Length > 4) File.WriteAllLines(args[4], names);
        int unsorted = 0; for (int i = 1; i < names.Count; i++) if (string.CompareOrdinal(names[i - 1], names[i]) > 0) { if (unsorted++ < 8) Console.WriteLine($"  order break at {i}: {names[i - 1]} > {names[i]}"); }
        Console.WriteLine($"  order breaks: {unsorted}; sample: {string.Join(" ", names.Skip(800).Take(6))}");
        return 0;
    }
    case "hkx":
    {
        // hkx <bnd> <name filter> : read a Havok file from a BND, summarise, round-trip
        var bnd = BND4.Read(args[1]);
        var bf = bnd.Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
        var root = BbHavok.Read(bf.Bytes);
        foreach (var v in root.m_namedVariants) Console.WriteLine($"variant {v.m_name} {v.m_className} {v.m_variant?.GetType().Name}");
        var sk = BbHavok.Find<HKX2.hkaSkeleton>(root);
        if (sk != null)
        {
            Console.WriteLine($"skeleton {sk.m_name} bones={sk.m_bones.Count}");
            for (int i = 0; i < sk.m_bones.Count; i++)
            {
                var m = sk.m_referencePose[i];
                Console.WriteLine($"  {i} {sk.m_bones[i].m_name} parent={sk.m_parentIndices[i]} t=({m.M11:0.###},{m.M12:0.###},{m.M13:0.###}) r=({m.M21:0.###},{m.M22:0.###},{m.M23:0.###},{m.M24:0.###})");
            }
        }
        var an = BbHavok.Find<HKX2.hkaAnimation>(root);
        if (an != null) Console.WriteLine($"anim {an.GetType().Name} dur={an.m_duration} tracks={an.m_numberOfTransformTracks} motion={an.m_extractedMotion?.GetType().Name}");
        var bi = BbHavok.Find<HKX2.hkaAnimationBinding>(root);
        if (bi != null) Console.WriteLine($"binding skel={bi.m_originalSkeletonName} map={bi.m_transformTrackToBoneIndices.Count} blend={bi.m_blendHint}");
        var again = BbHavok.Write(root);
        var root2 = BbHavok.Read(again);
        Console.WriteLine($"roundtrip in={bf.Bytes.Length} out={again.Length} reread variants={root2.m_namedVariants.Count}");
        if (args.Length > 3) File.WriteAllBytes(args[3], again);
        if (args.Length > 4) File.WriteAllBytes(args[4], bf.Bytes);
        return 0;
    }
    case "armlop":
    {
        // armlop <lop> <montage> <mesh> <out.png> : Pino in Lies of P with the arm mesh skinned (rigid, dominant bone), Bloodborne space (dev)
        var p = Lop.Open(args[1]);
        var mt = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + args[2]);
        var a = LopAnim.Load(p, mt.Sequence.Split('.')[0]);
        var mesh = LopMesh.Load(p, args[3]);
        var (bp, br) = Retarget.Model(mesh.Bones.Select(b => b.Parent).ToList(), i => mesh.Bones[i].T, i => mesh.Bones[i].R);
        var lpar = a.RefBones.Select(b => b.parent).ToList();
        var trackOf = a.RefBones.Select(b => a.Tracks.FindIndex(t => t.Bone == b.name)).ToArray();
        int lRoot = a.RefBones.FindIndex(b => b.name == "Root");
        int cols = 4, cell = 300; using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(cols * cell, cell * 2, new SixLabors.ImageSharp.PixelFormats.Rgba32(25, 25, 30, 255));
        var keep = new[] { "Bip001-L-Forearm", "Bn_L_ForeTwist1", "Bn_L_ForeTwist", "BN_ChargeShotgun_Base", "BN_ChargeShotgun_Rotate", "BN_ChargeShotgun_Move", "BN_ChargeShotgun_A", "BN_ChargeShotgun_B", "BN_ChargeShotgun_C" };
        float t0 = mt.SeqStart, t1 = mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps;
        for (int s = 0; s < cols; s++)
        {
            int f = Math.Clamp((int)MathF.Round((t0 + s / (float)(cols - 1) * (t1 - t0)) * a.Fps), 0, a.Frames - 1);
            var (lp, lr) = Retarget.Model(lpar, i => i == lRoot ? System.Numerics.Vector3.Zero : trackOf[i] >= 0 ? a.Tracks[trackOf[i]].T[f] : a.RefBones[i].t, i => trackOf[i] >= 0 ? a.Tracks[trackOf[i]].R[f] : a.RefBones[i].r);
            for (int view = 0; view < 2; view++)
            {
                int ox = s * cell + cell / 2, oy = view * cell + cell - 20; float sc = 120;
                System.Numerics.Vector2 Pr(System.Numerics.Vector3 q) { var g = ForgeAnims.Gp(q); return view == 0 ? new(ox + g.X * sc, oy - g.Y * sc) : new(ox + g.Z * sc, oy - g.Y * sc); }
                for (int vi = 0; vi < mesh.Verts.Count; vi += 3)
                {
                    var v = mesh.Verts[vi]; int db = 0; for (int k = 1; k < 4; k++) if (v.Bw[k] > v.Bw[db]) db = k;
                    int mb = v.Bi[db]; var nm = mesh.Bones[mb].Name; if (!keep.Contains(nm)) continue;
                    int ab = a.RefBones.FindIndex(b => b.name == nm); if (ab < 0) continue;
                    var local = System.Numerics.Vector3.Transform(v.P - bp[mb], System.Numerics.Quaternion.Inverse(br[mb]));
                    var q = Pr(lp[ab] + System.Numerics.Vector3.Transform(local, lr[ab]));
                    int x = (int)q.X, y = (int)q.Y; if (x >= 0 && y >= 0 && x < img.Width && y < img.Height) img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 210, 80, 255);
                }
                for (int b = 0; b < lp.Length; b++)
                {
                    int pa = lpar[b]; if (pa < 0) continue; var n = a.RefBones[b].name;
                    if (!n.StartsWith("Bip001") || n.Contains("Finger") || n.Contains("Twist")) continue;
                    AnimPreview.Line(img, Pr(lp[pa]), Pr(lp[b]), n.Contains("-L-") ? new SixLabors.ImageSharp.PixelFormats.Rgba32(90, 170, 255, 255) : new SixLabors.ImageSharp.PixelFormats.Rgba32(200, 200, 200, 255));
                }
            }
        }
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, args[4]);
        return 0;
    }
    case "armfit":
    {
        // armfit <bb> <anim .hkx file> <partsbnd> <out.png> : the Hunter (sticks) in frames of the move with the left-hand
        // weapon model's points where the game puts them (L_Weapon), front and side (dev)
        var chr = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "chr");
        var bb = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(chr, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        var an = BbAnim.Read(File.ReadAllBytes(args[2]));
        var fl = FLVER2.Read(BND4.Read(args[3]).Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var vs = fl.Meshes[0].Vertices.Select(v => v.Position).Where((v, i) => i % 3 == 0).ToList();
        int cols = 4, cell = 300; using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(cols * cell, cell * 2, new SixLabors.ImageSharp.PixelFormats.Rgba32(25, 25, 30, 255));
        int w = bb.Index("L_Weapon");
        for (int s = 0; s < cols; s++)
        {
            int fr = s * (an.Frames - 1) / (cols - 1);
            var (P, R) = Retarget.Model(bb.Parent,
                i => { int t = Array.IndexOf(an.TrackToBone, (short)i); return t >= 0 ? an.Pose[fr, t].Translation : bb.T[i]; },
                i => { int t = Array.IndexOf(an.TrackToBone, (short)i); return t >= 0 ? System.Numerics.Quaternion.Normalize(an.Pose[fr, t].Rotation) : bb.R[i]; });
            for (int view = 0; view < 2; view++)
            {
                int ox = s * cell + cell / 2, oy = view * cell + cell - 20; float sc = 120;
                System.Numerics.Vector2 Pr(System.Numerics.Vector3 p) => view == 0 ? new(ox + p.X * sc, oy - p.Y * sc) : new(ox + p.Z * sc, oy - p.Y * sc);
                foreach (var v in vs)
                {
                    var vv = v;
                    if (args.Length > 5) { var (dp0, dr0) = ForgeModels.BodyDummy(short.Parse(args[5]), Path.Combine(chr, "c0000.chrbnd.dcx")); var (rp0, rr0) = Retarget.Model(bb.Parent, i => bb.T[i], i => bb.R[i]); vv = System.Numerics.Vector3.Transform(dp0 - rp0[w] + System.Numerics.Vector3.Transform(v, dr0), System.Numerics.Quaternion.Inverse(rr0[w])); }
                    var q = Pr(P[w] + System.Numerics.Vector3.Transform(vv, R[w]));
                    int x = (int)q.X, y = (int)q.Y; if (x >= 0 && y >= 0 && x < img.Width && y < img.Height) img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 210, 80, 255);
                }
                for (int b = 1; b < P.Length; b++)
                {
                    int pa = bb.Parent[b]; if (pa < 0) continue; var n = bb.Names[b];
                    if (n.Contains("Support") || n.Contains("Target") || n.StartsWith("Root") || n.Contains("Finger")) continue;
                    AnimPreview.Line(img, Pr(P[pa]), Pr(P[b]), n.StartsWith("L_") ? new SixLabors.ImageSharp.PixelFormats.Rgba32(90, 170, 255, 255) : new SixLabors.ImageSharp.PixelFormats.Rgba32(200, 200, 200, 255));
                }
            }
        }
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, args[4]);
        return 0;
    }
    case "annot":
    {
        // annot <anibnd> <name> : annotation tracks of one animation (dev)
        var bf = BND4.Read(args[1]).Files.First(f => f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
        var an = BbHavok.Find<HKX2.hkaAnimation>(BbHavok.Read(bf.Bytes))!;
        Console.WriteLine($"{bf.Name} dur={an.m_duration} tracks={an.m_annotationTracks.Count}");
        foreach (var t in an.m_annotationTracks.Where(t => t.m_annotations.Count > 0))
            Console.WriteLine($"  {t.m_trackName}: " + string.Join(", ", t.m_annotations.Select(a => $"{a.m_time:0.00} {a.m_text}")));
        return 0;
    }
    case "freeicons":
    {
        // freeicons <bb> : icon files the game ships that no item uses (dev)
        var dvd = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4");
        var ps = BbInspect.Params(dvd, out _);
        var used = new HashSet<int>();
        foreach (var kv in ps)
            foreach (var r in kv.Value.Rows)
                foreach (var c in r.Cells.Where(c => c.Def.InternalName.StartsWith("iconId", StringComparison.OrdinalIgnoreCase)))
                    used.Add(Convert.ToInt32(c.Value));
        var files = Directory.GetFiles(Path.Combine(dvd, "menu"), "menu_knowledge_*.tpf.dcx").Select(f => int.Parse(Path.GetFileName(f)[15..20])).OrderBy(x => x).ToList();
        Console.WriteLine($"used {used.Count}, files {files.Count}");
        Console.WriteLine("free: " + string.Join(" ", files.Where(f => !used.Contains(f))));
        return 0;
    }
    case "tpfinfo":
    {
        // tpfinfo <tpf(.dcx)> : a loose texture file's textures (dev)
        var tpf = TPF.Read(args[1]);
        Console.WriteLine($"platform={tpf.Platform} enc={tpf.Encoding} flag2={tpf.Flag2} compression={tpf.Compression}");
        foreach (var t in tpf.Textures) Console.WriteLine($"  {t.Name} fmt={t.Format} type={t.Type} mips={t.Mipmaps} bytes={t.Bytes.Length} hdr={(t.Header == null ? "-" : $"{t.Header.Width}x{t.Header.Height}")} flags1={t.Flags1}");
        return 0;
    }
    case "slotjudges":
    {
        // slotjudges <bb> <anibnd> <cat> <variation> <slot...> : each slot's attack events, their behaviour rows and names (dev)
        var ps = BbInspect.Params(Path.Combine(args[1], "CUSA03173", "dvdroot_ps4"), out _);
        var beh = ps["BehaviorParam_PC"];
        var t = TAE.Read(BND4.Read(args[2]).Files.First(f => f.Name.EndsWith($"\\a{args[3]}.tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        int variation = int.Parse(args[4]);
        foreach (var id in args.Skip(5).Select(long.Parse))
        {
            var a = t.Animations.FirstOrDefault(x => x.ID == id);
            if (a == null) { Console.WriteLine($"{id}: missing"); continue; }
            string hdr = a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io ? $"imports {io.ImportFromAnimID}" : a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX ? $"hkx from {st.ImportHKXSourceAnimID}" : "own";
            Console.WriteLine($"{id}: {hdr}, {a.Events.Count} events");
            foreach (var e in a.Events.Where(e => e.Type == 1 || e.Type == 2 || e.Type == 318))
            {
                var p = e.GetParameterBytes(false);
                int judge = e.Type == 318 ? BitConverter.ToInt32(p, 4) : BitConverter.ToInt32(p, 8);
                var row = beh.Rows.FirstOrDefault(r => r.ID == 100_000_000 + variation * 1000 + judge);
                Console.WriteLine($"   type {e.Type} t={e.StartTime:0.00}-{e.EndTime:0.00} judge {judge} -> {row?.Name ?? "?"} refType {row?["refType"].Value} ref {row?["refId"].Value}");
            }
        }
        return 0;
    }
    case "taeev":
    {
        // taeev <anibnd> <cat> <slot...> : every event of the slots, with params as ints (dev)
        var t = TAE.Read(BND4.Read(args[1]).Files.First(f => f.Name.EndsWith($"\\a{args[2]}.tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var id in args.Skip(3).Select(long.Parse))
        {
            var a = t.Animations.FirstOrDefault(x => x.ID == id);
            if (a == null) { Console.WriteLine($"{id}: missing"); continue; }
            Console.WriteLine($"{id}: file={a.AnimFileName} {a.MiniHeader.GetType().Name}");
            foreach (var e in a.Events.OrderBy(e => e.StartTime))
            {
                var p = e.GetParameterBytes(false);
                var ints = Enumerable.Range(0, p.Length / 4).Select(i => BitConverter.ToInt32(p, i * 4));
                Console.WriteLine($"   {e.Type,4} {e.StartTime,6:0.00}-{e.EndTime,6:0.00}  {string.Join(" ", ints)}");
            }
        }
        return 0;
    }
    case "rtparam":
    {
        // rtparam <bb> [built parambnd] : does each param survive read+write unchanged; with a built file, which rows differ from vanilla (dev)
        var dvd = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4");
        var van = BbInspect.Params(dvd, out var vbnd);
        foreach (var f in vbnd.Files)
        {
            var name = Path.GetFileNameWithoutExtension(f.Name);
            if (!van.TryGetValue(name, out var p) || p.AppliedParamdef == null) { Console.WriteLine($"{name}: no def"); continue; }
            var w = p.Write();
            bool same = w.AsSpan().SequenceEqual(f.Bytes);
            Console.WriteLine($"{name}: {(same ? "round-trips" : $"DIFFERS ({f.Bytes.Length} -> {w.Length} bytes)")} rows {p.Rows.Count} rowSize {p.DetectedSize}");
        }
        if (args.Length > 2)
        {
            Environment.SetEnvironmentVariable("LOP_PARAM_FILE", args[2]);
            var built = BbInspect.Params(dvd, out _);
            foreach (var name in new[] { "AtkParam_Pc", "BehaviorParam_PC", "Bullet" })
            {
                var a = van[name]; var b = built[name];
                var bi = b.Rows.ToDictionary(r => r.ID);
                int changed = 0, missing = 0;
                foreach (var r in a.Rows)
                {
                    if (!bi.TryGetValue(r.ID, out var r2)) { missing++; continue; }
                    for (int c = 0; c < r.Cells.Count; c++)
                        if (!Equals(r.Cells[c].Value, r2.Cells[c].Value) && !(r.Cells[c].Value is byte[] x && r2.Cells[c].Value is byte[] y && x.AsSpan().SequenceEqual(y)))
                        { changed++; if (changed < 15) Console.WriteLine($"  {name} {r.ID} {r.Cells[c].Def.InternalName}: {r.Cells[c].Value} -> {r2.Cells[c].Value}"); }
                }
                Console.WriteLine($"{name}: vanilla {a.Rows.Count} rows, built {b.Rows.Count}, changed cells {changed}, missing {missing}");
            }
        }
        return 0;
    }
    case "bladedir":
    {
        // bladedir <anibnd with the anim> <skeleton anibnd> <name part> [step s] : blade tip and direction over time, in the
        // character's space (-Z = forward, +Y = up) (dev)
        var a = BbAnim.Read(BND4.Read(args[1]).Files.First(f => f.Name.Contains(args[3], StringComparison.OrdinalIgnoreCase)).Bytes);
        var sk = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(args[2]).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        float step = args.Length > 4 ? float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.1f;
        int wb = sk.Index("R_Weapon");
        var trackOf = new int[sk.Names.Length]; Array.Fill(trackOf, -1);
        for (int t = 0; t < a.TrackToBone.Length; t++) trackOf[a.TrackToBone[t]] = t;
        for (float time = 0; time <= (a.Frames - 1) * a.FrameDuration + 1e-3f; time += step)
        {
            int f = Math.Min(a.Frames - 1, (int)MathF.Round(time / a.FrameDuration));
            var P = new System.Numerics.Vector3[sk.Names.Length]; var Q = new System.Numerics.Quaternion[sk.Names.Length];
            for (int b = 0; b < sk.Names.Length; b++)
            {
                System.Numerics.Vector3 lt = sk.T[b]; System.Numerics.Quaternion lr = sk.R[b];
                if (trackOf[b] >= 0) { var x = a.Pose[f, trackOf[b]]; lt = x.Translation; lr = System.Numerics.Quaternion.Normalize(x.Rotation); }
                int p = sk.Parent[b];
                if (p < 0) { P[b] = lt; Q[b] = lr; }
                else { P[b] = P[p] + System.Numerics.Vector3.Transform(lt, Q[p]); Q[b] = System.Numerics.Quaternion.Normalize(Q[p] * lr); }
            }
            if (args.Length > 5 && args[5].Contains(':'))
            {
                // bladedir ... <step> <boneA:boneB> : direction from one bone to another instead (e.g. L_Forearm:L_Hand, the Cataclysm's barrel)
                var ab = args[5].Split(':'); int ia = sk.Index(ab[0]), ib = sk.Index(ab[1]);
                var d2 = System.Numerics.Vector3.Normalize(P[ib] - P[ia]);
                Console.WriteLine($"{time:0.00}s {ab[1]} ({P[ib].X:0.00},{P[ib].Y:0.00},{P[ib].Z:0.00}) dir ({d2.X:0.00},{d2.Y:0.00},{d2.Z:0.00})");
                continue;
            }
            var tip = P[wb] + System.Numerics.Vector3.Transform(new System.Numerics.Vector3(0.13f, -1.55f, 0), Q[wb]);
            var dir = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitY, Q[wb]);
            Console.WriteLine($"{time:0.00}s hand ({P[wb].X:0.00},{P[wb].Y:0.00},{P[wb].Z:0.00}) tip ({tip.X:0.00},{tip.Y:0.00},{tip.Z:0.00}) blade dir ({dir.X:0.00},{dir.Y:0.00},{dir.Z:0.00})");
        }
        return 0;
    }
    case "animdur":
    {
        // animdur <anibnd> <name part...> : length of each matching animation file (dev)
        foreach (var bf in BND4.Read(args[1]).Files.Where(f => args.Skip(2).Any(a => f.Name.Contains(a, StringComparison.OrdinalIgnoreCase))))
        {
            try { var ba = BbAnim.Read(bf.Bytes); Console.WriteLine($"{Path.GetFileName(bf.Name)} {BbHavok.Find<HKX2.hkaAnimation>(BbHavok.Read(bf.Bytes))!.m_duration:0.000}s travel {(ba.RootMotion.Count > 0 ? ba.RootMotion[^1].ToString("0.00") : "-")}"); }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(bf.Name)} unreadable: {e.Message}"); }
        }
        return 0;
    }
    case "wepdummies":
    {
        // wepdummies <partsbnd> : every flver's dummy points (id, position) in a weapon parts file (dev)
        foreach (var f in BND4.Read(args[1]).Files.Where(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
        {
            var fl = FLVER2.Read(f.Bytes);
            Console.WriteLine($"{Path.GetFileName(f.Name)}: {string.Join("  ", fl.Dummies.Select(d => $"{d.ReferenceID}({d.Position.X:0.00},{d.Position.Y:0.00},{d.Position.Z:0.00})"))}");
        }
        return 0;
    }
    case "taelist":
    {
        // taelist <anibnd> <cat> : every slot of a moveset, what it plays, and its attack/shot judges (dev)
        var t = TAE.Read(BND4.Read(args[1]).Files.First(f => f.Name.EndsWith($"\\a{args[2]}.tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var a in t.Animations)
        {
            string hdr = a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io ? $"imports {io.ImportFromAnimID}" : a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX ? $"hkx from {st.ImportHKXSourceAnimID}" : "own";
            var j = a.Events.Where(e => e.Type == 1 || e.Type == 2 || e.Type == 318).Select(e => { var p = e.GetParameterBytes(false); return $"{e.Type}:{(e.Type == 318 ? BitConverter.ToInt32(p, 8) : BitConverter.ToInt32(p, 8))}@{e.StartTime:0.00}"; });
            Console.WriteLine($"{a.ID} {a.AnimFileName} {hdr} ev={a.Events.Count} {string.Join(" ", j)}");
        }
        return 0;
    }
    case "marks":
    {
        // marks <lop> <montage path under Animset>... : a Lies of P move's timed marks (hit checks, fire, input windows) (dev)
        var p = Lop.Open(args[1]);
        foreach (var mp in args.Skip(2))
        {
            var m = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + mp);
            Console.WriteLine($"{mp}: len {m.Length:0.00}");
            foreach (var x in m.Marks.OrderBy(x => x.t)) Console.WriteLine($"   {x.t:0.00} (+{x.dur:0.00}) {x.name}");
        }
        return 0;
    }
    case "farthest":
    {
        // farthest <partsbnd> <x> <y> <z> : the main model's point farthest along that direction (e.g. a barrel tip) (dev)
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var d = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(float.Parse(args[2], inv), float.Parse(args[3], inv), float.Parse(args[4], inv)));
        var fl = FLVER2.Read(BND4.Read(args[1]).Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var best = fl.Meshes.SelectMany(me => me.Vertices).OrderByDescending(v => System.Numerics.Vector3.Dot(v.Position, d)).First().Position;
        Console.WriteLine($"farthest along ({d.X:0.00},{d.Y:0.00},{d.Z:0.00}): ({best.X:0.00},{best.Y:0.00},{best.Z:0.00})");
        return 0;
    }
    case "meshbones":
    {
        // meshbones <lop> <mesh path> [name part] : a Lies of P mesh's bones (parent, rest pose) and how many vertices each leads (dev)
        var m = LopMesh.Load(Lop.Open(args[1]), args[2]);
        var cnt = new int[m.Bones.Count];
        foreach (var v in m.Verts) { int b = 0; for (int k = 1; k < 4; k++) if (v.Bw[k] > v.Bw[b]) b = k; cnt[v.Bi[b]]++; }
        for (int i = 0; i < m.Bones.Count; i++)
            if (args.Length < 4 || m.Bones[i].Name.Contains(args[3]))
                Console.WriteLine($"{i} {m.Bones[i].Name} parent {m.Bones[i].Parent} ({(m.Bones[i].Parent >= 0 ? m.Bones[m.Bones[i].Parent].Name : "-")}) T {m.Bones[i].T} R {m.Bones[i].R} verts {cnt[i]}");
        return 0;
    }
    case "wepanim":
    {
        // wepanim <partsbnd> : a weapon's own animation files (anibnd inside the parts file): names, TAE slots, bones (dev)
        foreach (var f in BND4.Read(args[1]).Files.Where(x => x.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"== {f.Name}");
            var ab = BND4.Read(f.Bytes);
            foreach (var x in ab.Files)
            {
                string extra = "";
                if (x.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase))
                {
                    try { var t = TAE.Read(x.Bytes); extra = $" TAE id {t.ID}: slots {string.Join(",", t.Animations.Select(a => a.ID))}"; } catch (Exception e) { extra = " TAE? " + e.Message; }
                }
                else if (x.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) && x.Name.Contains("skeleton", StringComparison.OrdinalIgnoreCase))
                {
                    try { var sk = BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(x.Bytes))!; extra = " bones " + string.Join(",", sk.m_bones.Select(b => b.m_name)); } catch (Exception e) { extra = " ? " + e.Message; }
                }
                Console.WriteLine($"  {x.ID} {x.Name} {x.Bytes.Length}{extra}");
            }
        }
        return 0;
    }
    case "partsscan":
    {
        // partsscan <parts dir> : weapon part files holding anything besides models and textures (animations etc.) (dev)
        foreach (var f in Directory.GetFiles(args[1], "wp_*.partsbnd.dcx").Where(f => !f.EndsWith("_l.partsbnd.dcx")))
        {
            var bnd = BND4.Read(f);
            var other = bnd.Files.Where(x => !x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) && !x.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).Select(x => Path.GetFileName(x.Name)).ToList();
            var bones = string.Join("/", bnd.Files.Where(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Select(x => FLVER2.Read(x.Bytes).Nodes.Count));
            if (other.Count > 0) Console.WriteLine($"{Path.GetFileName(f)}: bones {bones}; {string.Join(", ", other)}");
        }
        return 0;
    }
    case "taegroups":
    {
        // taegroups <anibnd> <cat> <slot...> : each event's event group (type), and events in no group (dev)
        var t = TAE.Read(BND4.Read(args[1]).Files.First(f => f.Name.EndsWith($"\\a{args[2]}.tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var id in args.Skip(3).Select(long.Parse))
        {
            var a = t.Animations.FirstOrDefault(x => x.ID == id);
            if (a == null) { Console.WriteLine($"{id}: missing"); continue; }
            Console.WriteLine($"{id}: {a.EventGroups.Count} groups ({string.Join(",", a.EventGroups.Select(g => g.GroupType))}), {a.Events.Count(e => e.Group == null)} of {a.Events.Count} events in no group");
            foreach (var e in a.Events.Where(e => e.Type == 1 || e.Type == 318 || e.Type == 96 || e.Type == 32 || e.Group == null))
                Console.WriteLine($"   type {e.Type,4} {e.StartTime:0.00} group {(e.Group == null ? "NONE" : e.Group.GroupType.ToString())}");
        }
        return 0;
    }
    case "paramdiff":
    {
        // paramdiff <bb> <parambnd A> <parambnd B> <param> [minId maxId] : rows/cells that differ between two built param files (dev)
        var dvd = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4");
        Environment.SetEnvironmentVariable("LOP_PARAM_FILE", args[2]); var A = BbInspect.Params(dvd, out _)[args[4]];
        Environment.SetEnvironmentVariable("LOP_PARAM_FILE", args[3]); var B = BbInspect.Params(dvd, out _)[args[4]];
        long lo = args.Length > 5 ? long.Parse(args[5]) : long.MinValue, hi = args.Length > 6 ? long.Parse(args[6]) : long.MaxValue;
        var bi = B.Rows.Where(r => r.ID >= lo && r.ID <= hi).ToDictionary(r => r.ID);
        var ai = A.Rows.Where(r => r.ID >= lo && r.ID <= hi).ToDictionary(r => r.ID);
        foreach (var id in ai.Keys.Except(bi.Keys)) Console.WriteLine($"only in A: {id} {ai[id].Name}");
        foreach (var id in bi.Keys.Except(ai.Keys)) Console.WriteLine($"only in B: {id} {bi[id].Name}");
        foreach (var id in ai.Keys.Intersect(bi.Keys))
            for (int c = 0; c < ai[id].Cells.Count; c++)
            {
                object x = ai[id].Cells[c].Value, y = bi[id].Cells[c].Value;
                if (x is byte[] xb && y is byte[] yb ? !xb.AsSpan().SequenceEqual(yb) : !Equals(x, y))
                    Console.WriteLine($"{id} {ai[id].Cells[c].Def.InternalName}: {x} -> {y}");
            }
        return 0;
    }
    case "findint":
    {
        // findint <anibnd> <tae name> <min> <max> : every event in that TAE with an int param in [min,max] (dev)
        int lo = int.Parse(args[3]), hi = int.Parse(args[4]);
        var bf = BND4.Read(args[1]).Files.First(f => f.Name.EndsWith("\\" + args[2] + ".tae", StringComparison.OrdinalIgnoreCase));
        var t = TAE.Read(bf.Bytes);
        foreach (var a in t.Animations)
            foreach (var e in a.Events)
            {
                var p = e.GetParameterBytes(false);
                var ints = Enumerable.Range(0, p.Length / 4).Select(i => BitConverter.ToInt32(p, i * 4)).ToList();
                if (ints.Any(v => v >= lo && v <= hi)) Console.WriteLine($"{a.ID} type {e.Type} t={e.StartTime:0.00} {string.Join(" ", ints)}");
            }
        return 0;
    }
    case "find318":
    {
        // find318 <anibnd> [type] : every TAE slot with a projectile event (318, or the given type), with params (dev)
        int ty = args.Length > 2 ? int.Parse(args[2]) : 318;
        foreach (var bf in BND4.Read(args[1]).Files.Where(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)))
        {
            TAE t; try { t = TAE.Read(bf.Bytes); } catch { continue; }
            foreach (var a in t.Animations)
                foreach (var e in a.Events.Where(e => e.Type == ty))
                {
                    var p = e.GetParameterBytes(false);
                    var ints = Enumerable.Range(0, p.Length / 4).Select(i => BitConverter.ToInt32(p, i * 4));
                    Console.WriteLine($"{Path.GetFileNameWithoutExtension(bf.Name)} {a.ID} t={e.StartTime:0.00} {string.Join(" ", ints)}");
                }
        }
        return 0;
    }
    case "findjudge":
    {
        // findjudge <anibnd> <judge> : every TAE slot whose attack event uses that behaviour judge (dev)
        int j = int.Parse(args[2]);
        foreach (var bf in BND4.Read(args[1]).Files.Where(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)))
        {
            TAE t; try { t = TAE.Read(bf.Bytes); } catch { continue; }
            foreach (var a in t.Animations)
                foreach (var e in a.Events.Where(e => e.Type == 1 || e.Type == 2))
                {
                    var p = e.GetParameterBytes(false);
                    if (p.Length >= 12 && BitConverter.ToInt32(p, 8) == j) Console.WriteLine($"{Path.GetFileName(bf.Name)} {a.ID} type={e.Type} t={e.StartTime:0.00} file={a.AnimFileName} ev={a.Events.Count}");
                }
        }
        return 0;
    }
    case "clipsel":
    {
        // clipsel <behavior.hkx> <anim name part>... : every manual selector holding a clip of that animation (dev)
        var pf = new Packfile(File.ReadAllBytes(args[1]));
        var objs = pf.Objects(); var byStart = objs.ToDictionary(o => o.start);
        var globalsBySrc = pf.Global.GroupBy(g => g.src).ToDictionary(g => g.Key, g => g.First());
        var localAt = pf.Local.GroupBy(l => l.src).ToDictionary(g => g.Key, g => g.First().dst);
        string? Anim(uint c) => byStart.TryGetValue(c, out var o) && o.cls == "hkbClipGenerator" && localAt.TryGetValue(c + 144, out var d) ? pf.Str(d) : null;
        string SelName(uint s) => localAt.TryGetValue(s + 56, out var d) ? pf.Str(d) : "?";
        foreach (var sel in objs.Where(o => o.cls == "CustomManualSelectorGenerator"))
            foreach (var l in pf.Local.Where(l => l.src > sel.start && l.src < sel.end).OrderBy(l => l.src))
            {
                int n = pf.I32(l.src + 8);
                if (n <= 0 || n > 4096 || !globalsBySrc.ContainsKey(l.dst)) continue;
                var kids = Enumerable.Range(0, n).Select(i => globalsBySrc.TryGetValue(l.dst + (uint)i * 8, out var g) ? Anim(g.dst) : null).ToList();
                if (args.Skip(2).Any(a => kids.Any(k => k != null && k.Contains(a))))
                    Console.WriteLine($"{SelName(sel.start)} off={pf.I32(l.src + 16)} id={pf.I32(l.src + 20)} n={n}: {string.Join(" ", kids.Where(k => k != null && args.Skip(2).Any(a => k.Contains(a[..4]))).Take(12))}");
                break;
            }
        return 0;
    }
    case "cliptrig":
    {
        // cliptrig <behavior.hkx> <anim name> : that clip's raw fields and its trigger array (dev)
        var pf = new Packfile(File.ReadAllBytes(args[1]));
        var objs = pf.Objects(); var byStart = objs.ToDictionary(o => o.start);
        var localAt = pf.Local.GroupBy(l => l.src).ToDictionary(g => g.Key, g => g.First().dst);
        foreach (var o in objs.Where(o => o.cls == "hkbClipGenerator"))
        {
            if (!localAt.TryGetValue(o.start + 144, out var d) || pf.Str(d) != args[2]) continue;
            Console.WriteLine($"clip @{o.start} len {o.end - o.start}");
            for (uint q = o.start; q < o.end; q += 16) Console.WriteLine($"  +{q - o.start,3}: {Convert.ToHexString(pf.Data.GetRange((int)q, (int)Math.Min(16, o.end - q)).ToArray())}");
            foreach (var g in pf.Global.Where(g => g.src >= o.start && g.src < o.end))
            {
                var t = byStart.TryGetValue(g.dst, out var to) ? to : default;
                Console.WriteLine($"  ptr +{g.src - o.start} -> {t.cls} @{g.dst} len {t.end - t.start}");
                if (t.cls == "hkbClipTriggerArray")
                {
                    for (uint q = t.start; q < t.end; q += 16) Console.WriteLine($"     +{q - t.start,3}: {Convert.ToHexString(pf.Data.GetRange((int)q, (int)Math.Min(16, t.end - q)).ToArray())}");
                    foreach (var l in pf.Local.Where(l => l.src >= t.start && l.src < t.end))
                    {
                        int n = pf.I32(l.src + 8);
                        Console.WriteLine($"     array +{l.src - t.start} n={n} at {l.dst}");
                        for (int i = 0; i < n && i < 16; i++) { uint e = l.dst + (uint)i * 32; Console.WriteLine($"       [{i}] t={BitConverter.ToSingle(pf.Data.GetRange((int)e, 4).ToArray())} id={pf.I32(e + 8)} raw={Convert.ToHexString(pf.Data.GetRange((int)e, 32).ToArray())}"); }
                    }
                }
            }
        }
        return 0;
    }
    case "selchild":
    {
        // selchild <behavior.hkx> <name filter> <index>... : manual selectors whose name matches, and the clips at those child indices (dev)
        var pf = new Packfile(File.ReadAllBytes(args[1]));
        var objs = pf.Objects(); var byStart = objs.ToDictionary(o => o.start);
        var globalsBySrc = pf.Global.GroupBy(g => g.src).ToDictionary(g => g.Key, g => g.First());
        string NameOf((uint start, uint end, string cls, uint n) o) { foreach (var l in pf.Local.Where(l => l.src >= o.start && l.src < o.end).OrderBy(l => l.src)) { try { var s = pf.Str(l.dst); if (s.Length > 2 && s.All(c => c >= 32 && c < 127)) return s; } catch { } } return "?"; }
        string Desc(uint at, int depth = 0)
        {
            if (!byStart.TryGetValue(at, out var o)) return "?";
            if (o.cls == "hkbClipGenerator") { var a = pf.Local.FirstOrDefault(l => l.src == o.start + 144); return "clip " + (a.dst != 0 ? pf.Str(a.dst) : NameOf(o)); }
            if (depth > 2) return o.cls + " " + NameOf(o);
            // first pointer-array or pointer inside: describe its first target
            var inner = pf.Global.Where(g => g.src >= o.start && g.src < o.end).Select(g => g.dst).Take(3).ToList();
            return o.cls + " " + NameOf(o) + (inner.Count > 0 ? " -> " + string.Join(" | ", inner.Select(i => Desc(i, depth + 1))) : "");
        }
        var idx = args.Skip(3).Select(int.Parse).ToList();
        foreach (var sel in objs.Where(o => o.cls.Contains("ManualSelector")))
        {
            var nm = NameOf(sel);
            if (!nm.Contains(args[2], StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var l in pf.Local.Where(l => l.src > sel.start && l.src < sel.end).OrderBy(l => l.src))
            {
                int n = pf.I32(l.src + 8);
                if (n <= 0 || n > 4096 || !globalsBySrc.ContainsKey(l.dst)) continue;
                Console.WriteLine($"{sel.cls} {nm} children={n} offsetType={pf.I32(l.src + 16)} animId={pf.I32(l.src + 20)}");
                foreach (var i in idx.Where(i => i < n)) Console.WriteLine($"   [{i}] {Desc(globalsBySrc[l.dst + (uint)i * 8].dst)}");
                break;
            }
        }
        return 0;
    }
    case "shoulderfit":
    {
        // shoulderfit <bb> <anim stem e.g. a143_030000> <partsbnd>... : in the move's last frame (the carry pose),
        // where each model's blade passes the right shoulder, in the weapon's own space (dev)
        var chr = Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "chr");
        var bb = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(chr, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        BbAnim? an = null;
        foreach (var fpath in Directory.GetFiles(chr, "c0000_*.anibnd.dcx"))
        {
            var hit = BND4.Read(fpath).Files.FirstOrDefault(f => f.Name.EndsWith(args[2] + ".hkx", StringComparison.OrdinalIgnoreCase));
            if (hit != null) { an = BbAnim.Read(hit.Bytes); break; }
        }
        if (an == null) { Console.WriteLine("anim not found"); return 2; }
        foreach (var fr in new[] { 0, an.Frames - 1 })
        {
            var (P, R) = Retarget.Model(bb.Parent,
                i => { int t = Array.IndexOf(an.TrackToBone, (short)i); return t >= 0 ? an.Pose[fr, t].Translation : bb.T[i]; },
                i => { int t = Array.IndexOf(an.TrackToBone, (short)i); return t >= 0 ? System.Numerics.Quaternion.Normalize(an.Pose[fr, t].Rotation) : bb.R[i]; });
            int w = bb.Index("R_Weapon"), sh = bb.Index("R_UpperArm"), cl = bb.Index("R_Clavicle");
            var shoulder = P[sh] + new System.Numerics.Vector3(0, 0.07f, 0);
            var inv = System.Numerics.Quaternion.Inverse(R[w]);
            Console.WriteLine($"frame {fr}: weapon X={System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, R[w]):0.00} Y={System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, R[w]):0.00} Z={System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, R[w]):0.00}; shoulder in weapon space {System.Numerics.Vector3.Transform(shoulder - P[w], inv) * 100:0.0} cm; world up in weapon space {System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, inv):0.00}");
            foreach (var pb in args.Skip(3))
            {
                var fl = FLVER2.Read(BND4.Read(pb).Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                var vs = fl.Meshes.SelectMany(m => m.Vertices.Select(v => v.Position)).ToList();
                var local = System.Numerics.Vector3.Transform(shoulder - P[w], inv);
                var near = vs.OrderBy(v => System.Numerics.Vector3.Distance(v, local)).First();
                // the blade's lowest point (world) near the shoulder, along the blade length at the shoulder
                var band = vs.Where(v => MathF.Abs(v.Y - local.Y) < 0.04f).ToList();
                var lowest = band.Count > 0 ? band.OrderBy(v => System.Numerics.Vector3.Transform(v, R[w]).Y).First() : near;
                Console.WriteLine($"  {Path.GetFileName(pb)}: nearest point {System.Numerics.Vector3.Distance(near, local) * 100:0.0} cm at {near * 100:0.0}; lowest point at that length {lowest * 100:0.0} is {(P[w] + System.Numerics.Vector3.Transform(lowest, R[w])).Y * 100 - shoulder.Y * 100:0.0} cm above shoulder top");
            }
        }
        return 0;
    }
    case "gripprofile":
    {
        // gripprofile <partsbnd> : the model's cross-section every 2 cm along Y (weapon space; dev)
        var bnd = BND4.Read(args[1]);
        var f = bnd.Files.First(x => x.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase));
        var fl = FLVER2.Read(f.Bytes);
        var vs = fl.Meshes.SelectMany(m => m.Vertices.Select(v => v.Position)).ToList();
        if (args.Length > 2)
        {
            // side view (X right, Y down = blade up) as a picture, 4 px per cm, hand (origin) marked
            using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(400, 800, new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 20, 26, 255));
            foreach (var v in vs)
            {
                int x = 200 + (int)(v.X * 400), y = 160 - (int)(v.Y * 400);
                if (x >= 0 && y >= 0 && x < 400 && y < 800) img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(230, 230, 230, 255);
            }
            for (int d = -8; d <= 8; d++) { img[200 + d, 160] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 60, 60, 255); img[200, 160 + d] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 60, 60, 255); }
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, args[2]);
        }
        for (int s = (int)MathF.Floor(vs.Min(v => v.Y) * 50); s <= (int)MathF.Ceiling(vs.Max(v => v.Y) * 50); s++)
        {
            var sl = vs.Where(v => v.Y >= s / 50f && v.Y < (s + 1) / 50f).ToList();
            if (sl.Count == 0) continue;
            Console.WriteLine($"y={s * 2,4}cm n={sl.Count,5} x {sl.Min(v => v.X) * 100,6:0.0}..{sl.Max(v => v.X) * 100,6:0.0} z {sl.Min(v => v.Z) * 100,6:0.0}..{sl.Max(v => v.Z) * 100,6:0.0}");
        }
        return 0;
    }
    case "bonecurve":
    {
        // bonecurve <lop> <bone> <montage path under Animset>... : one bone's local translation over the move (montage
        // play range), every 0.1 s, plus the move's marks (dev)
        var p = Lop.Open(args[1]);
        foreach (var m in args.Skip(3))
        {
            var mt = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + m);
            var a = LopAnim.Load(p, mt.Sequence.Split('.')[0]);
            var tr = a.Tracks.FirstOrDefault(t => t.Bone == args[2]);
            float s0 = mt.SeqStart, s1 = mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps;
            Console.WriteLine($"== {m} ({mt.Sequence}) play {s0:0.00}-{s1:0.00}s, marks: {string.Join(", ", mt.Marks.Select(x => $"{x.name}@{x.t:0.00}"))}");
            if (tr == null) { Console.WriteLine("   bone not animated; skeleton has it: " + a.RefBones.Any(b => b.name == args[2])); continue; }
            for (float t = 0; t <= s1 - s0 + 0.001f; t += 0.1f)
            {
                int f = Math.Clamp((int)MathF.Round((s0 + t) * a.Fps), 0, a.Frames - 1);
                Console.WriteLine($"   {t:0.0}s  {tr.T[f].X:0.0} {tr.T[f].Y:0.0} {tr.T[f].Z:0.0}");
            }
        }
        return 0;
    }
    case "travel":
    {
        // travel <lop> <montage path under Animset>... : root travel over time (dev; -Z = forward)
        var p = Lop.Open(args[1]);
        foreach (var m in args.Skip(2))
        {
            var mt = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + m);
            var a = LopAnim.Load(p, mt.Sequence.Split('.')[0]);
            var tr = a.Tracks.FirstOrDefault(t => t.Bone == "Root");
            if (tr == null) { Console.WriteLine($"{m}: no root track"); continue; }
            int f0 = (int)(mt.SeqStart * a.Fps), f1 = Math.Min(a.Frames - 1, (int)((mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps) * a.Fps));
            int steps = args.Length > 0 && Environment.GetEnvironmentVariable("LOP_TRAVEL_STEPS") is string ss && int.TryParse(ss, out var sn) ? sn : 5;
            var s = string.Join(" ", Enumerable.Range(0, steps + 1).Select(k => { int f = f0 + (f1 - f0) * k / steps; var d = ForgeAnims.Gp(tr.T[f] - tr.T[f0]); return $"{(f - f0) / a.Fps:0.00}s:x{d.X:0.00},z{d.Z:0.00}"; }));
            Console.WriteLine($"{m}: {s}");
        }
        return 0;
    }
    case "skelarm":
    {
        // skelarm <lop> <bb> : arm-chain bones of both rigs (dev)
        var chr = Path.Combine(args[2], "CUSA03173", "dvdroot_ps4", "chr");
        var bb = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Path.Combine(chr, "c0000.anibnd.dcx")).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        for (int i = 0; i < bb.Names.Length; i++)
            if ((bb.Names[i].StartsWith("R_") || bb.Names[i].StartsWith("L_")) && (bb.Names[i].Contains("Arm") || bb.Names[i].Contains("Hand") || bb.Names[i].Contains("Fore") || bb.Names[i].Contains("Clav") || bb.Names[i] == "R_Finger2" || bb.Names[i].EndsWith("_Weapon")) || bb.Names[i].Contains("Support_R") && (bb.Names[i].Contains("Arm") || bb.Names[i].Contains("Elbow") || bb.Names[i].Contains("Fore")))
                Console.WriteLine($"bb {i} {bb.Names[i]} parent={(bb.Parent[i] < 0 ? "-" : bb.Names[bb.Parent[i]])} t={bb.T[i]:0.000}");
        var p = Lop.Open(args[1]);
        var mtA = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + (args.Length > 3 ? args[3] : "TwoHand/GunBlade/MT_GunBlade_NA1")); foreach (var rb in LopAnim.Load(p, mtA.Sequence.Split('.')[0]).RefBones.Where(x => x.name.Contains("Weapon") || x.name.Contains("Prop"))) Console.WriteLine("lopbone " + rb.name); var a = LopAnim.Load(p, mtA.Sequence.Split('.')[0]);
        for (int i = 0; i < a.RefBones.Count; i++)
            if (a.RefBones[i].name.Contains("-R-") && (a.RefBones[i].name.Contains("Arm") || a.RefBones[i].name.Contains("Hand") || a.RefBones[i].name.Contains("Fore") || a.RefBones[i].name.Contains("Clav") || a.RefBones[i].name.EndsWith("Finger2") || a.RefBones[i].name == "BN_Weapon_R") || a.RefBones[i].name.Contains("_R_Fore") || a.RefBones[i].name.Contains("R_ForeTwist") || a.RefBones[i].name.Contains("R_UpperTwist"))
                Console.WriteLine($"lop {i} {a.RefBones[i].name} parent={(a.RefBones[i].parent < 0 ? "-" : a.RefBones[a.RefBones[i].parent].name)} t={a.RefBones[i].t:0.0}");
        return 0;
    }
    case "hkspatch":
    {
        // hkspatch <bb> <out.hks> : the control-script patch alone, written to a scratch file (dev check)
        var hks = File.ReadAllBytes(Path.Combine(args[1], "CUSA03173", "dvdroot_ps4", "action", "script", "c0000.hks"));
        int fnCount = HksPatch.Walk(hks).Count;
        int n = HksPatch.AllowLeftGun(hks, Console.WriteLine) + HksPatch.GiveLeftGunLayer(hks, Console.WriteLine);
        Console.WriteLine($"functions={fnCount} changed={n}");
        File.WriteAllBytes(args[2], hks);
        return 0;
    }
    case "montages":
    {
        // montages <lop> <out.txt> <path prefix>... : timing summary of every montage under the prefixes
        var p = Lop.Open(args[1]);
        using var w = new StreamWriter(args[2]);
        foreach (var prefix in args.Skip(3))
            foreach (var f in p.Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset")).OrderBy(k => k))
            {
                var path = f[..^7];
                try
                {
                    var m = LopMontage.Load(p, path);
                    var hits = string.Join(",", m.Marks.Where(x => x.name == "LWeaponHitCheck" || x.name.Contains("Projectile") || x.name.Contains("Shot") || x.name.Contains("Fire")).Select(x => $"{x.name}@{x.t:0.00}+{x.dur:0.00}"));
                    w.WriteLine($"{System.IO.Path.GetFileName(path)}\tlen={m.Length:0.00}\tseq={System.IO.Path.GetFileName(m.Sequence)}[{m.SeqStart:0.00}-{m.SeqEnd:0.00}x{m.Rate}]\tcombo={m.First("LApplyInput_LightAttack"):0.00}\tdodge={m.First("LApplyInput_Dash"):0.00}\tmove={m.First("LAllowInput_Move"):0.00}\thits={hits}");
                    w.WriteLine("\t\tall: " + string.Join(" ", m.Marks.Select(x => x.name.Replace("LAnimNotify", "").Replace("L", "") + "@" + x.t.ToString("0.00"))).Replace("\n", " "));
                }
                catch (Exception e) { w.WriteLine($"{System.IO.Path.GetFileName(path)}\t(not a montage: {e.GetType().Name})"); }
            }
        return 0;
    }
    case "forge":
    {
        // forge <lop> <bb> <ready file> [mod folder] : builds the whole mod layer from the player's own two games,
        // in the mods folder the launcher uses, on top of the player's other enabled mods.
        var lopDir = args[1]; var bbDir = args[2];
        ForgeItems.Diag = args.Contains("--diag");
        args = args.Where(a => a != "--diag").ToArray();
        Layers.Init(bbDir);
        BbInspect.UseLayers = true;
        var modDir = args.Length > 4 ? args[4] : Path.Combine(Layers.ModsDir, Layers.ModName);
        var ready = args.Length > 3 ? args[3] : Path.Combine(modDir, "lop-arsenal-ready.json");
        var dvd = Layers.Dvd;
        var outDvd = Path.Combine(modDir, "dvdroot_ps4");
        void ErrorNote(string text)
        {
            foreach (var dir in new[] { modDir, Path.GetDirectoryName(Path.GetFullPath(ready)), AppContext.BaseDirectory })
            {
                try { if (string.IsNullOrEmpty(dir)) continue; Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "lop-arsenal-error.log"), DateTime.Now + " " + text + Environment.NewLine); return; }
                catch { }
            }
        }
        if (!Directory.Exists(dvd))
        {
            var msg = $"Couldn't find Bloodborne's game files (the folder with dvdroot_ps4) for the bbport-windows at {bbDir}: it isn't inside that folder, and the bbport launcher's game folder setting ({GameData.LauncherGameDir() ?? "not set"}) doesn't point to one. Open Play Bloodborne.exe once and choose your Bloodborne game folder, then press Play again.";
            Console.WriteLine("FAILED: " + msg);
            ErrorNote(msg);
            return 4;
        }
        Console.WriteLine("Bloodborne game files: " + Path.GetDirectoryName(dvd));
        Console.WriteLine($"mods folder: {Layers.ModsDir}; building on: {string.Join(", ", Layers.Enabled)}");
        // An earlier build left in the port's default mods folder would load twice if the launcher's folder is that one; elsewhere it's unused.
        var stale = Path.Combine(bbDir, "mods", Layers.ModName);
        if (!string.Equals(Path.GetFullPath(stale), Path.GetFullPath(modDir), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(stale, "lop-arsenal-ready.json")))
            Directory.Delete(stale, true);
        if (Directory.Exists(outDvd)) Directory.Delete(outDvd, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ready))!);
        void Stage(string id, string words) { Console.WriteLine($"@@stage {id} {words}"); Console.Out.Flush(); }
        try
        {
            if (File.Exists(ready)) File.Delete(ready);
            var items = Sheets.Load<ItemRow>("items");
            var moves = Sheets.Load<MoveRow>("moves");
            Stage("open", "Opening your Lies of P");
            var p = Lop.Open(lopDir);
            Stage("items", "Adding the weapons to the bath shop");
            ForgeItems.Build(p, dvd, outDvd, items, Console.WriteLine);
            Console.WriteLine("@@progress 0.3");
            Stage("models", "Converting the Pale Knight and Cataclysm models");
            ForgeModels.Build(p, dvd, outDvd, items, Console.WriteLine);
            Console.WriteLine("@@progress 0.7");
            Stage("moves", "Building the movesets");
            ForgeAnims.WeaponTilt = items.FirstOrDefault(i => i.fit == "blade-z")?.gripTilt ?? 0;
            ForgeMoves.Build(p, dvd, outDvd, moves, Console.WriteLine);
            // The control script lets Cataclysm (moveset 161) fire from the left hand like Bloodborne's own guns.
            // Every moveset-number check in the Hunter's control script sees the new movesets as their templates.
            var hks = File.ReadAllBytes(Layers.Source(Path.Combine("action", "script", "c0000.hks")));
            var hksOut = Path.Combine(outDvd, "action", "script", "c0000.hks");
            Directory.CreateDirectory(Path.GetDirectoryName(hksOut)!);
            var asCat = moves.Where(m => m.bbCategory >= 160).GroupBy(m => m.bbCategory).ToDictionary(g => g.Key, g => g.First().templateCategory);
            string? hksText = null;
            try { hksText = HksText.Build(hks, asCat, Console.WriteLine, items.Where(i => i.slot == "left").Select(i => i.wepmotionCategory).ToList(), items.Where(i => i.idleComboTransform).Select(i => i.wepmotionCategory).ToList(), items.Where(i => i.ownL2).Select(i => i.wepmotionCategory).ToList(), ShotSheet.Load().buff is { countsAs: > 0 } bf ? (bf.countsAs, bf.spEffect) : null); }
            catch (Exception e) { Console.WriteLine("control script: text version failed (" + e.Message + ")"); }
            if (hksText != null) File.WriteAllText(hksOut, hksText, new System.Text.UTF8Encoding(false));
            else if (HksPatch.AllowLeftGun(hks, Console.WriteLine) + HksPatch.GiveLeftGunLayer(hks, Console.WriteLine) > 0)
                File.WriteAllBytes(hksOut, hks);
            Console.WriteLine("@@progress 1");
            File.WriteAllText(ready, System.Text.Json.JsonSerializer.Serialize(new { version = 1, built = DateTime.UtcNow, items = items.Select(i => i.key) }));
            Console.WriteLine("ready");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e.Message);
            ErrorNote(e.ToString());
            return 3;
        }
    }
    case "models":
    {
        // models <lop> <bb> <out mod folder> : weapon models only (dev)
        Layers.Init(args[2]);
        var p = Lop.Open(args[1]);
        var dvd = Path.Combine(args[2], "CUSA03173", "dvdroot_ps4");
        var outDvd = Path.Combine(args[3], "dvdroot_ps4");
        ForgeModels.Build(p, dvd, outDvd, Sheets.Load<ItemRow>("items"), Console.WriteLine);
        return 0;
    }
    case "preview":
    {
        // preview <out.png> <partsbnd>... : side (X/Y) and top (Z/Y) shaded views of each file's first flver, side by side (dev)
        var files = args.Skip(2).ToList();
        int cell = 420, W = cell * 2 * files.Count, H = 820;
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(W, H, new SixLabors.ImageSharp.PixelFormats.Rgba32(40, 40, 46, 255));
        var zbuf = new float[W * H];
        for (int fi = 0; fi < files.Count; fi++)
        {
            var bnd = BND4.Read(files[fi]);
            var fl = FLVER2.Read(bnd.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
            for (int view = 0; view < 2; view++)
            {
                Array.Fill(zbuf, float.MaxValue);
                int ox = (fi * 2 + view) * cell + cell / 2, oy = H / 3; float sc = 380f;
                System.Numerics.Vector3 Proj(System.Numerics.Vector3 p) => view == 0 ? new(ox + p.X * sc, oy - p.Y * sc, p.Z) : new(ox + p.Z * sc, oy - p.Y * sc, -p.X);
                var light = System.Numerics.Vector3.Normalize(new(0.4f, 0.6f, view == 0 ? -0.7f : 0.7f));
                foreach (var me in fl.Meshes)
                {
                    var idx = me.FaceSets[0].Indices;
                    for (int i = 0; i + 2 < idx.Count; i += 3)
                    {
                        var a = me.Vertices[idx[i]].Position; var b = me.Vertices[idx[i + 1]].Position; var c = me.Vertices[idx[i + 2]].Position;
                        var nrm = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(b - a, c - a));
                        if (float.IsNaN(nrm.X)) continue;
                        float shade = 0.25f + 0.75f * MathF.Abs(System.Numerics.Vector3.Dot(nrm, light));
                        var A = Proj(a); var B = Proj(b); var C = Proj(c);
                        int x0 = (int)MathF.Max(0, MathF.Min(A.X, MathF.Min(B.X, C.X))), x1 = (int)MathF.Min(W - 1, MathF.Max(A.X, MathF.Max(B.X, C.X)));
                        int y0 = (int)MathF.Max(0, MathF.Min(A.Y, MathF.Min(B.Y, C.Y))), y1 = (int)MathF.Min(H - 1, MathF.Max(A.Y, MathF.Max(B.Y, C.Y)));
                        float den = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
                        if (MathF.Abs(den) < 1e-6f) continue;
                        for (int y = y0; y <= y1; y++)
                            for (int x = x0; x <= x1; x++)
                            {
                                float w0 = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / den;
                                float w1 = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / den;
                                float w2 = 1 - w0 - w1;
                                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                                float z = w0 * A.Z + w1 * B.Z + w2 * C.Z;
                                if (z >= zbuf[y * W + x]) continue;
                                zbuf[y * W + x] = z;
                                byte g = (byte)(shade * 220);
                                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(g, g, (byte)Math.Min(255, g + 15), 255);
                            }
                    }
                }
                // grip (origin) marker
                for (int k = -6; k <= 6; k++) { img[Math.Clamp(ox + k, 0, W - 1), oy] = new(255, 60, 60, 255); img[ox, Math.Clamp(oy + k, 0, H - 1)] = new(255, 60, 60, 255); }
            }
        }
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, args[1]);
        return 0;
    }
    case "esdints":
    {
        // esdints <talkesdbnd> <min> <max> : int constants in each talk script within a range (dev)
        var bnd = BND4.Read(args[1]); int lo = int.Parse(args[2]), hi = int.Parse(args[3]);
        foreach (var f in bnd.Files)
        {
            var b = f.Bytes; var hits = new List<int>();
            for (int i = 0; i + 5 <= b.Length; i++)
            {
                if (b[i] == 0x82) { int v = BitConverter.ToInt32(b, i + 1); if (v >= lo && v <= hi) hits.Add(v); }
                if (b[i] == 0x81) { float v = BitConverter.ToSingle(b, i + 1); if (v >= lo && v <= hi && v == MathF.Floor(v)) hits.Add(-(int)v); }
            }
            if (hits.Count > 0) Console.WriteLine($"{Path.GetFileName(f.Name)}: {string.Join(" ", hits)}");
            if (args.Length > 4 && f.Name.Contains(args[4]))
                for (int i = 0; i + 5 <= b.Length; i++)
                    if (b[i] == 0x82 && BitConverter.ToInt32(b, i + 1) == lo)
                        Console.WriteLine("  ctx: " + Convert.ToHexString(b, Math.Max(0, i - 8), Math.Min(40, b.Length - Math.Max(0, i - 8))));
        }
        return 0;
    }
    case "esdcalls":
    {
        // esdcalls <talkesdbnd> <esd name part> : every command call whose arguments mention a shop id (dev)
        var bnd = BND4.Read(args[1]);
        foreach (var f in bnd.Files.Where(f => f.Name.Contains(args[2])))
        {
            var esd = ESD.Read(f.Bytes);
            foreach (var g in esd.StateGroups) foreach (var s in g.Value)
            {
                var calls = s.Value.EntryCommands.Concat(s.Value.WhileCommands).Concat(s.Value.ExitCommands)
                    .Concat(s.Value.Conditions.SelectMany(c => c.PassCommands));
                foreach (var c in calls)
                {
                    var hex = c.Arguments.Select(Convert.ToHexString).ToList();
                    if (hex.Any(h => h.Contains("A0860100") || h.Contains("A086")))
                        Console.WriteLine($"{Path.GetFileName(f.Name)} g{g.Key} s{s.Key} c{c.CommandBank}_{c.CommandID}({string.Join(", ", hex)})");
                }
            }
        }
        return 0;
    }
    case "fmgget":
    {
        // fmgget <msgbnd> <id>... : the text of those ids in every FMG of the archive (dev)
        var bnd = BND4.Read(args[1]);
        foreach (var f in bnd.Files)
        {
            var fmg = FMG.Read(f.Bytes);
            foreach (var id in args.Skip(2).Select(int.Parse))
            {
                var e = fmg.Entries.FirstOrDefault(x => x.ID == id);
                if (e != null) Console.WriteLine($"{f.ID} {id}: {(e.Text ?? "<null>").Replace("\n", " / ").Substring(0, Math.Min(90, (e.Text ?? "<null>").Length))}");
            }
        }
        return 0;
    }
    case "paramhdr":
    {
        // paramhdr <parambnd> <param file name> : raw header and row table summary (dev)
        var bnd = BND4.Read(args[1]);
        var f = bnd.Files.First(x => x.Name.EndsWith(args[2], StringComparison.OrdinalIgnoreCase));
        var b = f.Bytes;
        Console.WriteLine($"{args[2]} size={b.Length} hdr={Convert.ToHexString(b, 0, 0x40)}");
        int count = BitConverter.ToUInt16(b, 0x0A);
        Console.WriteLine($"count@0x0A={count} dataStart@0x04?={BitConverter.ToUInt32(b, 0x00)} u16@0x08={BitConverter.ToUInt16(b, 0x08)}");
        long prev = -1; int unsorted = 0;
        var ids = new List<long>();
        for (int i = 0; i < count; i++)
        {
            long id = BitConverter.ToInt64(b, 0x40 + i * 24) & 0xFFFFFFFF;
            long data = BitConverter.ToInt64(b, 0x40 + i * 24 + 8);
            if (id < prev) unsorted++;
            prev = id; ids.Add(id);
            if (args.Length > 3 && args.Skip(3).Contains(id.ToString())) Console.WriteLine($"  row {id} data@{data}");
        }
        Console.WriteLine($"unsorted={unsorted} first={ids.FirstOrDefault()} last={ids.LastOrDefault()}");
        return 0;
    }
    case "flverinfo":
    {
        // flverinfo <partsbnd> : bounds, nodes and dummies of each flver (dev)
        var bnd = BND4.Read(args[1]);
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)))
        {
            var fl = FLVER2.Read(f.Bytes);
            var min = new System.Numerics.Vector3(1e9f); var max = -min;
            foreach (var me in fl.Meshes) foreach (var v in me.Vertices) { min = System.Numerics.Vector3.Min(min, v.Position); max = System.Numerics.Vector3.Max(max, v.Position); }
            Console.WriteLine($"{Path.GetFileName(f.Name)} bounds {min} .. {max} hdr {fl.Header.BoundingBoxMin}..{fl.Header.BoundingBoxMax}");
            for (int i = 0; i < fl.Nodes.Count; i++) { var n = fl.Nodes[i]; Console.WriteLine($"  node{i} {n.Name} p={n.ParentIndex} t={n.Translation} r={n.Rotation} s={n.Scale} bb={n.BoundingBoxMin}..{n.BoundingBoxMax} flags={n.Flags}"); }
            foreach (var dm in fl.Dummies) Console.WriteLine($"  dummy {dm.ReferenceID} pos={dm.Position} fwd={dm.Forward} up={dm.Upward} parent={dm.ParentBoneIndex} attach={dm.AttachBoneIndex} useUp={dm.UseUpwardVector}");
            foreach (var me in fl.Meshes) Console.WriteLine($"  mesh facesets={me.FaceSets.Count} [{string.Join(",", me.FaceSets.Select(fs => $"{fs.Flags}:{fs.Indices.Count}:strip={fs.TriangleStrip}:cull={fs.CullBackfaces}"))}] uvs={me.Vertices[0].UVs.Count} tans={me.Vertices[0].Tangents.Count} colors={me.Vertices[0].Colors.Count} bbox={me.BoundingBox?.Min}..{me.BoundingBox?.Max}");
            foreach (var me in fl.Meshes)
                for (int k = 0; k < me.Vertices[0].UVs.Count; k++)
                {
                    var us = me.Vertices.Select(v => v.UVs[k]).ToList();
                    Console.WriteLine($"  uv{k} u={us.Min(u => u.X):0.###}..{us.Max(u => u.X):0.###} v={us.Min(u => u.Y):0.###}..{us.Max(u => u.Y):0.###}");
                    if (k == 1)
                    {
                        // which position axis does uv1 follow?
                        var v0 = me.Vertices.OrderBy(v => v.Position.Y).First(); var v1 = me.Vertices.OrderBy(v => v.Position.Y).Last();
                        Console.WriteLine($"  uv1 at lowest Y {v0.Position} -> {v0.UVs[1]}, highest Y {v1.Position} -> {v1.UVs[1]}");
                    }
                }
            foreach (var me in fl.Meshes) Console.WriteLine($"  color0 {me.Vertices[0].Colors[0].R},{me.Vertices[0].Colors[0].G},{me.Vertices[0].Colors[0].B},{me.Vertices[0].Colors[0].A}  distinct={me.Vertices.Select(v => v.Colors[0].A).Distinct().Count()}");
            foreach (var m in fl.Materials) Console.WriteLine($"  mat gx={m.GXIndex} idx={m.Index}");
            Console.WriteLine($"  gxlists={fl.GXLists.Count} buflayout0={fl.BufferLayouts.Count} hdr unicode={fl.Header.Unicode} unk5c={fl.Header.Unk5C}");
        }
        return 0;
    }
    case "bc4probe":
    {
        // bc4probe : header of a BC4 DDS from the encoder, and how SoulsFormats reads it (dev)
        var enc = new BCnEncoder.Encoder.BcEncoder();
        enc.OutputOptions.GenerateMipMaps = true; enc.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc4; enc.OutputOptions.FileFormat = BCnEncoder.Shared.OutputFileFormat.Dds;
        var px = new byte[64 * 64 * 4]; using var ms = new MemoryStream();
        enc.EncodeToStream(px, 64, 64, BCnEncoder.Encoder.PixelFormat.Rgba32, ms);
        var b = ms.ToArray();
        Console.WriteLine($"len={b.Length} {Convert.ToHexString(b, 0, 148)}");
        var dds = new DDS(b);
        Console.WriteLine($"mips={dds.dwMipMapCount} flags={dds.dwFlags} fourcc={dds.ddspf.dwFourCC} dxgi={dds.GetDXGIFormat()} h10={(dds.header10 == null ? "-" : dds.header10.dxgiFormat.ToString())}");
        return 0;
    }
    case "tpflist":
    {
        // tpflist <partsbnd> : each texture's format, header and byte size (dev)
        var bnd = BND4.Read(args[1]);
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
        {
            var tpf = TPF.Read(f.Bytes);
            Console.WriteLine($"{Path.GetFileName(f.Name)} platform={tpf.Platform} enc={tpf.Encoding} flag2={tpf.Flag2}");
            foreach (var t in tpf.Textures)
                Console.WriteLine($"  {t.Name} fmt={t.Format} type={t.Type} mips={t.Mipmaps} flags1={t.Flags1} bytes={t.Bytes.Length} hdr={(t.Header == null ? "-" : $"{t.Header.Width}x{t.Header.Height} dxgi={t.Header.DXGIFormat} u1={t.Header.Unk1} u2={t.Header.Unk2} tex={t.Header.TextureCount}")}");
        }
        return 0;
    }
    case "tpfnames":
    {
        // tpfnames <tpf.dcx> : texture names and sizes of a loose texture file (dev)
        foreach (var t in TPF.Read(args[1]).Textures) Console.WriteLine($"{t.Name} {t.Header?.Width}x{t.Header?.Height} bytes={t.Bytes.Length}");
        return 0;
    }
    case "tpfpng":
    {
        // tpfpng <tpf.dcx> <outdir> <name filter> : textures of a loose texture file as pictures (dev)
        Directory.CreateDirectory(args[2]);
        foreach (var t in TPF.Read(args[1]).Textures.Where(t => t.Name.Contains(args[3])))
        {
            var ddsFile = BCnEncoder.Shared.ImageFiles.DdsFile.Load(new MemoryStream(t.Headerize())); var px = new BCnEncoder.Decoder.BcDecoder().Decode(ddsFile); int W = (int)ddsFile.header.dwWidth, H = (int)ddsFile.header.dwHeight;
            using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(W, H);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { var c = px[y * W + x]; img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(c.r, c.g, c.b, 255); }
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, Path.Combine(args[2], t.Name + ".png"));
            Console.WriteLine($"{t.Name} {W}x{H} fmt={t.Format}");
        }
        return 0;
    }
    case "icondump":
    {
        // icondump <tpf.dcx> <out.png> : a loose texture file's first texture as a picture (dev)
        var t = TPF.Read(args[1]).Textures[0];
        var dds = t.Headerize();
        var ddsFile = BCnEncoder.Shared.ImageFiles.DdsFile.Load(new MemoryStream(dds)); var px = new BCnEncoder.Decoder.BcDecoder().Decode(ddsFile); int W = (int)ddsFile.header.dwWidth, H = (int)ddsFile.header.dwHeight;
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(W, H);
        double a = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { var c = px[y * W + x]; img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(c.r, c.g, c.b, c.a); a += c.a; }
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, args[2]);
        Console.WriteLine($"{t.Name} {W}x{H} fmt={t.Format} dxgi={t.Header?.DXGIFormat} u1={t.Header?.Unk1} u2={t.Header?.Unk2} fourcc={ddsFile.header.ddsPixelFormat.dwFourCc} dx10={ddsFile.dx10Header.dxgiFormat} meanA={a / (W * H):0}");
        return 0;
    }
    case "tpfdump":
    {
        // tpfdump <partsbnd> <outdir> : de-swizzled DDS of each texture, and channel means (dev)
        var bnd = BND4.Read(args[1]);
        Directory.CreateDirectory(args[2]);
        foreach (var f in bnd.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)))
            foreach (var t in TPF.Read(f.Bytes).Textures)
            {
                var dds = t.Headerize();
                File.WriteAllBytes(Path.Combine(args[2], t.Name + ".dds"), dds);
                var ddsFile = BCnEncoder.Shared.ImageFiles.DdsFile.Load(new MemoryStream(dds)); var px = new BCnEncoder.Decoder.BcDecoder().Decode(ddsFile); int W = (int)ddsFile.header.dwWidth, H = (int)ddsFile.header.dwHeight;
                double r = 0, g = 0, b = 0, a = 0; long n = 0;
                for (int y = 0; y < H; y += 4) for (int x = 0; x < W; x += 4) { var c = px[y * W + x]; r += c.r; g += c.g; b += c.b; a += c.a; n++; }
                Console.WriteLine($"{t.Name} {W}x{H} fmt={t.Format} mean r={r / n:0} g={g / n:0} b={b / n:0} a={a / n:0}");
            }
        return 0;
    }
    default:
        Console.WriteLine("usage: LopForge key <LiesOfP folder>");
        return 1;
}







