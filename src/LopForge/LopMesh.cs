using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Meshes;
using System.Numerics;

namespace LopForge;

// A Lies of P skinned mesh, read from the player's own archives, in plain numbers.
public sealed class LopMesh
{
    public sealed class Bone { public string Name = ""; public int Parent; public Vector3 T; public Quaternion R; public Vector3 S; }
    public sealed class Vert { public Vector3 P; public Vector3 N; public Vector4 Tan; public Vector2 Uv; public int[] Bi = new int[4]; public float[] Bw = new float[4]; }
    public sealed class Section { public int Material; public int[] Tris = Array.Empty<int>(); }

    public List<Bone> Bones = new();
    public List<Vert> Verts = new();
    public List<Section> Sections = new();
    public List<string> Materials = new();

    public static LopMesh Load(IFileProvider p, string path)
    {
        var sk = p.LoadPackageObject<USkeletalMesh>(path + "." + Path.GetFileName(path));
        var m = new LopMesh();
        var rs = sk.ReferenceSkeleton;
        for (int i = 0; i < rs.FinalRefBoneInfo.Length; i++)
        {
            var t = rs.FinalRefBonePose[i];
            m.Bones.Add(new Bone
            {
                Name = rs.FinalRefBoneInfo[i].Name.Text,
                Parent = rs.FinalRefBoneInfo[i].ParentIndex,
                T = new Vector3(t.Translation.X, t.Translation.Y, t.Translation.Z),
                R = new Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W),
                S = new Vector3(t.Scale3D.X, t.Scale3D.Y, t.Scale3D.Z),
            });
        }
        if (!sk.TryConvert(out SkeletalMeshDto dto)) throw new InvalidDataException("mesh convert failed: " + path);
        var lod = dto.LODs[0];
        foreach (var v in lod.Vertices)
        {
            var x = new Vert
            {
                P = new Vector3(v.Position.X, v.Position.Y, v.Position.Z),
                N = new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z),
                Tan = new Vector4(v.Tangent.X, v.Tangent.Y, v.Tangent.Z, v.Tangent.W),
                Uv = new Vector2(v.Uv.U, v.Uv.V),
            };
            var inf = v.Influences;
            for (int k = 0; k < 4 && k < inf.Length; k++) { x.Bi[k] = inf[k].Bone; x.Bw[k] = inf[k].Weight; }
            m.Verts.Add(x);
        }
        foreach (var s in lod.Sections)
        {
            var tris = new int[s.NumFaces * 3];
            for (int i = 0; i < tris.Length; i++) tris[i] = (int)lod.Indices[s.FirstIndex + i];
            m.Sections.Add(new Section { Material = s.MaterialIndex, Tris = tris });
        }
        foreach (var mat in dto.Materials) m.Materials.Add(mat.Material?.ResolvedObject?.GetPathName() ?? mat.Name);
        return m;
    }
}
