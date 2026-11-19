using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using System.Numerics;

namespace LopForge;

// A Lies of P animation, sampled to local bone transforms per frame, from the player's archives.
public sealed class LopAnim
{
    public sealed class Track { public string Bone = ""; public Vector3[] T = Array.Empty<Vector3>(); public Quaternion[] R = Array.Empty<Quaternion>(); }
    public int Frames;
    public float Fps;
    public string Skeleton = "";
    public List<Track> Tracks = new();
    public List<string> Notifies = new();
    // The skeleton's own bones (all of them, not only the animated ones): name, parent, rest pose.
    public List<(string name, int parent, Vector3 t, Quaternion r)> RefBones = new();

    public static List<(string name, int parent, Vector3 t, Quaternion r)> RefPose(USkeleton skel)
    {
        var rs = skel.ReferenceSkeleton; var list = new List<(string, int, Vector3, Quaternion)>();
        for (int b = 0; b < rs.FinalRefBoneInfo.Length; b++)
        {
            var p = rs.FinalRefBonePose[b];
            list.Add((rs.FinalRefBoneInfo[b].Name.Text, rs.FinalRefBoneInfo[b].ParentIndex, new Vector3(p.Translation.X, p.Translation.Y, p.Translation.Z), new Quaternion(p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W)));
        }
        return list;
    }

    public static LopAnim Load(IFileProvider p, string path)
    {
        var seq = p.LoadPackageObject<UAnimSequence>(path + "." + Path.GetFileName(path));
        var skel = seq.Skeleton.Load<USkeleton>()!;
        var anim = new LopAnim { Skeleton = skel.GetPathName(), RefBones = RefPose(skel) };
        var ca = skel.ConvertAnims(seq);
        var s = ca.Sequences[0];
        anim.Frames = s.NumFrames;
        anim.Fps = s.FramesPerSecond;
        var rs = skel.ReferenceSkeleton;
        for (int b = 0; b < s.Tracks.Count && b < rs.FinalRefBoneInfo.Length; b++)
        {
            var tr = s.Tracks[b];
            var t = new Track { Bone = rs.FinalRefBoneInfo[b].Name.Text, T = new Vector3[s.NumFrames], R = new Quaternion[s.NumFrames] };
            var refPose = rs.FinalRefBonePose[b];
            for (int f = 0; f < s.NumFrames; f++)
            {
                var pos = refPose.Translation; var rot = refPose.Rotation; var scl = refPose.Scale3D;
                tr.GetBoneTransform(f, s.NumFrames, ref rot, ref pos, ref scl);
                t.T[f] = new Vector3(pos.X, pos.Y, pos.Z);
                t.R[f] = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
            }
            anim.Tracks.Add(t);
        }
        if (seq.Notifies != null)
            foreach (var n in seq.Notifies)
                anim.Notifies.Add($"{n.LinkValue:0.000}s {n.NotifyName} {n.Notify?.Name ?? n.NotifyStateClass?.Name}");
        return anim;
    }
}
