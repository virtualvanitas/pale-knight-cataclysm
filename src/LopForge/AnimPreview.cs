using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LopForge;

// Dev check: a Bloodborne animation (ours or the game's) drawn as a stick figure, front and side, every few frames.
public static class AnimPreview
{
    public static void Render(BbAnim a, Retarget.BbSkel bb, string outPng, int cols = 8)
    {
        int shots = Math.Min(cols * 2, a.Frames);
        int cell = 220, W = cols * cell, H = ((shots + cols - 1) / cols) * cell * 2;
        using var img = new Image<Rgba32>(W, H, new Rgba32(30, 30, 36, 255));
        for (int s = 0; s < shots; s++)
        {
            int f = (int)Math.Round(s * (a.Frames - 1) / (double)Math.Max(1, shots - 1));
            var (P, _) = Retarget.Model(bb.Parent,
                i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? a.Pose[f, t].Translation : bb.T[i]; },
                i => { int t = Array.IndexOf(a.TrackToBone, (short)i); return t >= 0 ? a.Pose[f, t].Rotation : bb.R[i]; });
            var rmv = f < a.RootMotion.Count ? new Vector3(a.RootMotion[f].X, a.RootMotion[f].Y, a.RootMotion[f].Z) : Vector3.Zero;
            for (int view = 0; view < 2; view++)
            {
                int ox = (s % cols) * cell + cell / 2, oy = (s / cols) * cell * 2 + view * cell + cell - 12;
                float sc = 95;
                Vector2 Pr(Vector3 p) { p += rmv * 0.3f; return view == 0 ? new(ox + p.X * sc, oy - p.Y * sc) : new(ox + p.Z * sc, oy - p.Y * sc); }
                for (int b = 1; b < P.Length; b++)
                {
                    int pa = bb.Parent[b]; if (pa < 0) continue;
                    var n = bb.Names[b];
                    if (n.Contains("Support") || n.Contains("Target") || n.Contains("Case") || n.StartsWith("Root") || n.StartsWith("Upper")) continue;
                    var c = n.StartsWith("L_") ? new Rgba32(90, 170, 255) : n.StartsWith("R_") ? new Rgba32(255, 120, 90) : new Rgba32(220, 220, 220);
                    if (n.Contains("Weapon")) c = new Rgba32(255, 230, 60);
                    Line(img, Pr(P[pa]), Pr(P[b]), c);
                }
                Line(img, new Vector2(ox - 90, oy), new Vector2(ox + 90, oy), new Rgba32(70, 70, 80));
            }
        }
        img.SaveAsPng(outPng);
    }

    internal static void Line(Image<Rgba32> img, Vector2 a, Vector2 b, Rgba32 c)
    {
        int n = (int)MathF.Max(1, Vector2.Distance(a, b));
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            int x = (int)p.X, y = (int)p.Y;
            if (x >= 0 && y >= 0 && x < img.Width && y < img.Height) img[x, y] = c;
        }
    }
}

