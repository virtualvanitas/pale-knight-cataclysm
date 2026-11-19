using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using SoulsFormats;

namespace LopForge;

// Lies of P PBR textures (BaseColor, ARM = AO/Roughness/Metal, Normal) read from the player's own copy,
// turned into Bloodborne's albedo (_a), reflectance (_r), shininess (_s) and normal (_n) maps.
public static class LopTex
{
    public sealed class Rgba { public int W, H; public byte[] Px = Array.Empty<byte>(); }

    public static Rgba Load(IFileProvider p, string path, int maxSize)
    {
        var tex = p.LoadPackageObject<UTexture2D>(path + "." + Path.GetFileName(path));
        var c = tex.Decode(maxSize, new ExportOptions()) ?? throw new InvalidDataException("texture decode failed: " + path);
        var o = new Rgba { W = c.Width, H = c.Height, Px = new byte[c.Width * c.Height * 4] };
        var f = c.PixelFormat.ToString();
        int n = c.Width * c.Height;
        if (c.Data.Length == n * 4)
        {
            bool bgra = f.Contains("B8G8R8A8");
            for (int i = 0; i < n; i++)
            {
                o.Px[i * 4 + 0] = c.Data[i * 4 + (bgra ? 2 : 0)];
                o.Px[i * 4 + 1] = c.Data[i * 4 + 1];
                o.Px[i * 4 + 2] = c.Data[i * 4 + (bgra ? 0 : 2)];
                o.Px[i * 4 + 3] = c.Data[i * 4 + 3];
            }
        }
        else if (c.Data.Length == n)
        {
            for (int i = 0; i < n; i++) { o.Px[i * 4] = o.Px[i * 4 + 1] = o.Px[i * 4 + 2] = c.Data[i]; o.Px[i * 4 + 3] = 255; }
        }
        else throw new InvalidDataException($"unexpected decoded format {f} for {path}");
        return o;
    }

    // The picture centred on a transparent square of the given size, keeping its proportions (menu icons).
    public static Rgba FitSquare(Rgba s, int size)
    {
        float k = Math.Min(size / (float)s.W, size / (float)s.H);
        int w = Math.Max(1, (int)(s.W * k)), h = Math.Max(1, (int)(s.H * k));
        var r = Resize(s, w, h);
        var o = new Rgba { W = size, H = size, Px = new byte[size * size * 4] };
        int ox = (size - w) / 2, oy = (size - h) / 2;
        for (int y = 0; y < h; y++) Buffer.BlockCopy(r.Px, y * w * 4, o.Px, ((oy + y) * size + ox) * 4, w * 4);
        return o;
    }

