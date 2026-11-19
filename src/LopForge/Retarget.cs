using System.Numerics;
using HKX2;
using SoulsAssetPipeline.Animation;

namespace LopForge;

// Moves a Lies of P animation (Pino's 3ds Max Biped rig, Unreal space) onto Bloodborne's Hunter (the same kind of
// Biped rig, Havok space): each mapped bone gets the Lies of P bone's orientation in character space, the
// rest follows the Hunter's own rest pose; the walk/lunge distance becomes Bloodborne's root motion.
public static class Retarget
{
    // Bloodborne bone <- Lies of P bone.
    public static readonly Dictionary<string, string> Map = BuildMap();
    static Dictionary<string, string> BuildMap()
    {
        var m = new Dictionary<string, string>
        {
            ["Pelvis"] = "Bip001-Pelvis", ["Spine"] = "Bip001-Spine", ["Spine1"] = "Bip001-Spine1", ["Spine2"] = "Bip001-Spine2",
            ["Neck"] = "Bip001-Neck", ["Head"] = "Bip001-Head",
            // The weapon bone: Lies of P turns it against the hand for twirls and flicks (creator 2026-11-10).
            ["R_Weapon"] = "BN_Weapon_R",
        };
        foreach (var s in new[] { "L", "R" })
        {
            m[$"{s}_Thigh"] = $"Bip001-{s}-Thigh"; m[$"{s}_Calf"] = $"Bip001-{s}-Calf"; m[$"{s}_Foot"] = $"Bip001-{s}-Foot"; m[$"{s}_Toe0"] = $"Bip001-{s}-Toe0";
            m[$"{s}_Clavicle"] = $"Bip001-{s}-Clavicle"; m[$"{s}_UpperArm"] = $"Bip001-{s}-UpperArm"; m[$"{s}_Forearm"] = $"Bip001-{s}-Forearm"; m[$"{s}_Hand"] = $"Bip001-{s}-Hand";
            for (int f = 0; f <= 4; f++) { m[$"{s}_Finger{f}"] = $"Bip001-{s}-Finger{f}"; m[$"{s}_Finger{f}1"] = $"Bip001-{s}-Finger{f}1"; m[$"{s}_Finger{f}2"] = $"Bip001-{s}-Finger{f}2"; }
        }
        return m;
    }

    // Unreal (left-handed, Z up, cm) -> right-handed Z up: negate Y.
    public static Vector3 Rh(Vector3 p) => new(p.X, -p.Y, p.Z);
    public static Quaternion Rh(Quaternion q) => new(-q.X, q.Y, -q.Z, q.W);

    public static (Vector3[] p, Quaternion[] r) Model(IReadOnlyList<int> parent, Func<int, Vector3> t, Func<int, Quaternion> r)
    {
        int n = parent.Count; var P = new Vector3[n]; var R = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            int pa = parent[i];
            if (pa < 0) { P[i] = t(i); R[i] = r(i); }
            else { P[i] = P[pa] + Vector3.Transform(t(i), R[pa]); R[i] = Quaternion.Normalize(R[pa] * r(i)); }
        }
        return (P, R);
    }

    public sealed class BbSkel
    {
        public string[] Names = Array.Empty<string>(); public int[] Parent = Array.Empty<int>();
        public Vector3[] T = Array.Empty<Vector3>(); public Quaternion[] R = Array.Empty<Quaternion>();
        public static BbSkel From(hkaSkeleton sk)
        {
            var s = new BbSkel { Names = sk.m_bones.Select(b => b.m_name).ToArray(), Parent = sk.m_parentIndices.Select(x => (int)x).ToArray() };
            s.T = sk.m_referencePose.Select(m => new Vector3(m.M11, m.M12, m.M13)).ToArray();
            s.R = sk.m_referencePose.Select(m => new Quaternion(m.M21, m.M22, m.M23, m.M24)).ToArray();
            return s;
        }
        public int Index(string n) => Array.IndexOf(Names, n);
    }

    public static void Probe(LopAnim a, BbSkel bb, Action<string> log)
    {
        var lp = a.RefBones.Select(b => b.parent).ToList();
        var (LP, LR) = Model(lp, i => Rh(a.RefBones[i].t), i => Rh(a.RefBones[i].r));
        var (BP, BR) = Model(bb.Parent, i => bb.T[i], i => bb.R[i]);
        int L(string n) => a.RefBones.FindIndex(b => b.name == n);
        foreach (var (bn, ln) in new[] { ("Pelvis", "Bip001-Pelvis"), ("Head", "Bip001-Head"), ("L_Hand", "Bip001-L-Hand"), ("R_Hand", "Bip001-R-Hand"), ("L_Foot", "Bip001-L-Foot"), ("L_Toe0", "Bip001-L-Toe0"), ("L_UpperArm", "Bip001-L-UpperArm"), ("L_Forearm", "Bip001-L-Forearm") })
        {
            int b = bb.Index(bn), l = L(ln);
            var bx = Vector3.Transform(Vector3.UnitX, BR[b]); var lx = l < 0 ? Vector3.Zero : Vector3.Transform(Vector3.UnitX, LR[l]);
            log($"{bn,-12} bb pos={BP[b]:0.00} xaxis={bx:0.00}   lop pos={(l < 0 ? "missing" : (LP[l] / 100).ToString("0.00"))} xaxis={lx:0.00}");
        }
    }
}
