using System.Numerics;
using HKX2;
using SoulsAssetPipeline.Animation;

namespace LopForge;

// A Bloodborne animation decoded to local bone transforms per frame (from the player's own anibnd files).
public sealed class BbAnim
{
    public int Frames;
    public float FrameDuration;
    public int Tracks;
    public short[] TrackToBone = Array.Empty<short>();
    public NewBlendableTransform[,] Pose = new NewBlendableTransform[0, 0];   // [frame, track]
    public List<Vector4> RootMotion = new();
    public Vector4 Up, Forward;

    public static BbAnim Read(byte[] hkx)
    {
        var root = BbHavok.Read(hkx);
        var an = BbHavok.Find<hkaAnimation>(root) ?? throw new InvalidDataException("no animation");
        var bi = BbHavok.Find<hkaAnimationBinding>(root);
        var r = new BbAnim { Tracks = an.m_numberOfTransformTracks, TrackToBone = bi?.m_transformTrackToBoneIndices.ToArray() ?? Array.Empty<short>() };
        if (an.m_extractedMotion is hkaDefaultAnimatedReferenceFrame rf) { r.RootMotion = rf.m_referenceFrameSamples; r.Up = rf.m_up; r.Forward = rf.m_forward; }
        if (an is hkaSplineCompressedAnimation sc)
        {
            r.Frames = sc.m_numFrames; r.FrameDuration = sc.m_frameDuration;
            var list = SplineCompressedAnimation.ReadSplCmpAnimBytesAndSampleToUncomp(sc.m_data.ToArray(), sc.m_numberOfTransformTracks, sc.m_numBlocks, sc.m_numFrames, sc.m_maxFramesPerBlock);
            r.Pose = new NewBlendableTransform[r.Frames, r.Tracks];
            for (int f = 0; f < r.Frames; f++) for (int t = 0; t < r.Tracks; t++) r.Pose[f, t] = list[f * r.Tracks + t];
        }
        else if (an is hkaInterleavedUncompressedAnimation iu)
        {
            r.Frames = iu.m_transforms.Count / r.Tracks; r.FrameDuration = an.m_duration / Math.Max(1, r.Frames - 1);
            r.Pose = new NewBlendableTransform[r.Frames, r.Tracks];
            for (int f = 0; f < r.Frames; f++)
                for (int t = 0; t < r.Tracks; t++)
                {
                    var m = iu.m_transforms[f * r.Tracks + t];   // HKX2 packs qsTransform as rows: T, R(xyzw), S
                    r.Pose[f, t] = new NewBlendableTransform(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M31, m.M32, m.M33), new Quaternion(m.M21, m.M22, m.M23, m.M24));
                }
        }
        else throw new InvalidDataException("unsupported animation type " + an.GetType().Name);
        return r;
    }
}
