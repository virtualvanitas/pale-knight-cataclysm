namespace LopForge;

// The Hunter's behaviour (Action\c0000\Export\Behaviors\c0000.hkx) picks each move through a
// CustomManualSelectorGenerator that holds one hkbClipGenerator per weapon moveset ("a143_032660" etc.) and
// chooses the child whose moveset number is the equipped weapon's wepmotionCategory; a moveset with no child
// falls back to another weapon's. For each new moveset this clones the template moveset's children
// (name "aTTT_xxxxxx_hkx_AutoSet_00" and animation "aTTT_xxxxxx" -> "aNNN_..."), adding them to the same selectors.
public static class BehaviorClips
{
    const int NameAt = 56, AnimAt = 144;

    public static byte[] AddMovesets(byte[] file, IEnumerable<(int cat, int tcat)> pairs, Action<string> log, List<string> addedNames)
    {
        var pf = new Packfile(file);
        var objs = pf.Objects();
        var byStart = objs.ToDictionary(o => o.start);
        var localAt = pf.Local.GroupBy(l => l.src).ToDictionary(g => g.Key, g => g.First().dst);

        string? AnimName(uint clip) => localAt.TryGetValue(clip + AnimAt, out var d) ? pf.Str(d) : null;
        // Selector children arrays: header (hkArray of pointers) in the selector, elements are global fixups.
        var selectors = objs.Where(o => o.cls == "CustomManualSelectorGenerator").ToList();
        var globalsBySrc = pf.Global.GroupBy(g => g.src).ToDictionary(g => g.Key, g => g.First());
        var pairList = pairs.ToList();
        var added = pairList.ToDictionary(p => p.cat, p => 0);

        foreach (var sel in selectors)
        {
            // The generators array header is the first local fixup in the selector whose target holds object pointers.
            uint hdr = 0, arr = 0; int size = 0;
            foreach (var l in pf.Local.Where(l => l.src > sel.start && l.src < sel.end).OrderBy(l => l.src))
            {
                int n = pf.I32(l.src + 8);
                if (n > 0 && n < 4096 && globalsBySrc.ContainsKey(l.dst)) { hdr = l.src; arr = l.dst; size = n; break; }
            }
            if (size == 0) continue;
            var kids = Enumerable.Range(0, size).Select(i => globalsBySrc.TryGetValue(arr + (uint)i * 8, out var g) ? g.dst : 0u).ToList();
            var names = kids.Select(k => byStart.TryGetValue(k, out var o) && o.cls == "hkbClipGenerator" ? AnimName(k) : null).ToList();
            var newKids = new List<uint>();
            foreach (var (cat, tcat) in pairList)
            {
                if (names.Any(n => n != null && n.StartsWith($"a{cat:D3}_"))) continue;
                for (int i = 0; i < kids.Count; i++)
                {
                    var n = names[i];
                    if (n == null || !n.StartsWith($"a{tcat:D3}_")) continue;
                    newKids.Add(CloneClip(pf, byStart[kids[i]], localAt, $"a{cat:D3}_", $"a{tcat:D3}_"));
                    addedNames.Add($"a{cat:D3}_" + n[$"a{tcat:D3}_".Length..]);
                    added[cat]++;
                }
            }
            if (newKids.Count == 0) continue;
            // New, longer array of child pointers.
            pf.Align(16);
            uint na = (uint)pf.Data.Count;
            pf.Data.AddRange(new byte[(size + newKids.Count) * 8]);
            for (int i = 0; i < size; i++) { var g = globalsBySrc[arr + (uint)i * 8]; pf.Global.Add((na + (uint)i * 8, g.sec, g.dst)); }
            for (int i = 0; i < newKids.Count; i++) pf.Global.Add((na + (uint)(size + i) * 8, (uint)pf.DataSectionIndex, newKids[i]));
            int li = pf.Local.FindIndex(l => l.src == hdr);
            pf.Local[li] = (hdr, na);
            int count = size + newKids.Count;
            uint cap = (uint)pf.I32(hdr + 12);
            pf.SetI32(hdr + 8, count);
            pf.SetI32(hdr + 12, (int)((uint)count | (cap & 0xC0000000)));
        }
        foreach (var kv in added) log($"behaviour: moveset a{kv.Key}: {kv.Value} clips added");
        return pf.Write();
    }

