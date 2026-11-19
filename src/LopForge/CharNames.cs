using System.Text;

namespace LopForge;

// Bloodborne only plays animations whose names are listed in the Hunter's character file
// (c0000.anibnd: Action\c0000\Export\Characters\c0000.hkx, hkbCharacterStringData animation names).
// This appends names to that list by editing the Havok 2014 packfile's __data__ section directly:
// a new, longer pointer array and the new strings go at the end of the data, and the list's array header
// is pointed at it. Everything else in the file stays byte-for-byte the same.
public static class CharNames
{
    public static List<string> Read(byte[] file, string key = "a000_000000")
    {
        var s = Section.Find(file);
        var (hdr, slots) = s.LocateNames(key);
        return slots.Select(sl => s.Str(s.Local[sl])).ToList();
    }

    public static void Debug(byte[] file, Action<string> log, string keyName = "a000_000000")
    {
        var s = Section.Find(file);
        int at = s.Data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(keyName + "\0"));
        log($"data={s.Data.Length} fixups={s.LocalList.Count} string at {at}");
        foreach (var l in s.LocalList.Where(l => l.dst == (uint)at)) log($"  ptr to string from {l.src}");
        uint slot = s.LocalList.First(l => l.dst == (uint)at).src;
        uint lo = slot, hi = slot;
        while (s.Local.ContainsKey(lo - 8)) lo -= 8;
        while (s.Local.ContainsKey(hi + 8)) hi += 8;
        log($"  slot block {lo}..{hi} ({(hi - lo) / 8 + 1} slots); first strings: {s.Str(s.Local[lo])}, {s.Str(s.Local[lo + 8])} ... last {s.Str(s.Local[hi])}");
        foreach (var l in s.LocalList.Where(l => l.dst >= lo - 64 && l.dst <= hi)) log($"  fixup {l.src} -> {l.dst}  size@+8={BitConverter.ToInt32(s.Data, (int)l.src + 8)} cap={BitConverter.ToUInt32(s.Data, (int)l.src + 12):X8}");
    }

    public static byte[] Add(byte[] file, IEnumerable<string> names, string key = "a000_000000")
    {
        var s = Section.Find(file);
        var (hdr, slots) = s.LocateNames(key);
        var have = new HashSet<string>(slots.Select(sl => s.Str(s.Local[sl])));
        var add = names.Where(n => have.Add(n)).ToList();
        if (add.Count == 0) return file;

        var data = new List<byte>(s.Data);
        void Align(int a) { while (data.Count % a != 0) data.Add(0); }
        Align(16);
        int arr = data.Count;
        int count = slots.Count + add.Count;
        data.AddRange(new byte[count * 8]);
        Align(16);
        // The old array is left unreferenced (its pointers no longer fixed up).
        var oldSlots = new HashSet<uint>(slots);
        var fix = s.LocalList.Where(l => !oldSlots.Contains(l.src)).ToList();
        // The array header now points at the new array.
        for (int i = 0; i < fix.Count; i++) if (fix[i].src == hdr) fix[i] = (hdr, (uint)arr);
        for (int i = 0; i < slots.Count; i++) fix.Add(((uint)(arr + i * 8), s.Local[slots[i]]));
        for (int i = 0; i < add.Count; i++)
        {
            int str = data.Count;
            data.AddRange(Encoding.ASCII.GetBytes(add[i])); data.Add(0);
            Align(2);
            fix.Add(((uint)(arr + (slots.Count + i) * 8), (uint)str));
        }
        Align(16);
        var d = data.ToArray();
        // Size and capacity (keep the capacity's flag bits).
        BitConverter.GetBytes(count).CopyTo(d, (int)hdr + 8);
        uint cap = BitConverter.ToUInt32(d, (int)hdr + 12);
        BitConverter.GetBytes((uint)count | (cap & 0xC0000000)).CopyTo(d, (int)hdr + 12);
        return s.Rebuild(file, d, fix);
    }

    sealed class Section
    {
        public int HeaderPos, Abs;
        public uint LocOff, GlobOff, VirtOff, ExpOff, ImpOff, EndOff;
        public byte[] Data = Array.Empty<byte>();
        public List<(uint src, uint dst)> LocalList = new();
        public Dictionary<uint, uint> Local = new();

        public static Section Find(byte[] f)
        {
            var tag = Encoding.ASCII.GetBytes("__data__");
            for (int p = 0x40; p < Math.Min(f.Length, 0x400); p++)
            {
                if (!f.AsSpan(p, tag.Length).SequenceEqual(tag)) continue;
                var s = new Section { HeaderPos = p };
                int o = p + 20;
                s.Abs = BitConverter.ToInt32(f, o);
                s.LocOff = BitConverter.ToUInt32(f, o + 4); s.GlobOff = BitConverter.ToUInt32(f, o + 8);
                s.VirtOff = BitConverter.ToUInt32(f, o + 12); s.ExpOff = BitConverter.ToUInt32(f, o + 16);
                s.ImpOff = BitConverter.ToUInt32(f, o + 20); s.EndOff = BitConverter.ToUInt32(f, o + 24);
                if (s.Abs + s.EndOff != f.Length) throw new InvalidDataException("__data__ is not the last section");
                s.Data = f.AsSpan(s.Abs, (int)s.LocOff).ToArray();
                for (uint q = s.LocOff; q + 8 <= s.GlobOff; q += 8)
                {
                    uint src = BitConverter.ToUInt32(f, s.Abs + (int)q);
                    if (src == 0xFFFFFFFF) continue;
                    uint dst = BitConverter.ToUInt32(f, s.Abs + (int)q + 4);
                    s.LocalList.Add((src, dst)); s.Local[src] = dst;
                }
                return s;
            }
            throw new InvalidDataException("no __data__ section");
        }

        public string Str(uint off)
        {
            int e = (int)off; while (e < Data.Length && Data[e] != 0) e++;
            return Encoding.ASCII.GetString(Data, (int)off, e - (int)off);
        }

        // The pointer array whose strings are the animation names (it holds "a000_000000").
        public (uint hdr, List<uint> slots) LocateNames(string keyName)
        {
            var key = Encoding.ASCII.GetBytes(keyName + "\0");
            int at = Data.AsSpan().IndexOf(key);
            if (at < 0) throw new InvalidDataException("animation names not found");
            uint slot = LocalList.First(l => l.dst == (uint)at).src;
            uint lo = slot, hi = slot;
            while (Local.ContainsKey(lo - 8)) lo -= 8;
            while (Local.ContainsKey(hi + 8)) hi += 8;
            // The array start is the slot some array header points at; the array may be a run inside a longer block.
            var heads = LocalList.Where(l => l.dst >= lo && l.dst <= slot && (l.dst - lo) % 8 == 0).ToList();
            foreach (var h in heads.OrderByDescending(h => h.dst))
            {
                int size = BitConverter.ToInt32(Data, (int)h.src + 8);
                if (size <= 0 || h.dst + (uint)size * 8 <= slot) continue;
                var slots = Enumerable.Range(0, size).Select(i => h.dst + (uint)i * 8).ToList();
                if (slots.All(Local.ContainsKey)) return (h.src, slots);
            }
            throw new InvalidDataException("animation name list header not found");
        }

        public byte[] Rebuild(byte[] f, byte[] data, List<(uint src, uint dst)> fix)
        {
            var o = new List<byte>(f.AsSpan(0, Abs).ToArray());
            o.AddRange(data);
            uint loc = (uint)data.Length;
            foreach (var (src, dst) in fix) { o.AddRange(BitConverter.GetBytes(src)); o.AddRange(BitConverter.GetBytes(dst)); }
            while ((o.Count - Abs) % 16 != 0) o.Add(0xFF);
            uint glob = (uint)(o.Count - Abs);
            o.AddRange(f.AsSpan(Abs + (int)GlobOff, (int)(EndOff - GlobOff)).ToArray());
            uint shift = glob - GlobOff;
            var r = o.ToArray();
            int h = HeaderPos + 20;
            BitConverter.GetBytes(loc).CopyTo(r, h + 4);
            BitConverter.GetBytes(glob).CopyTo(r, h + 8);
            BitConverter.GetBytes(VirtOff + shift).CopyTo(r, h + 12);
            BitConverter.GetBytes(ExpOff + shift).CopyTo(r, h + 16);
            BitConverter.GetBytes(ImpOff + shift).CopyTo(r, h + 20);
            BitConverter.GetBytes(EndOff + shift).CopyTo(r, h + 24);
            return r;
        }
    }
}


