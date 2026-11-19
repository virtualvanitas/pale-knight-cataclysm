using System.Numerics;
using HKX2;
using SoulsFormats;

namespace LopForge;

// A weapon's own moving part, the way Bloodborne animates the Hunter Pistol's barrel and flintlock: its parts file
// carries an animation package (WP_A_xxxx.anibnd: skeleton.HKX, a TAE whose slots are the character's moves, e.g.
// 117051000, plus 999000000 at rest, and one hkx per animation), and its held model binds points to the package's
// bones (B_Master, B_Barrel). The package is built from the player's own copy of the template weapon's (items
// partAnimFrom, 2500 = Hunter Pistol): its skeleton as is, its slot layout moved to this weapon's moveset, and new
// animations: at rest the part stays put; on the buffed-shot moves (51010, 52160, 52260, 52560 and their variants, Bone Marrow Ash's variants,
// which the Strengthen Bullet now counts as) it slides out by the piece's MoveDelta, holds, and slides back.
public sealed class PartAnim
{
    BinderFile outer = null!;
    BND4 inner = null!;
    hkaSkeleton skel = null!;
    byte[] templateHkx = null!;
    string oldTag = "", newTag = "";
    int oldCat;
    public int ExtendCount;
    const string Master = "B_Master", Moving = "B_Barrel";