    // Shared moves (a000, e.g. the idle transform of a weapon: ChangeGimmick47 Idle = a000_019560) are chosen by a
    // selector that ignores the weapon (offsetType 11). For the given ids this makes the selector choose by the
    // right-hand weapon's moveset (offsetType 13, as the attack selectors do), keeps the shared clip first (every
    // other weapon falls back to it, as before) and adds the new moveset's own clip "aNNN_<id>".
    public static byte[] AddCommonOverrides(byte[] file, int cat, IReadOnlyCollection<int> ids, Action<string> log, List<string> addedNames)
    {
        var pf = new Packfile(file);
        var objs = pf.Objects();
        var byStart = objs.ToDictionary(o => o.start);
        var localAt = pf.Local.GroupBy(l => l.src).ToDictionary(g => g.Key, g => g.First().dst);
        var globalsBySrc = pf.Global.GroupBy(g => g.src).ToDictionary(g => g.Key, g => g.First());
        string? AnimName(uint clip) => localAt.TryGetValue(clip + AnimAt, out var d) ? pf.Str(d) : null;
        int done = 0;
        foreach (var sel in objs.Where(o => o.cls == "CustomManualSelectorGenerator").ToList())
        {
            uint hdr = 0, arr = 0; int size = 0;
            foreach (var l in pf.Local.Where(l => l.src > sel.start && l.src < sel.end).OrderBy(l => l.src))
            {
                int n = pf.I32(l.src + 8);
                if (n > 0 && n < 4096 && globalsBySrc.ContainsKey(l.dst)) { hdr = l.src; arr = l.dst; size = n; break; }
            }
            if (size != 1 || pf.I32(hdr + 16) != 11 || !ids.Contains(pf.I32(hdr + 20))) continue;
            uint kid = globalsBySrc[arr].dst;
            if (!byStart.TryGetValue(kid, out var clip) || clip.cls != "hkbClipGenerator") continue;
            var name = AnimName(kid);
            if (name == null || !name.StartsWith("a000_")) continue;
            uint nk = CloneClip(pf, clip, localAt, $"a{cat:D3}_", "a000_");
            addedNames.Add($"a{cat:D3}_" + name[5..]);
            pf.Align(16);
            uint na = (uint)pf.Data.Count;
            pf.Data.AddRange(new byte[16]);
            pf.Global.Add((na, globalsBySrc[arr].sec, kid));
            pf.Global.Add((na + 8, (uint)pf.DataSectionIndex, nk));
            int li = pf.Local.FindIndex(l => l.src == hdr);
            pf.Local[li] = (hdr, na);
            uint cap = (uint)pf.I32(hdr + 12);
            pf.SetI32(hdr + 8, 2);
            pf.SetI32(hdr + 12, (int)(2u | (cap & 0xC0000000)));
            pf.SetI32(hdr + 16, 13);
            done++;
        }
        log($"behaviour: {done} shared moves (transform etc.) now have a{cat} versions");
        return pf.Write();
    }

