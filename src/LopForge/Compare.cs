using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LopForge;

// Dev check: the Lies of P move on Pino's own skeleton (top) and the converted move on the Hunter (bottom),
// same moments, both drawn in Bloodborne space from the front and the side.
public static class Compare
{
    public static void Render(LopAnim a, LopMontage mt, BbAnim conv, Retarget.BbSkel bb, string outPng, int shots = 8)
    {
        int cell = 220, W = shots * cell, H = cell * 4;
        using var img = new Image<Rgba32>(W, H, new Rgba32(30, 30, 36, 255));
        var lpar = a.RefBones.Select(b => b.parent).ToList();
        var trackOf = a.RefBones.Select(b => a.Tracks.FindIndex(t => t.Bone == b.name)).ToArray();
        int lRoot = a.RefBones.FindIndex(b => b.name == "Root");
        float t0 = mt.SeqStart, t1 = mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps;
        for (int s = 0; s < shots; s++)
        {
            float u = s / (float)Math.Max(1, shots - 1);
            // Lies of P
            float st = t0 + u * (t1 - t0);
            int f = Math.Clamp((int)MathF.Round(st * a.Fps), 0, a.Frames - 1);
            var (lp, _) = Retarget.Model(lpar, i => i == lRoot ? Vector3.Zero : trackOf[i] >= 0 ? a.Tracks[trackOf[i]].T[f] : a.RefBones[i].t,
                                               i => trackOf[i] >= 0 ? a.Tracks[trackOf[i]].R[f] : a.RefBones[i].r);
            var lpos = lp.Select(ForgeAnims.Gp).ToArray();
            Draw(img, lpos, lpar.ToArray(), a.RefBones.Select(b => b.name).ToArray(), s * cell, 0, cell);
            // Converted
            int cf = Math.Clamp((int)MathF.Round(u * (conv.Frames - 1)), 0, conv.Frames - 1);
            var (bp, _) = Retarget.Model(bb.Parent,
                i => { int t = Array.IndexOf(conv.TrackToBone, (short)i); return t >= 0 ? conv.Pose[cf, t].Translation : bb.T[i]; },
                i => { int t = Array.IndexOf(conv.TrackToBone, (short)i); return t >= 0 ? conv.Pose[cf, t].Rotation : bb.R[i]; });
            Draw(img, bp, bb.Parent, bb.Names, s * cell, cell * 2, cell);
        }
        img.SaveAsPng(outPng);
    }