    static Rgba Resize(Rgba s, int w, int h)
    {
        if (s.W == w && s.H == h) return s;
        var o = new Rgba { W = w, H = h, Px = new byte[w * h * 4] };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x * s.W / w, sy = y * s.H / h;
                Buffer.BlockCopy(s.Px, (sy * s.W + sx) * 4, o.Px, (y * w + x) * 4, 4);
            }
        return o;
    }

    static float Lin(byte v) { float c = v / 255f; return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f); }
    static byte Srgb(float l) { l = Math.Clamp(l, 0, 1); float c = l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1 / 2.4f) - 0.055f; return (byte)Math.Round(c * 255); }

    // The four Bloodborne maps from Lies of P's metal/roughness set.
    public static (Rgba a, Rgba r, Rgba s, Rgba n) ToBloodborne(Rgba bc, Rgba arm, Rgba nrm, int size)
    {
        int w = Math.Min(size, bc.W), h = Math.Min(size * bc.H / bc.W, bc.H);
        bc = Resize(bc, w, h); arm = Resize(arm, w, h); nrm = Resize(nrm, w, h);
        var a = new Rgba { W = w, H = h, Px = new byte[w * h * 4] };
        var r = new Rgba { W = w, H = h, Px = new byte[w * h * 4] };
        var s = new Rgba { W = w, H = h, Px = new byte[w * h * 4] };
        var n = new Rgba { W = w, H = h, Px = new byte[w * h * 4] };
        for (int i = 0; i < w * h; i++)
        {
            int k = i * 4;
            float ao = arm.Px[k] / 255f, rough = arm.Px[k + 1] / 255f, metal = arm.Px[k + 2] / 255f;
            for (int ch = 0; ch < 3; ch++)
            {
                float lin = Lin(bc.Px[k + ch]);
                a.Px[k + ch] = Srgb(lin * (1 - metal) * (0.35f + 0.65f * ao));          // diffuse colour (metal has none)
                r.Px[k + ch] = Srgb((0.04f * (1 - metal) + lin * metal) * (0.5f + 0.5f * ao)); // specular colour
            }
            a.Px[k + 3] = 255; r.Px[k + 3] = 255;
            byte gloss = (byte)Math.Round(Math.Clamp(1 - rough, 0, 1) * 255);
            s.Px[k] = gloss; s.Px[k + 1] = 0; s.Px[k + 2] = 0; s.Px[k + 3] = 255;
            // Unreal normal maps keep X/Y in R/G (two-channel); rebuild Z.
            float nx = nrm.Px[k] / 127.5f - 1, ny = nrm.Px[k + 1] / 127.5f - 1;
            float nz = MathF.Sqrt(MathF.Max(0, 1 - nx * nx - ny * ny));
            n.Px[k] = nrm.Px[k]; n.Px[k + 1] = nrm.Px[k + 1]; n.Px[k + 2] = (byte)Math.Round((nz * 0.5f + 0.5f) * 255); n.Px[k + 3] = 255;
        }
        return (a, r, s, n);
    }

    // A PS4 TPF texture in the same format as the template's texture of that kind.
    public static TPF.Texture ToTpf(Rgba img, string name, TPF.Texture like, bool mipmaps = true)
    {
        var dxgi = like.Header?.DXGIFormat ?? 71;
        var fmt = dxgi switch
        {
            80 or 81 => CompressionFormat.Bc4,
            83 or 84 => CompressionFormat.Bc5,
            98 or 99 => CompressionFormat.Bc7,
            74 or 75 => CompressionFormat.Bc2,
            77 or 78 => CompressionFormat.Bc3,
            _ => CompressionFormat.Bc1,
        };
        var enc = new BcEncoder();
        enc.OutputOptions.GenerateMipMaps = mipmaps;
        enc.OutputOptions.Quality = CompressionQuality.Balanced;
        enc.OutputOptions.Format = fmt;
        enc.OutputOptions.FileFormat = OutputFileFormat.Dds;
        enc.OutputOptions.DdsBc1WriteAlphaFlag = false;
        using var ms = new MemoryStream();
        enc.EncodeToStream(img.Px, img.W, img.H, PixelFormat.Rgba32, ms);
        var dds = ms.ToArray();
        // The encoder tags BC4/BC5 as "BC4U"/"BC5U"; the TPF packer reads only "ATI1"/"ATI2" (otherwise it writes an empty texture).
        var four = System.Text.Encoding.ASCII.GetString(dds, 84, 4);
        if (four == "BC4U") System.Text.Encoding.ASCII.GetBytes("ATI1").CopyTo(dds, 84);
        if (four == "BC5U") System.Text.Encoding.ASCII.GetBytes("ATI2").CopyTo(dds, 84);
        var t = new TPF.Texture(name, like.Format, like.Flags1, dds, TPF.TPFPlatform.PS4);
        if (t.Bytes.Length == 0) throw new InvalidDataException("texture came out empty: " + name);
        t.Header!.DXGIFormat = dxgi;            // same bits; sRGB vs linear is only how they're read
        t.Header.Unk1 = like.Header!.Unk1;
        t.Header.Unk2 = like.Header.Unk2;
        return t;
    }
}