    // Manual selectors whose name matches (moves sheet selectorFrom, e.g. the left-gun roll shot's 'Left' ones) and whose
    // slot is in the template family [fromId, fromId + 20) ask for the same offset in the new family (toId) instead: their
    // slot number changes and every clip they hold is replaced by a copy named after the new slot (Front/Back keep the
    // originals). 'moved' gets (moveset, old slot, new slot) for each clip, so the movesets can get the new slot.
    public static byte[] RetargetSelectors(byte[] file, System.Text.RegularExpressions.Regex nameRx, int fromId, int toId, Action<string> log, List<string> addedNames, List<(int cat, int oldId, int newId)> moved)
    {
        var pf = new Packfile(file);
        var objs = pf.Objects();
        var byStart = objs.ToDictionary(o => o.start);
        var localAt = pf.Local.GroupBy(l => l.src).ToDictionary(g => g.Key, g => g.First().dst);
        var globalsBySrc = pf.Global.GroupBy(g => g.src).ToDictionary(g => g.Key, g => g.First());
        string? AnimName(uint clip) => localAt.TryGetValue(clip + AnimAt, out var d) ? pf.Str(d) : null;
        int sels = 0, clips = 0;
        foreach (var sel in objs.Where(o => o.cls == "CustomManualSelectorGenerator").ToList())
        {
            if (!localAt.TryGetValue(sel.start + NameAt, out var nd) || !nameRx.IsMatch(pf.Str(nd))) continue;
            uint hdr = 0, arr = 0; int size = 0;
            foreach (var l in pf.Local.Where(l => l.src > sel.start && l.src < sel.end).OrderBy(l => l.src))
            {
                int n = pf.I32(l.src + 8);
                if (n > 0 && n < 4096 && globalsBySrc.ContainsKey(l.dst)) { hdr = l.src; arr = l.dst; size = n; break; }
            }
            if (size == 0) continue;
            int id = pf.I32(hdr + 20);
            if (id < fromId || id >= fromId + 20) continue;
            int newId = toId + (id - fromId);
            string oldTag = $"_{id:D6}", newTag = $"_{newId:D6}";
            for (int i = 0; i < size; i++)
            {
                uint at = arr + (uint)i * 8;
                int gi = pf.Global.FindIndex(g => g.src == at);
                if (gi < 0) continue;
                var g0 = pf.Global[gi];
                if (!byStart.TryGetValue(g0.dst, out var clip) || clip.cls != "hkbClipGenerator") continue;
                var name = AnimName(g0.dst);
                if (name == null || name.Length != 11 || !name.EndsWith(oldTag)) throw new InvalidDataException($"selector slot {id}: unexpected clip '{name}'");
                uint nk = CloneClip(pf, clip, localAt, s => s.Replace(oldTag, newTag));
                pf.Global[gi] = (g0.src, g0.sec, nk);
                addedNames.Add(name.Replace(oldTag, newTag));
                moved.Add((int.Parse(name.Substring(1, 3)), id, newId));
                clips++;
            }
            pf.SetI32(hdr + 20, newId);
            sels++;
        }
        log($"behaviour: {sels} selectors ({nameRx}) moved from slots {fromId}+ to {toId}+, {clips} clips copied");
        if (sels == 0) throw new InvalidDataException($"no selector matches {nameRx} with a slot in {fromId}..{fromId + 19}");
        return pf.Write();
    }

    static uint CloneClip(Packfile pf, (uint start, uint end, string cls, uint nameOff) clip, Dictionary<uint, uint> localAt, string newPre, string oldPre)
        => CloneClip(pf, clip, localAt, s => newPre + s[oldPre.Length..]);

    static uint CloneClip(Packfile pf, (uint start, uint end, string cls, uint nameOff) clip, Dictionary<uint, uint> localAt, Func<string, string> rename)
    {
        pf.Align(16);
        uint at = (uint)pf.Data.Count;
        uint len = Math.Min(clip.end - clip.start, 400u);
        pf.Data.AddRange(pf.Data.GetRange((int)clip.start, (int)len));
        pf.Align(16);
        // Strings: the two names with the new moveset number; other owned data shared with the original.
        foreach (var l in pf.Local.Where(l => l.src >= clip.start && l.src < clip.start + len).ToList())
        {
            uint off = l.src - clip.start;
            uint dst = l.dst;
            if (off == NameAt || off == AnimAt) { var s = pf.Str(l.dst); dst = pf.AddString(rename(s)); }
            pf.Local.Add((at + off, dst));
        }
        foreach (var g in pf.Global.Where(g => g.src >= clip.start && g.src < clip.start + len).ToList()) pf.Global.Add((at + (g.src - clip.start), g.sec, g.dst));
        var v = pf.Virtual.First(x => x.src == clip.start);
        pf.Virtual.Add((at, v.sec, v.name));
        return at;
    }
}

