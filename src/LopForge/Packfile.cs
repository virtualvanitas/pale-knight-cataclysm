using System.Text;

namespace LopForge;

// Minimal Havok 2014 packfile (64-bit, little endian) editor for the __data__ section: objects (virtual fixups),
// pointers to objects (global fixups) and owned data (local fixups). Lets us clone objects and grow arrays
// without a full class model, keeping every untouched byte as it was.
public sealed class Packfile
{
    public byte[] File;
    public int DataHeader, DataAbs;
    public uint LocOff, GlobOff, VirtOff, ExpOff, ImpOff, EndOff;
    public List<byte> Data;
    public List<(uint src, uint dst)> Local = new();
    public List<(uint src, uint sec, uint dst)> Global = new();
    public List<(uint src, uint sec, uint name)> Virtual = new();
    public byte[] ClassNames = Array.Empty<byte>();
    public int DataSectionIndex;

    public Packfile(byte[] f)
    {
        File = f;
        int secCount = BitConverter.ToInt32(f, 0x14);
        int p = FindTag(f, "__classnames__");
        int first = p;
        for (int i = 0; i < secCount; i++)
        {
            int h = first + i * 64;
            var tag = Encoding.ASCII.GetString(f, h, 19).TrimEnd('\0');
            int abs = BitConverter.ToInt32(f, h + 20);
            if (tag == "__classnames__") { uint loc = BitConverter.ToUInt32(f, h + 24); ClassNames = f.AsSpan(abs, (int)loc).ToArray(); }
            if (tag == "__data__")
            {
                DataSectionIndex = i; DataHeader = h; DataAbs = abs;
                LocOff = BitConverter.ToUInt32(f, h + 24); GlobOff = BitConverter.ToUInt32(f, h + 28); VirtOff = BitConverter.ToUInt32(f, h + 32);
                ExpOff = BitConverter.ToUInt32(f, h + 36); ImpOff = BitConverter.ToUInt32(f, h + 40); EndOff = BitConverter.ToUInt32(f, h + 44);
            }
        }
        if (DataAbs + EndOff != f.Length) throw new InvalidDataException("__data__ is not last");
        Data = new List<byte>(f.AsSpan(DataAbs, (int)LocOff).ToArray());
        for (uint q = LocOff; q + 8 <= GlobOff; q += 8) { uint s = U(DataAbs + q); if (s != 0xFFFFFFFF) Local.Add((s, U(DataAbs + q + 4))); }
        for (uint q = GlobOff; q + 12 <= VirtOff; q += 12) { uint s = U(DataAbs + q); if (s != 0xFFFFFFFF) Global.Add((s, U(DataAbs + q + 4), U(DataAbs + q + 8))); }
        for (uint q = VirtOff; q + 12 <= ExpOff; q += 12) { uint s = U(DataAbs + q); if (s != 0xFFFFFFFF) Virtual.Add((s, U(DataAbs + q + 4), U(DataAbs + q + 8))); }
    }

    uint U(long at) => BitConverter.ToUInt32(File, (int)at);
    static int FindTag(byte[] f, string t) { var b = Encoding.ASCII.GetBytes(t); for (int i = 0; i < 0x400; i++) if (f.AsSpan(i, b.Length).SequenceEqual(b)) return i; throw new InvalidDataException("no " + t); }

    public string ClassName(uint nameOff) { int e = (int)nameOff; while (ClassNames[e] != 0) e++; return Encoding.ASCII.GetString(ClassNames, (int)nameOff, e - (int)nameOff); }
    public string Str(uint off) { int e = (int)off; while (Data[e] != 0) e++; return Encoding.ASCII.GetString(Data.GetRange((int)off, e - (int)off).ToArray()); }
    public int I32(uint off) => BitConverter.ToInt32(Data.GetRange((int)off, 4).ToArray());
    public void SetI32(uint off, int v) { var b = BitConverter.GetBytes(v); for (int i = 0; i < 4; i++) Data[(int)off + i] = b[i]; }

    // Objects in data order with their class and extent (to the next object).
    public List<(uint start, uint end, string cls, uint nameOff)> Objects()
    {
        var v = Virtual.OrderBy(x => x.src).ToList();
        var r = new List<(uint, uint, string, uint)>();
        for (int i = 0; i < v.Count; i++)
        {
            uint end = i + 1 < v.Count ? v[i + 1].src : (uint)Data.Count;
            r.Add((v[i].src, end, ClassName(v[i].name), v[i].name));
        }
        return r;
    }

    public void Align(int a) { while (Data.Count % a != 0) Data.Add(0); }

    public uint AddString(string s) { uint at = (uint)Data.Count; Data.AddRange(Encoding.ASCII.GetBytes(s)); Data.Add(0); Align(2); return at; }

    public byte[] Write()
    {
        Align(16);
        var o = new List<byte>(File.AsSpan(0, DataAbs).ToArray());
        o.AddRange(Data);
        uint loc = (uint)Data.Count;
        foreach (var (s, d) in Local.OrderBy(x => x.src)) { o.AddRange(BitConverter.GetBytes(s)); o.AddRange(BitConverter.GetBytes(d)); }
        while ((o.Count - DataAbs) % 16 != 0) o.Add(0xFF);
        uint glob = (uint)(o.Count - DataAbs);
        foreach (var (s, sec, d) in Global.OrderBy(x => x.src)) { o.AddRange(BitConverter.GetBytes(s)); o.AddRange(BitConverter.GetBytes(sec)); o.AddRange(BitConverter.GetBytes(d)); }
        while ((o.Count - DataAbs) % 16 != 0) o.Add(0xFF);
        uint virt = (uint)(o.Count - DataAbs);
        foreach (var (s, sec, n) in Virtual.OrderBy(x => x.src)) { o.AddRange(BitConverter.GetBytes(s)); o.AddRange(BitConverter.GetBytes(sec)); o.AddRange(BitConverter.GetBytes(n)); }
        for (int i = 0; i < 16; i++) o.Add(0xFF);   // as Bloodborne's own files end their fixup tables
        while ((o.Count - DataAbs) % 16 != 0) o.Add(0xFF);
        uint exp = (uint)(o.Count - DataAbs);
        // exports/imports are empty in these files
        if (ImpOff != ExpOff || EndOff != ImpOff) throw new InvalidDataException("exports/imports not supported");
        var r = o.ToArray();
        int h = DataHeader;
        BitConverter.GetBytes(loc).CopyTo(r, h + 24); BitConverter.GetBytes(glob).CopyTo(r, h + 28); BitConverter.GetBytes(virt).CopyTo(r, h + 32);
        BitConverter.GetBytes(exp).CopyTo(r, h + 36); BitConverter.GetBytes(exp).CopyTo(r, h + 40); BitConverter.GetBytes(exp).CopyTo(r, h + 44);
        return r;
    }
}
