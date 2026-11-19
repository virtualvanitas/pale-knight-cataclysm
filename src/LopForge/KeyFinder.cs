using System.Security.Cryptography;
using System.Text;

namespace LopForge;

// Finds the pak AES key inside the player's own Lies of P executable (the same byte patterns
// AESDumpster scans for: a key built with eight 32-bit immediate moves), then proves each
// candidate by decrypting the start of the pak index, which must begin with the mount point.
public static class KeyFinder
{
    static readonly (string pattern, int[] offsets)[] Patterns =
    {
        ("C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?", new[] { 3, 10, 17, 24, 35, 42, 49, 56 }),
        ("C7 ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?", new[] { 2, 9, 16, 23, 30, 37, 44, 51 }),
        ("C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? 48 ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ?", new[] { 3, 10, 21, 28, 35, 42, 49, 56 }),
        ("C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? ? C7 ? ? ? ? ? C3", new[] { 51, 45, 38, 31, 24, 17, 10, 3 }),
    };

    public static byte[]? Find(string exePath, string pakPath)
    {
        var (idx0, ok) = ReadIndexHead(pakPath);
        if (!ok) throw new InvalidDataException("pak footer not found: " + pakPath);
        var exe = File.ReadAllBytes(exePath);
        var seen = new HashSet<string>();
        foreach (var (pattern, offsets) in Patterns)
        {
            var pat = pattern.Split(' ').Select(s => s == "?" ? -1 : Convert.ToInt32(s, 16)).ToArray();
            int last = exe.Length - pat.Length;
            for (int i = 0; i < last; i++)
            {
                if (exe[i] != 0xC7) continue;
                bool m = true;
                for (int j = 1; j < pat.Length; j++)
                    if (pat[j] >= 0 && exe[i + j] != pat[j]) { m = false; break; }
                if (!m) continue;
                var key = new byte[32];
                for (int k = 0; k < 8; k++) Buffer.BlockCopy(exe, i + offsets[k], key, k * 4, 4);
                var hex = Convert.ToHexString(key);
                if (!seen.Add(hex)) continue;
                if (Proves(key, idx0)) return key;
            }
        }
        return null;
    }

    static bool Proves(byte[] key, byte[] head)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var plain = aes.DecryptEcb(head, PaddingMode.None);
        int len = BitConverter.ToInt32(plain, 0);
        if (len < 4 || len > 512) return false;
        var s = Encoding.ASCII.GetString(plain, 4, Math.Min(9, plain.Length - 4));
        return s.StartsWith("../");
    }

    // Returns the first 32 encrypted bytes of the pak index.
    static (byte[], bool) ReadIndexHead(string pak)
    {
        using var fs = File.OpenRead(pak);
        int n = 300;
        fs.Seek(-n, SeekOrigin.End);
        var b = new byte[n];
        fs.ReadExactly(b);
        for (int i = 0; i < n - 4; i++)
        {
            if (BitConverter.ToUInt32(b, i) != 0x5A6F12E1) continue;
            long off = BitConverter.ToInt64(b, i + 8);
            fs.Seek(off, SeekOrigin.Begin);
            var head = new byte[32];
            fs.ReadExactly(head);
            return (head, true);
        }
        return (Array.Empty<byte>(), false);
    }
}
