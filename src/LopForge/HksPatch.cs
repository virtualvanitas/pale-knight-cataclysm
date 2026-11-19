using System.Buffers.Binary;
using System.Text;

namespace LopForge;

// The Hunter's control script (action/script/c0000.hks, compiled HavokScript) only lets a left-hand weapon fire when its
// moveset number is one of Bloodborne's guns: (24, 31, 41, 128, 129, 130, 152, 153) in Act_WeaponAttack. The script is
// read from the player's own copy and its one "== 152" test there becomes ">= 152", so the left hand fires for 152 (Church
// Cannon), 153 (Piercing Rifle) and our 161 (Cataclysm). No other left-hand weapon has a number of 152 or more.
public static class HksPatch
{
    public sealed class Fn
    {
        public int CodeOffset, CodeCount;
        public List<(byte type, float num, string? str)> Consts = new();
        public string? Name;
    }

    public static List<Fn> Walk(byte[] b)
    {
        var list = new List<Fn>();
        if (b.Length < 0xF0 || b[0] != 0x1B || b[1] != (byte)'L' || b[4] != 0x51 || b[5] != 0x0E) throw new InvalidDataException("not a HavokScript file");
        int numSize = b[10];
        int p = 0xEE;
        int I32() { int v = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p)); p += 4; return v; }
        string? Str()
        {
            long len = (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(p)); p += 8;
            if (len <= 0) return null;
            var s = Encoding.UTF8.GetString(b, p, (int)len - 1); p += (int)len; return s;
        }
        void Func()
        {
            var f = new Fn();
            I32(); I32(); p += 1; I32(); I32();
            int count = I32();
            p = (p + 3) & ~3;
            f.CodeOffset = p; f.CodeCount = count; p += count * 4;
            int nk = I32();
            for (int i = 0; i < nk; i++)
            {
                byte t = b[p++];
                switch (t)
                {
                    case 0: f.Consts.Add((t, 0, null)); break;
                    case 1: f.Consts.Add((t, b[p++], null)); break;
                    case 3:
                        float v = numSize == 8 ? (float)BinaryPrimitives.ReadDoubleBigEndian(b.AsSpan(p)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(p));
                        p += numSize; f.Consts.Add((t, v, null)); break;
                    case 4: f.Consts.Add((t, 0, Str())); break;
                    default: throw new InvalidDataException($"HavokScript constant type {t} at {p - 1}");
                }
            }
            I32(); I32();
            int locals = I32(), upvals = I32();
            I32(); I32();
            Str(); f.Name = Str();
            p += count * 4;
            for (int i = 0; i < locals; i++) { Str(); I32(); I32(); }
            for (int i = 0; i < upvals; i++) Str();
            list.Add(f);
            int kids = I32();
            for (int i = 0; i < kids; i++) Func();
        }
        Func();
        return list;
    }

    const int OpEq = 4, OpLt = 56, OpLtBk = 57, OpLoadK = 25;

    // Act_LeftWeaponAddAnime sets LeftWeaponAdd (the left hand's gun-holding layer the gun-shot states blend on) from
    // the left weapon's moveset: 24/128/129 -> 1 (pistols), 41/130/153 -> 2, 31 -> 3, 151 -> 4, 32 -> 0, else 100
    // (none: a gun shot then plays nothing and never ends). Its second "== 151" branch can never be reached (the
    // first one catches 151), so it becomes "> 153 -> 1": Cataclysm (161, built on the pistol moveset a129) gets the
    // pistol layer. No other left-hand weapon has a number above 153.
    public static int GiveLeftGunLayer(byte[] b, Action<string> log)
    {
        int changed = 0;
        foreach (var f in Walk(b))
        {
            int K(float v) => f.Consts.FindIndex(k => k.type == 3 && k.num == v);
            bool HasStr(string s) => f.Consts.Any(k => k.type == 4 && k.str == s);
            if (!HasStr("LeftWeaponAdd")) continue;
            int k151 = K(151), k153 = K(153), k4 = K(4), k1 = K(1);
            if (k151 < 0 || k153 < 0 || k4 < 0 || k1 < 0 || K(129) < 0 || K(32) < 0) continue;
            var eqs = new List<int>();
            for (int i = 0; i < f.CodeCount; i++)
            {
                uint ins = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(f.CodeOffset + i * 4));
                int c = (int)((ins >> 8) & 0x1FF);
                if ((int)(ins >> 25) == OpEq && (c & 0x100) != 0 && (c & 0xFF) == k151) eqs.Add(i);
            }
            if (eqs.Count != 2) { log($"control script: LeftWeaponAdd has {eqs.Count} tests of 151, left unchanged"); continue; }
            int at = f.CodeOffset + eqs[1] * 4;
            uint eq = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
            uint a = eq & 0xFF, reg = (eq >> 17) & 0xFF;
            // EQ(a, R, K151) -> LT_BK(a, K153, R): skip unless (153 < R) == a
            uint lt = ((uint)OpLtBk << 25) | ((uint)k153 << 17) | (reg << 8) | a;
            // ...and the value it sets: the first LOADK of 4 after it becomes 1.
            int load = -1;
            for (int i = eqs[1] + 1; i < Math.Min(f.CodeCount, eqs[1] + 10); i++)
            {
                uint ins = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(f.CodeOffset + i * 4));
                if ((int)(ins >> 25) == OpLoadK && (int)((ins >> 8) & 0x1FFFF) == k4) { load = i; break; }
            }
            if (load < 0) { log("control script: LeftWeaponAdd value not found, left unchanged"); continue; }
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), lt);
            int lat = f.CodeOffset + load * 4;
            uint li = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(lat));
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(lat), (li & 0xFE0000FF) | ((uint)k1 << 8));
            changed++;
        }
        log(changed > 0 ? "control script: Cataclysm holds like a pistol (LeftWeaponAdd)" : "control script: LeftWeaponAdd not changed");
        return changed;
    }

    // Returns how many tests were changed (0 = the script isn't the one this was made for; left as it is).
    public static int AllowLeftGun(byte[] b, Action<string> log)
    {
        var fns = Walk(b);
        int changed = 0;
        foreach (var f in fns)
        {
            bool Has(float v) => f.Consts.Any(k => k.type == 3 && k.num == v);
            // The left-hand gun test: a function holding every gun number, 152 among them.
            if (!(Has(152) && Has(153) && Has(129) && Has(41) && Has(31) && Has(24))) continue;
            var k152 = Enumerable.Range(0, f.Consts.Count).Where(i => f.Consts[i].type == 3 && f.Consts[i].num == 152).ToHashSet();
            for (int i = 0; i < f.CodeCount; i++)
            {
                int at = f.CodeOffset + i * 4;
                uint ins = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
                int op = (int)(ins >> 25);
                if (op != OpEq) continue;
                int c = (int)((ins >> 8) & 0x1FF);
                if ((c & 0x100) == 0 || !k152.Contains(c & 0xFF)) continue;
                uint a = ins & 0xFF;
                // EQ(a, R, K152): skip unless (R == 152) == a  ->  LT(!a, R, K152): skip unless (R >= 152) == a
                uint patched = ((uint)OpLt << 25) | (ins & 0x01FFFF00) | (a ^ 1);
                BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), patched);
                changed++;
            }
        }
        log(changed > 0 ? $"control script: left-hand guns now include Cataclysm ({changed} test)" : "control script: left-hand gun test not found, left unchanged");
        return changed;
    }
}
