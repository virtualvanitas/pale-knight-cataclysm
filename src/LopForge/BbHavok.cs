using HKX2;
using SoulsFormats;

namespace LopForge;

// Reads and writes Bloodborne's Havok 2014 packfiles (skeleton and animations).
public static class BbHavok
{
    public static hkRootLevelContainer Read(byte[] bytes)
    {
        var des = new PackFileDeserializer();
        return (hkRootLevelContainer)des.Deserialize(new BinaryReaderEx(false, bytes));
    }

    public static byte[] Write(hkRootLevelContainer root)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriterEx(false, ms);
        new PackFileSerializer().Serialize(root, bw);
        bw.Finish();
        return ms.ToArray();
    }

    public static T? Find<T>(hkRootLevelContainer root) where T : class
    {
        foreach (var v in root.m_namedVariants)
        {
            if (v.m_variant is T t) return t;
            if (v.m_variant is hkaAnimationContainer ac)
            {
                if (typeof(T) == typeof(hkaSkeleton) && ac.m_skeletons.Count > 0) return ac.m_skeletons[0] as T;
                if (typeof(T) == typeof(hkaAnimationBinding) && ac.m_bindings.Count > 0) return ac.m_bindings[0] as T;
                if (typeof(T).IsAssignableTo(typeof(hkaAnimation)) && ac.m_animations.Count > 0) return ac.m_animations[0] as T;
            }
        }
        return null;
    }
}
