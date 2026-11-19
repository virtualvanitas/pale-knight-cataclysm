using System.Numerics;

namespace LopForge;

// The Hunter's skinning helpers (Support_*: elbow, shoulder, forearm/upper-arm twist, knee, thigh twist, hips, skirt)
// are animated in the game's own moves, following the main bones. Lies of P has no such bones, so their rule is
// learned from the player's own Bloodborne animations: each helper turns by a fraction (full turn, or only the roll
// about the driver's bone axis) of how one nearby main bone has turned against the helper's parent since rest.
public sealed class SupportRig
{
    public sealed record Rule(int Bone, int Driver, bool TwistOnly, float W, float Err);
    public readonly List<Rule> Rules = new();
    readonly Retarget.BbSkel bb;
    readonly Quaternion[] restM;

    public SupportRig(Retarget.BbSkel skel)
    {
        bb = skel;
        restM = Retarget.Model(bb.Parent, i => bb.T[i], i => bb.R[i]).r;
    }

    static float Ang(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Min(1f, MathF.Abs(Quaternion.Dot(a, b))));

    static Quaternion Pow(Quaternion q, float w)
    {
        if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        float a = 2 * MathF.Acos(Math.Clamp(q.W, -1, 1));
        if (a < 1e-5f) return Quaternion.Identity;
        var axis = Vector3.Normalize(new Vector3(q.X, q.Y, q.Z));
        return Quaternion.CreateFromAxisAngle(axis, a * w);
    }

    static Quaternion TwistAbout(Quaternion q, Vector3 axis)
    {
        var p = Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis) * axis;
        var t = new Quaternion(p.X, p.Y, p.Z, q.W);
        return t.LengthSquared() < 1e-10f ? Quaternion.Identity : Quaternion.Normalize(t);
    }

    // Deviation of the driver against the helper's parent, in the parent's frame, and the helper's predicted local.
    Quaternion Predict(int s, int d, bool twist, float w, Quaternion[] M)
    {
        int p = bb.Parent[s];
        var rel = Quaternion.Inverse(M[p]) * M[d];
        var rel0 = Quaternion.Inverse(restM[p]) * restM[d];
        var delta = Quaternion.Normalize(rel * Quaternion.Inverse(rel0));
        if (twist) delta = TwistAbout(delta, Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rel0)));
        return Quaternion.Normalize(Pow(delta, w) * bb.R[s]);
    }

    public static bool IsHelper(string n) => n.StartsWith("Support_");

    public void Fit(IEnumerable<BbAnim> anims, Action<string> log)
    {
        // Sample model rotations from the game's animations.
        var frames = new List<Quaternion[]>();
        foreach (var a in anims)
            for (int f = 0; f < a.Frames; f += 2)
            {
                var (_, R) = Retarget.Model(bb.Parent,
                    i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? a.Pose[f, t].Translation : bb.T[i]; },
                    i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? Quaternion.Normalize(a.Pose[f, t].Rotation) : bb.R[i]; });
                frames.Add(R);
            }
        int n = bb.Names.Length;
        // Hierarchy distance for choosing nearby drivers.
        int Dist(int a, int b)
        {
            var up = new Dictionary<int, int>(); int x = a, k = 0;
            while (x >= 0) { up[x] = k++; x = bb.Parent[x]; }
            x = b; k = 0;
            while (x >= 0) { if (up.TryGetValue(x, out var ka)) return ka + k; x = bb.Parent[x]; k++; }
            return 99;
        }
        for (int s = 0; s < n; s++)
        {
            if (!IsHelper(bb.Names[s])) continue;
            var drivers = Enumerable.Range(0, n).Where(d => d != s && d != bb.Parent[s] && !IsHelper(bb.Names[d]) && !bb.Names[d].Contains("Finger") && Retarget.Map.ContainsKey(bb.Names[d]) && Dist(s, d) <= 3).ToList();
            Rule best = new(s, -1, false, 0, float.MaxValue);
            // The rest rule (helper never moves) as the baseline.
            {
                float e = 0; foreach (var M in frames) e += Ang(Quaternion.Inverse(M[bb.Parent[s]]) * M[s], bb.R[s]);
                best = best with { Err = e / frames.Count };
            }
            foreach (var d in drivers)
                foreach (var twist in new[] { false, true })
                    for (int wi = -20; wi <= 20; wi++)
                    {
                        float w = wi * 0.05f; if (w == 0) continue;
                        float e = 0;
                        foreach (var M in frames) e += Ang(Quaternion.Inverse(M[bb.Parent[s]]) * M[s], Predict(s, d, twist, w, M));
                        e /= frames.Count;
                        if (e < best.Err) best = new Rule(s, d, twist, w, e);
                    }
            Rules.Add(best);
            log($"  helper {bb.Names[s],-28} <- {(best.Driver < 0 ? "rest" : bb.Names[best.Driver]),-12} {(best.TwistOnly ? "roll" : "turn")} x{best.W:0.00}  err {best.Err * 57.3f:0.0} deg");
        }
    }

    // Overwrites helper bones (and the unmapped bones below them) in a pose's model rotations.
    public void Apply(Quaternion[] M)
    {
        var byBone = Rules.Where(r => r.Driver >= 0).ToDictionary(r => r.Bone);
        for (int b = 0; b < M.Length; b++)
        {
            int p = bb.Parent[b];
            if (byBone.TryGetValue(b, out var r)) M[b] = Quaternion.Normalize(M[p] * Predict(b, r.Driver, r.TwistOnly, r.W, M));
            else if (p >= 0 && IsHelperChain(b)) M[b] = Quaternion.Normalize(M[p] * bb.R[b]);
        }
    }

    bool IsHelperChain(int b) { for (int x = bb.Parent[b]; x >= 0; x = bb.Parent[x]) if (IsHelper(bb.Names[x])) return !Retarget.Map.ContainsKey(bb.Names[b]); return false; }
}


