using System.Numerics;
using CUE4Parse.FileProvider;
using HKX2;
using SoulsAssetPipeline.Animation;
using SoulsFormats;

namespace LopForge;

// Converts each moves-sheet row's Lies of P montage (the player's own copy) into a Bloodborne animation file
// for the Hunter, at 30 frames a second, with its forward motion as Bloodborne root motion.
public static class ForgeAnims
{
    public const float Fps = 30f;

    // Unreal component space (cm) -> Bloodborne character space (m): x_bb = -y, y_bb = z, z_bb = -x.
    static readonly Matrix4x4 GM = new(0, 0, -1, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1);
    static readonly Quaternion G = Quaternion.CreateFromRotationMatrix(GM);
    public static Vector3 Gp(Vector3 ue) => new Vector3(-ue.Y, ue.Z, -ue.X) / 100f;



    public sealed class Result { public byte[] Hkx = Array.Empty<byte>(); public float Duration; public int Frames; public LopMontage Montage = null!; }

    public static SupportRig? Rig;
    // The weapon bone: the Pale Knight model sits in Bloodborne's weapon bone as the Lies of P mesh sits in BN_Weapon_R
    // (axes x->-x, y->-z, z->-y, then the grip tilt), so the weapon bone turns as G * LoP weapon * inverse(that): the
    // blade points exactly where Lies of P's does (creator: the L2 shot pointed at the ground).
    public static float WeaponTilt;
    static Quaternion WeaponK
    {
        get
        {
            var a = Quaternion.CreateFromRotationMatrix(new Matrix4x4(-1, 0, 0, 0, 0, 0, -1, 0, 0, -1, 0, 0, 0, 0, 0, 1));
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, WeaponTilt * MathF.PI / 180f);
            return Quaternion.Normalize(Quaternion.Inverse(tilt * a));
        }
    }
    public static Result Convert(IFileProvider p, MoveRow row, Retarget.BbSkel bb, byte[] templateHkx, Dictionary<string, LopAnim> cache)
    {
        var mt = LopMontage.Load(p, "LiesofP/Content/ArtAsset/CH/PC/Pino/Animset/" + row.lopMontage);
        var seqPath = mt.Sequence.Split('.')[0];
        if (!cache.TryGetValue(seqPath, out var a)) cache[seqPath] = a = LopAnim.Load(p, seqPath);
        float t0 = mt.SeqStart, t1 = mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps;
        // Part of the move only (clipStart/clipEnd, seconds of the montage): e.g. the charge stance before the dash.
        if (row.clipEnd > row.clipStart && row.clipEnd > 0) t1 = Math.Min(t1, mt.SeqStart + row.clipEnd * mt.Rate);
        if (row.clipStart > 0)
        {
            t0 = mt.SeqStart + row.clipStart * mt.Rate;
            mt.Marks = mt.Marks.Select(m => (m.t - row.clipStart, m.dur, m.name, m.cls)).Where(m => m.Item1 >= 0).ToList();
        }
        if (row.duration > 0.05f)
        {
            float k = (t1 - t0) / mt.Rate / row.duration;   // >1: faster
            mt.Marks = mt.Marks.Select(m => (m.t / k, m.dur / k, m.name, m.cls)).ToList();
            mt.Rate = (t1 - t0) / row.duration;
        }
        float dur = (t1 - t0) / Math.Max(0.05f, mt.Rate);
        int frames = Math.Max(2, (int)MathF.Round(dur * Fps) + 1);

        var lpar = a.RefBones.Select(b => b.parent).ToList();
        int nL = a.RefBones.Count;
        var trackOf = new int[nL]; Array.Fill(trackOf, -1);
        for (int i = 0; i < a.Tracks.Count; i++) { int b = a.RefBones.FindIndex(x => x.name == a.Tracks[i].Bone); if (b >= 0) trackOf[b] = i; }
        int L(string n) => a.RefBones.FindIndex(b => b.name == n);
        int lRoot = L("Root");

        // Rest poses in Bloodborne space and the per-bone axis convention between the rigs.
        var (bRestP, bRestR) = Retarget.Model(bb.Parent, i => bb.T[i], i => bb.R[i]);
        var (lRestP, lRestR) = Retarget.Model(lpar, i => a.RefBones[i].t, i => a.RefBones[i].r);
        int nB = bb.Names.Length;
        var src = new int[nB]; var K = new Quaternion[nB];
        for (int b = 0; b < nB; b++)
        {
            src[b] = Retarget.Map.TryGetValue(bb.Names[b], out var ln) ? L(ln) : -1;
            if (src[b] < 0) continue;
            var k0 = Quaternion.Inverse(G * lRestR[src[b]]) * bRestR[b];
            // The weapon bone keeps its exact rest relation, so the model sits as at rest and only Lies of P's turn is added.
            K[b] = bb.Names[b].Contains("Weapon") ? WeaponK :  IsLimb(bb.Names[b]) ? Twist(k0) : Snap(k0);
        }


        var pose = new (Vector3 t, Quaternion r)[frames, nB];
        var rm = new List<Vector4>();
        Vector3 root0 = Vector3.Zero;
        for (int f = 0; f < frames; f++)
        {
            float st = t0 + f / Fps * mt.Rate;
            float sf = Math.Clamp(st * a.Fps, 0, a.Frames - 1);
            int f0 = (int)sf, f1 = Math.Min(a.Frames - 1, f0 + 1); float w = sf - f0;
            Vector3 LT(int i) { int tr = trackOf[i]; if (tr < 0) return a.RefBones[i].t; return Vector3.Lerp(a.Tracks[tr].T[f0], a.Tracks[tr].T[f1], w); }
            Quaternion LR(int i) { int tr = trackOf[i]; if (tr < 0) return a.RefBones[i].r; return Quaternion.Normalize(Quaternion.Slerp(a.Tracks[tr].R[f0], a.Tracks[tr].R[f1], w)); }
            // Root's travel becomes root motion; the pose is taken relative to where the root is (not its turn).
            var rootT = lRoot >= 0 ? LT(lRoot) : Vector3.Zero;
            if (f == 0) root0 = rootT;
            var (lp, lr) = Retarget.Model(lpar, i => i == lRoot ? Vector3.Zero : LT(i), LR);
            var travel = Gp(rootT - root0);
            float rs = row.rootScale > 0 ? row.rootScale : 1;   // moves sheet rootScale (e.g. a shorter dodge-attack lunge)
            rm.Add(new Vector4(travel.X * rs, 0, travel.Z * rs, 0));

            // Target orientation (Bloodborne space) for each mapped bone; everything else keeps the Hunter's rest pose.
            var M = new Quaternion[nB]; var P = new Vector3[nB];
            for (int b = 0; b < nB; b++)
            {
                int pa = bb.Parent[b];
                Quaternion want;
                if (src[b] >= 0) want = Quaternion.Normalize(G * lr[src[b]] * K[b]);
                else want = pa < 0 ? bRestR[b] : Quaternion.Normalize(M[pa] * bb.R[b]);
                M[b] = want;
                P[b] = pa < 0 ? bRestP[b] : P[pa] + Vector3.Transform(bb.T[b], M[pa]);
            }
            // The Hunter's skinning helpers follow the main bones the way they do in the game's own moves.
            Rig?.Apply(M);
            // Hips: Bloodborne's Master carries the hip position.
            int pel = bb.Index("Pelvis"), lpel = L("Bip001-Pelvis");
            var hip = Gp(lp[lpel]);
            // Foot IK targets (Master > X_Foot_Target2 > X_Foot_Target1 > X_Foot_Target): in the game's own
            // animations the last one sits exactly on the foot, and the game's foot IK pulls the feet to it.
            // Target2 moves, Target1 keeps its rest pose, Target turns with the foot.
            var tOverride = new Dictionary<int, Vector3>();
            foreach (var s in new[] { "L", "R" })
            {
                int t2 = bb.Index($"{s}_Foot_Target2"), tt1 = bb.Index($"{s}_Foot_Target1"), tg = bb.Index($"{s}_Foot_Target"), ft = bb.Index($"{s}_Foot");
                if (t2 < 0 || tt1 < 0 || tg < 0 || ft < 0) continue;
                int m0 = bb.Parent[t2];
                var p2 = P[ft] - Vector3.Transform(bb.T[tt1], M[t2]) - Vector3.Transform(bb.T[tg], M[tt1]);
                tOverride[t2] = Vector3.Transform(p2 - P[m0], Quaternion.Inverse(M[m0]));
                M[tg] = M[ft];
            }
            // Locals.
            for (int b = 0; b < nB; b++)
            {
                int pa = bb.Parent[b];
                var lr2 = pa < 0 ? M[b] : Quaternion.Normalize(Quaternion.Inverse(M[pa]) * M[b]);
                var lt = tOverride.TryGetValue(b, out var ov) ? ov : bb.T[b];
                if (b == 0) lt = hip;

                pose[f, b] = (lt, lr2);
            }
            // Master holds the hips: Root's rest offset is zero, so Master's position is the hip position.
            // Master's own turn is the rest turn; Root and below already hold the body's orientation.
            pose[f, 0] = (hip, bb.R[0]);
        }
        return new Result { Hkx = Write(templateHkx, pose, rm, frames, dur), Duration = dur, Frames = frames, Montage = mt };
    }

    // How the Lies of P grip holds its weapon mesh, in the Bloodborne weapon bone's space (dev): the axis images
    // of mesh X/Y/Z. Our model's current placement is x->-x, y->-z, z->-y.
    public static void GripProbe(LopAnim a, Retarget.BbSkel bb, string side, Action<string> log)
    {
        var lpar = a.RefBones.Select(b => b.parent).ToList();
        int L(string n) => a.RefBones.FindIndex(b => b.name == n);
        var (_, bRestR) = Retarget.Model(bb.Parent, i => bb.T[i], i => bb.R[i]);
        var (_, lRestR) = Retarget.Model(lpar, i => a.RefBones[i].t, i => a.RefBones[i].r);
        int bh = bb.Index($"{side}_Hand"), bw = bb.Index($"{side}_Weapon"), lh = L($"Bip001-{side}-Hand"), lw = L($"BN_Weapon_{side}");
        var K = SnapTwist(Quaternion.Inverse(G * lRestR[lh]) * bRestR[bh]);
        var Kfull = Quaternion.Inverse(G * lRestR[lh]) * bRestR[bh];
        var Wbb = bb.R[bw];
        var Wlop = Quaternion.Normalize(Quaternion.Inverse(lRestR[lh]) * lRestR[lw]);
        foreach (var (nm, k) in new[] { ("twist-snapped", K), ("full", Kfull) })
        {
            var A = Quaternion.Inverse(Wbb) * Quaternion.Inverse(k) * Wlop;
            log($"{side} grip ({nm}): mesh X->{Vector3.Transform(Vector3.UnitX, A):0.00} Y->{Vector3.Transform(Vector3.UnitY, A):0.00} Z->{Vector3.Transform(Vector3.UnitZ, A):0.00}");
        }
    }

    // The roll fix a limb bone gets (see Convert): Lies of P rest model rotation (Unreal) -> Bloodborne rest model rotation.
    public static Quaternion HandFix(Quaternion lopRest, Quaternion bbRest) => Twist(Quaternion.Inverse(G * lopRest) * bbRest);

    static bool IsLimb(string n) => n.Contains("Clavicle") || n.Contains("Arm", StringComparison.OrdinalIgnoreCase) || n.Contains("Hand") || n.Contains("Finger") || n.Contains("Thigh") || n.Contains("Calf");
    static bool IsUpper(string n) => n.StartsWith("Spine") || n.StartsWith("Neck") || n.StartsWith("Head") || n.Contains("Clavicle") || n.Contains("Arm") || n.Contains("Hand") || n.Contains("Finger") || n.Contains("Weapon") || n.StartsWith("Upper") || n.Contains("Shoulder") || n.Contains("Elbow") || n.Contains("Axilla");

    // Nearest of the 24 axis-aligned rotations.
    static Quaternion Snap(Quaternion q)
    {
        Quaternion best = Quaternion.Identity; float bd = -1;
        foreach (var c in Axis24.Value) { float d = MathF.Abs(Quaternion.Dot(c, q)); if (d > bd) { bd = d; best = c; } }
        return best;
    }
    // Keep only the turn about the bone (X) axis, to the nearest quarter turn: limbs point along X in both rigs,
    // so the rest of the difference is just the two rest poses' different stances.
    static Quaternion SnapTwist(Quaternion q)
    {
        var tw = Quaternion.Normalize(new Quaternion(q.X, 0, 0, q.W));
        float ang = 2 * MathF.Atan2(tw.X, tw.W);
        float s = MathF.Round(ang / (MathF.PI / 2)) * (MathF.PI / 2);
        return Quaternion.CreateFromAxisAngle(Vector3.UnitX, s);
    }
    // The exact roll about the bone (X) axis: limbs point along X in both rigs, so the rest of the difference is
    // the two rest poses' stances, while the roll decides which way elbows, knees and wrists bend.
    static Quaternion Twist(Quaternion q)
    {
        var tw = new Quaternion(q.X, 0, 0, q.W);
        return tw.LengthSquared() < 1e-8f ? Quaternion.Identity : Quaternion.Normalize(tw);
    }
    static readonly Lazy<List<Quaternion>> Axis24 = new(() =>
    {
        var list = new List<Quaternion>();
        var axes = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var x in axes) foreach (var y in axes)
        {
            if (MathF.Abs(Vector3.Dot(x, y)) > 0.5f) continue;
            var z = Vector3.Cross(x, y);
            var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
            list.Add(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)));
        }
        return list;
    });

    static byte[] Write(byte[] templateHkx, (Vector3 t, Quaternion r)[,] pose, List<Vector4> rm, int frames, float dur)
    {
        var root = BbHavok.Read(templateHkx);
        var ac = root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().First();
        var old = ac.m_animations[0];
        int n = pose.GetLength(1);
        var tr = new List<Matrix4x4>(frames * n);
        for (int f = 0; f < frames; f++)
            for (int b = 0; b < n; b++)
            {
                var (t, r) = pose[f, b];
                tr.Add(new Matrix4x4(t.X, t.Y, t.Z, 0, r.X, r.Y, r.Z, r.W, 1, 1, 1, 0, 0, 0, 0, 0));
            }
        var anim = new hkaInterleavedUncompressedAnimation
        {
            m_type = AnimationType.HK_INTERLEAVED_ANIMATION,
            m_duration = dur,
            m_numberOfTransformTracks = n,
            m_numberOfFloatTracks = 0,
            m_extractedMotion = new hkaDefaultAnimatedReferenceFrame { m_up = new Vector4(0, 1, 0, 0), m_forward = new Vector4(0, 0, 1, 0), m_duration = dur, m_referenceFrameSamples = rm },
            m_annotationTracks = old.m_annotationTracks.Select(t => new hkaAnnotationTrack { m_trackName = t.m_trackName, m_annotations = new List<hkaAnnotationTrackAnnotation>() }).ToList(),
            m_transforms = tr,
            m_floats = new List<float>(),
        };
        ac.m_animations[0] = anim;
        foreach (var b in ac.m_bindings) b.m_animation = anim;
        return BbHavok.Write(root);
    }
}