    // Mean/max angle (degrees) between each limb segment's direction in the Lies of P move and in the converted one.
    public static string Measure(LopAnim a, LopMontage mt, BbAnim conv, Retarget.BbSkel bb)
    {
        var lpar = a.RefBones.Select(b => b.parent).ToList();
        var trackOf = a.RefBones.Select(b => a.Tracks.FindIndex(t => t.Bone == b.name)).ToArray();
        int lRoot = a.RefBones.FindIndex(b => b.name == "Root");
        int L(string n) => a.RefBones.FindIndex(b => b.name == n);
        float t0 = mt.SeqStart, t1 = mt.SeqEnd > mt.SeqStart ? mt.SeqEnd : a.Frames / a.Fps;
        var segs = new List<(string name, int b0, int b1, int l0, int l1)>();
        foreach (var s in new[] { "L", "R" })
        {
            segs.Add(($"{s}clav", bb.Index($"{s}_Clavicle"), bb.Index($"{s}_UpperArm"), L($"Bip001-{s}-Clavicle"), L($"Bip001-{s}-UpperArm")));
            segs.Add(($"{s}upper", bb.Index($"{s}_UpperArm"), bb.Index($"{s}_Forearm"), L($"Bip001-{s}-UpperArm"), L($"Bip001-{s}-Forearm")));
            segs.Add(($"{s}fore", bb.Index($"{s}_Forearm"), bb.Index($"{s}_Hand"), L($"Bip001-{s}-Forearm"), L($"Bip001-{s}-Hand")));
            segs.Add(($"{s}hand", bb.Index($"{s}_Hand"), bb.Index($"{s}_Finger2"), L($"Bip001-{s}-Hand"), L($"Bip001-{s}-Finger2")));
            segs.Add(($"{s}thigh", bb.Index($"{s}_Thigh"), bb.Index($"{s}_Calf"), L($"Bip001-{s}-Thigh"), L($"Bip001-{s}-Calf")));
            segs.Add(($"{s}calf", bb.Index($"{s}_Calf"), bb.Index($"{s}_Foot"), L($"Bip001-{s}-Calf"), L($"Bip001-{s}-Foot")));
        }
        var sum = new double[segs.Count]; var max = new double[segs.Count]; int n = 0;
        for (int cf = 0; cf < conv.Frames; cf += 2)
        {
            float u = cf / (float)Math.Max(1, conv.Frames - 1);
            int f = Math.Clamp((int)MathF.Round((t0 + u * (t1 - t0)) * a.Fps), 0, a.Frames - 1);
            var (lp, lr) = Retarget.Model(lpar, i => i == lRoot ? Vector3.Zero : trackOf[i] >= 0 ? a.Tracks[trackOf[i]].T[f] : a.RefBones[i].t,
                                               i => trackOf[i] >= 0 ? a.Tracks[trackOf[i]].R[f] : a.RefBones[i].r);
            var (bp, br) = Retarget.Model(bb.Parent,
                i => { int t = Array.IndexOf(conv.TrackToBone, (short)i); return t >= 0 ? conv.Pose[cf, t].Translation : bb.T[i]; },
                i => { int t = Array.IndexOf(conv.TrackToBone, (short)i); return t >= 0 ? conv.Pose[cf, t].Rotation : bb.R[i]; });
            if (cf == 0)
            {
                int fo = bb.Index("R_Forearm"), ha = bb.Index("R_Hand"), ua = bb.Index("R_UpperArm");
                Console.WriteLine($"   dbg forearm X={Vector3.Transform(Vector3.UnitX, br[fo]):0.00} seg={Vector3.Normalize(bp[ha] - bp[fo]):0.00} upper X={Vector3.Transform(Vector3.UnitX, br[ua]):0.00} seg={Vector3.Normalize(bp[fo] - bp[ua]):0.00}");
                int tf = Array.IndexOf(conv.TrackToBone, (short)fo), th = Array.IndexOf(conv.TrackToBone, (short)ha);
                Console.WriteLine($"   dbg tracks forearm={tf} hand={th} handT={(th >= 0 ? conv.Pose[cf, th].Translation : Vector3.Zero):0.000} restT={bb.T[ha]:0.000}");
                int lfo = L("Bip001-R-Forearm"), lha = L("Bip001-R-Hand"); Console.WriteLine($"   dbg lop forearmX={ForgeAnims.Gp(Vector3.Transform(Vector3.UnitX * 100, lr[lfo])):0.00} handTrack={(trackOf[lha] >= 0 ? a.Tracks[trackOf[lha]].T[f] : a.RefBones[lha].t):0.0} foreTrack={(trackOf[lfo] >= 0 ? a.Tracks[trackOf[lfo]].T[f] : a.RefBones[lfo].t):0.0}"); Console.WriteLine($"   dbg lop forearm seg={Vector3.Normalize(ForgeAnims.Gp(lp[L("Bip001-R-Hand")]) - ForgeAnims.Gp(lp[L("Bip001-R-Forearm")])):0.00}");
            }
            for (int k = 0; k < segs.Count; k++)
            {
                var sg = segs[k]; if (sg.b0 < 0 || sg.b1 < 0 || sg.l0 < 0 || sg.l1 < 0) continue;
                var dl = Vector3.Normalize(ForgeAnims.Gp(lp[sg.l1]) - ForgeAnims.Gp(lp[sg.l0]));
                var db = Vector3.Normalize(bp[sg.b1] - bp[sg.b0]);
                double ang = Math.Acos(Math.Clamp(Vector3.Dot(dl, db), -1, 1)) * 57.2958;
                sum[k] += ang; max[k] = Math.Max(max[k], ang);
            }
            n++;
        }
        return string.Join("  ", segs.Select((s, k) => $"{s.name} {sum[k] / Math.Max(1, n):0}/{max[k]:0}"));
    }

    static void Draw(Image<Rgba32> img, Vector3[] P, int[] parent, string[] names, int x0, int y0, int cell)
    {
        for (int view = 0; view < 2; view++)
        {
            int ox = x0 + cell / 2, oy = y0 + view * cell + cell - 12; float sc = 95;
            Vector2 Pr(Vector3 p) => view == 0 ? new(ox + p.X * sc, oy - p.Y * sc) : new(ox + p.Z * sc, oy - p.Y * sc);
            for (int b = 0; b < P.Length; b++)
            {
                int pa = parent[b]; if (pa < 0) continue;
                var n = names[b];
                bool left = n.StartsWith("L_") || n.Contains("-L-") || n.Contains("_L_"), right = n.StartsWith("R_") || n.Contains("-R-") || n.Contains("_R_");
                if (!(n.StartsWith("Bip001") || n.StartsWith("L_") || n.StartsWith("R_") || n.StartsWith("Spine") || n.StartsWith("Neck") || n.StartsWith("Head") || n == "Pelvis")) continue;
                if (n.Contains("Twist") || n.Contains("Finger")) continue;
                var c = left ? new Rgba32(90, 170, 255) : right ? new Rgba32(255, 120, 90) : new Rgba32(220, 220, 220);
                AnimPreview.Line(img, Pr(P[pa]), Pr(P[b]), c);
            }
        }
    }
}