    public static PartAnim From(ItemRow it, string suffix)
    {
        var src = BND4.Read(Layers.Source(Path.Combine("parts", $"wp_a_{it.partAnimFrom:D4}{suffix}.partsbnd.dcx")));
        var o = src.Files.FirstOrDefault(f => f.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"wp_a_{it.partAnimFrom:D4}{suffix} has no animation package");
        var p = new PartAnim { outer = o, inner = BND4.Read(o.Bytes), oldTag = $"WP_A_{it.partAnimFrom:D4}", newTag = $"WP_A_{it.equipModelId:D4}" };
        p.skel = BbHavok.Find<hkaSkeleton>(BbHavok.Read(p.inner.Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!;
        var tae = TAE.Read(p.inner.Files.First(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        p.oldCat = (int)(tae.Animations.First(a => a.ID != 999_000_000).ID / 1_000_000);
        p.templateHkx = p.inner.Files.First(f => f.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes;
        if (p.Bone(Master) < 0 || p.Bone(Moving) < 0) throw new InvalidDataException("animation package lacks " + Master + "/" + Moving);
        return p;
    }

    int Bone(string name) => skel.m_bones.FindIndex(b => b.m_name == name);
    // HKX2 packs a qsTransform as rows: translation, rotation (xyzw), scale.
    Vector3 RefT(int b) { var m = skel.m_referencePose[b]; return new Vector3(m.M11, m.M12, m.M13); }
    Quaternion RefR(int b) { var m = skel.m_referencePose[b]; return new Quaternion(m.M21, m.M22, m.M23, m.M24); }

    // The held model: two nodes named as the package's bones, at its rest pose, and every point bound to one of them.
    internal void Rig(FLVER2 fl, ForgeModels.Piece piece)
    {
        var moving = piece.Moving!; int pts = piece.P.Count;
        int bm = Bone(Master), bb = Bone(Moving);
        if (RefR(bm).W < 0.9999f || RefR(bb).W < 0.9999f) throw new InvalidDataException("package bones are turned; the rig expects unrotated bones");
        fl.Nodes.RemoveAll(n => n.Name == Master || n.Name == Moving);
        short mi = (short)fl.Nodes.Count, bi = (short)(mi + 1);
        var nm = new FLVER.Node { Name = Master, ParentIndex = -1, FirstChildIndex = bi, Translation = RefT(bm) };
        var nb = new FLVER.Node { Name = Moving, ParentIndex = mi, FirstChildIndex = -1, Translation = RefT(bb) };
        fl.Nodes.Add(nm); fl.Nodes.Add(nb);
        int layout = fl.BufferLayouts.FindIndex(l => l.Any(m => m.Semantic == FLVER.LayoutSemantic.BoneIndices) && l.Any(m => m.Semantic == FLVER.LayoutSemantic.BoneWeights));
        if (layout < 0) throw new InvalidDataException("model has no vertex layout with bone weights");
        var me = fl.Meshes[0];
        if (me.Vertices.Count != pts) throw new InvalidDataException("rig: model points don't match the piece");
        me.UseBoneWeights = true;
        foreach (var vb in me.VertexBuffers) vb.LayoutIndex = layout;
        Vector3 loM = new(float.MaxValue), hiM = new(float.MinValue), loB = loM, hiB = hiM;
        for (int i = 0; i < me.Vertices.Count; i++)
        {
            var v = me.Vertices[i];
            // Weights in 255ths (the model stores them as bytes), the two summing to exactly one.
            int share = (int)MathF.Round(moving[i] * 255);
            var idx = v.BoneIndices; var w = v.BoneWeights;
            if (share >= 255) { idx[0] = bi; w[0] = 1; idx[1] = 0; w[1] = 0; }
            else if (share <= 0) { idx[0] = mi; w[0] = 1; idx[1] = 0; w[1] = 0; }
            else { idx[0] = bi; w[0] = share / 255f; idx[1] = mi; w[1] = (255 - share) / 255f; }
            idx[2] = idx[3] = 0; w[2] = w[3] = 0;
            v.BoneIndices = idx; v.BoneWeights = w;
            if (share > 0) { loB = Vector3.Min(loB, v.Position); hiB = Vector3.Max(hiB, v.Position); }
            if (share < 255) { loM = Vector3.Min(loM, v.Position); hiM = Vector3.Max(hiM, v.Position); }
        }
        // Node boxes are in the node's own space.
        var wm = RefT(bm); var wb = wm + RefT(bb);
        if (hiM.X >= loM.X) { nm.BoundingBoxMin = loM - wm; nm.BoundingBoxMax = hiM - wm; }
        if (hiB.X >= loB.X) { nb.BoundingBoxMin = loB - wb; nb.BoundingBoxMax = hiB - wb; }
    }

    // The package for this weapon: the template's skeleton, its slots on this weapon's moveset, new animations.
    internal BinderFile Package(ItemRow it, ForgeModels.Piece piece)
    {
        var delta = piece.MoveDelta;
        int cat = it.wepmotionCategory;
        string Rn(string s) => s.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
        var tf = inner.Files.First(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase));
        var tae = TAE.Read(tf.Bytes);
        string dir = Path.GetDirectoryName(Rn(inner.Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Name))!;
        byte[] rest = Anim(delta, null);
        // Each buffed-shot move's timing (items partAnimTimes "slot:fire:free"): out before the shot, back in by the time
        // the player can act again, so the next move never cuts the slide short (creator 0.3.25: not smooth going back in).
        var times = new Dictionary<int, (float fire, float free)>();
        foreach (var part in it.partAnimTimes.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var x = part.Split(':'); var inv = System.Globalization.CultureInfo.InvariantCulture;
            times[int.Parse(x[0])] = (float.Parse(x[1], inv), float.Parse(x[2], inv));
        }
        long Map(long id) => id == 999_000_000 ? id : (long)cat * 1_000_000 + id % 1_000_000;
        var anims = new List<TAE.Animation>();
        var files = new List<BinderFile>();
        void AddHkx(long id, byte[] bytes)
        {
            string stem = id == 999_000_000 ? "a999_000000" : $"a{cat:D3}_{id % 1_000_000:D6}";
            files.Add(new BinderFile(outer.Flags, (int)(1_000_000_000 + id % 1_000_000_000), Path.Combine(dir.Replace("\\tae", "\\hkx"), stem + ".hkx"), bytes));
        }
        foreach (var a in tae.Animations)
        {
            long id = Map(a.ID); int slot = (int)(a.ID % 1_000_000);
            bool buffed = a.ID != 999_000_000 && slot % 50 >= 10;   // 51010-51015, 52160-52165 ...: Bone Marrow Ash shots
            string stem = id == 999_000_000 ? "a999_000000" : $"a{cat:D3}_{slot:D6}";
            TAE.Animation na;
            if (buffed && slot % 10 == 0)
            {
                na = new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.Standard { ImportHKXSourceAnimID = -1 }, stem + ".hkt");
                if (!times.TryGetValue(slot, out var tm)) throw new InvalidDataException($"items partAnimTimes has no timing for {cat}_{slot}");
                AddHkx(id, Anim(delta, tm)); ExtendCount++;
            }
            else if (buffed)
                na = new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = (int)Map(a.ID - slot % 10) }, "");
            else if (a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io)
                na = new TAE.Animation(id, new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = (int)Map(io.ImportFromAnimID), Unknown = io.Unknown }, "");
            else
            {
                na = new TAE.Animation(id, a.MiniHeader.GetClone(), stem + ".hkt");
                AddHkx(id, rest);
            }
            anims.Add(na);
        }
        tae.Animations = anims;
        // The template package's own container settings, with this weapon's files in it.
        var nb = inner;
        var skelFiles = inner.Files.Where(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).ToList();
        nb.Files = new List<BinderFile>();
        foreach (var f in skelFiles)
            nb.Files.Add(new BinderFile(f.Flags, f.ID, Rn(f.Name), f.Bytes));
        nb.Files.Add(new BinderFile(tf.Flags, tf.ID, Rn(tf.Name), tae.Write()));
        nb.Files.AddRange(files);
        nb.Files.Sort((x, y) => x.ID.CompareTo(y.ID));
        return new BinderFile(outer.Flags, outer.ID, Rn(outer.Name), nb.Write());
    }

    // One animation of the package's skeleton at 30 fps: every bone at rest, the moving bone pushed out by delta * s(t).
    // With a timing: out by the shot (at most 0.1 s), held through the recoil, then eased back in so it is home by the
    // time the player can act (retract 0.15-0.45 s); without one, at rest.
    byte[] Anim(Vector3 delta, (float fire, float free)? timing)
    {
        float outEnd = 0, back = 0, home = 0, dur = 3f;
        int frames = 2;
        if (timing is { } tm)
        {
            outEnd = Math.Clamp(tm.fire, 0.04f, 0.1f);
            // Out through the recoil; slides back in as the Hunter straightens up, a little after
            // the move frees the player (creator 0.3.28: back in too quickly to be seen).
            // Lies of P's own normal Cataclysm shot (MT_Arm_ChargeShotgun_Fire1, its BN_ChargeShotgun_Move curve): out by
            // 0.3 s, held until about 0.4 s after the player may move again, pulled in over 0.2 s (creator 0.3.29: in
            // too soon). Here: back in from 0.5 s after the move frees the player, over 0.3 s.
            back = MathF.Max(tm.fire + 0.15f, tm.free + 0.5f);
            home = back + 0.3f;
            // At least as long as the longest buffed-shot move (3.9 s): the game restarts a weapon animation that ends
            // while the move still plays, which popped the barrels out again as the Hunter straightened up (creator 0.3.27).
            dur = MathF.Max(home + 0.5f, 4.5f); frames = (int)MathF.Round(dur * 30) + 1;
        }
        int mv = Bone(Moving);
        var root = BbHavok.Read(templateHkx);
        var ac = root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().First();
        var old = ac.m_animations[0];
        var map = ac.m_bindings[0].m_transformTrackToBoneIndices;
        int tracks = old.m_numberOfTransformTracks;
        static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        var tr = new List<Matrix4x4>(frames * tracks);
        for (int f = 0; f < frames; f++)
        {
            float t = frames > 1 ? dur * f / (frames - 1) : 0;
            float s = timing == null ? 0 : t < outEnd ? Smooth(t / outEnd) : t < back ? 1 : 1 - Smooth((t - back) / (home - back));
            for (int k = 0; k < tracks; k++)
            {
                int b = k < map.Count ? map[k] : k;
                var m = skel.m_referencePose[b];
                if (b == mv) { m.M11 += delta.X * s; m.M12 += delta.Y * s; m.M13 += delta.Z * s; }
                tr.Add(new Matrix4x4(m.M11, m.M12, m.M13, 0, m.M21, m.M22, m.M23, m.M24, 1, 1, 1, 0, 0, 0, 0, 0));
            }
        }
        var anim = new hkaInterleavedUncompressedAnimation
        {
            m_type = AnimationType.HK_INTERLEAVED_ANIMATION,
            m_duration = dur,
            m_numberOfTransformTracks = tracks,
            m_numberOfFloatTracks = 0,
            m_extractedMotion = null,
            m_annotationTracks = old.m_annotationTracks.Select(t => new hkaAnnotationTrack { m_trackName = t.m_trackName, m_annotations = new List<hkaAnnotationTrackAnnotation>() }).ToList(),
            m_transforms = tr,
            m_floats = new List<float>(),
        };
        ac.m_animations[0] = anim;
        foreach (var bnd in ac.m_bindings) bnd.m_animation = anim;
        return BbHavok.Write(root);
    }
}
