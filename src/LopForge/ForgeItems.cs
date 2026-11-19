using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Localization;
using SoulsFormats;

namespace LopForge;

// Items: weapon rows (+0..+10), Bath Messenger shop rows and item texts, written as a bbport mod layer.
public static class ForgeItems
{
    // Shop rows go at the end of the Bath Messengers' base stock (100000..100017 in 1.09).
    const int ShopBlock = 100000;      // Bath Messengers, first list (no badge needed)
    const int DiagBadgeBlock = 100600; // Sword Hunter Badge list (check builds only)
    static readonly int[] Lineups = { 100000, 110000, 120000, 130000, 140000 };
    public static bool Diag;
    // Check build: (weapon id, wepmotionCategory, wepmotionBothHandId, shop name).
    public static readonly (int id, int cat, int both, string label, int src)[] DiagCopies =
    {
        (9800000, 160, 2, "Pale Knight check A", 9600000),

    };

    public static void Build(IFileProvider lop, string dvd, string outDvd, List<ItemRow> items, Action<string> log)
    {
        var text = LopText(lop);
        string T(string key)
        {
            var k = key.StartsWith("lop:") ? key[4..] : key;
            var hit = text.FirstOrDefault(kv => kv.Key.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (hit.Value == null) throw new InvalidDataException("Lies of P text not found: " + k);
            return hit.Value;
        }

        // Params
        var ps = BbInspect.Params(dvd, out var bnd);
        var wep = ps["EquipParamWeapon"];
        var shop = ps["ShopLineupParam"];
        // Another enabled mod can carry a copy of this mod's own rows (creator's "Chester in Yharnam", 2026-11-13, was
        // saved from params that already had them): drop every row this builder makes (named after an item, or a check
        // copy) from the base, so a rebuild starts clean (no doubled shop rows, no sort-order clash).
        var ownPrefixes = items.Select(i => i.key).Concat(DiagCopies.Select(d => d.label)).ToList();
        int stale = 0;
        foreach (var pn in new[] { "EquipParamWeapon", "ShopLineupParam", "WeaponGenParam", "BehaviorParam_PC", "Bullet", "SpEffectParam", "AtkParam_Pc" })
            if (ps.TryGetValue(pn, out var pp))
                stale += pp.Rows.RemoveAll(r => r.Name != null && ownPrefixes.Any(k => r.Name.StartsWith(k, StringComparison.Ordinal)));
        if (stale > 0) log($"base params already held {stale} rows of this mod (another mod copied them): rebuilt fresh");
        foreach (var it in items)
        {
            for (int lvl = 0; lvl <= 10; lvl++)
            {
                var src = wep.Rows.FirstOrDefault(r => r.ID == it.templateWeaponId + lvl * 100);
                if (src == null) { if (lvl == 0) throw new InvalidDataException("template weapon missing " + it.templateWeaponId); continue; }
                int id = it.bbWeaponId + lvl * 100;
                wep.Rows.RemoveAll(r => r.ID == id);
                var row = new PARAM.Row(id, it.key + (lvl > 0 ? "+" + lvl : ""), wep.AppliedParamdef);
                for (int c = 0; c < row.Cells.Count; c++) row.Cells[c].Value = src.Cells[c].Value;
                Set(row, "equipModelId", (ushort)it.equipModelId);
                Set(row, "originEquipWep", it.bbWeaponId);
                Set(row, "wepmotionBothHandId", (byte)it.wepmotionBothHandId);
                if (it.wepmotionCategory >= 0) Set(row, "wepmotionCategory", (byte)it.wepmotionCategory);
                Set(row, "residentSpEffectId", it.residentSpEffectId);
                if (it.iconFrom != "") Set(row, "iconId", (ushort)it.iconId);
                // Transformed form held at the same hand point as the normal form (creator: it must look the same).
                if (it.formBDummy >= 0) Set(row, "dmypolyId_Slot2RightFormB", it.formBDummy);
                Set(row, "weight", it.weight);
                Set(row, "spAttribute", (byte)it.spAttribute);
                Set(row, "properStrength", (byte)it.properStrength);
                Set(row, "properAgility", (byte)it.properAgility);
                Set(row, "bulletConsumeNum", (sbyte)it.bulletConsumeNum);
                // Own place in the menus' sort order (the template's +sortIdOffset), not shared with the template weapon.
                if (it.sortIdOffset != 0)
                {
                    int sid = Convert.ToInt32(src.Cells.First(x => x.Def.InternalName == "sortId").Value) + it.sortIdOffset;
                    var clash = wep.Rows.FirstOrDefault(r => r.ID != id && Convert.ToInt32(r.Cells.First(x => x.Def.InternalName == "sortId").Value) == sid);
                    if (clash != null) throw new InvalidDataException($"sortId {sid} for {it.key}+{lvl} already used by weapon {clash.ID}");
                    Set(row, "sortId", sid);
                }
                // Template upgrade curve, scaled from the template's +0 to this weapon's base numbers.
                Scale(row, src, wep, it.templateWeaponId, "attackBasePhysics", it.attackBasePhysics);
                Scale(row, src, wep, it.templateWeaponId, "attackBaseFire", it.attackBaseFire);
                Scale(row, src, wep, it.templateWeaponId, "correctStrength", it.correctStrength);
                Scale(row, src, wep, it.templateWeaponId, "correctAgility", it.correctAgility);
                Scale(row, src, wep, it.templateWeaponId, "correctMagic", it.correctMagic);
                wep.Rows.Add(row);
            }
        }
        wep.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));

        // Blood gem slots: Bloodborne keeps them per weapon and level in WeaponGenParam (slot shapes, opening at the
        // levels the template opens them). Each weapon copies its gemSlotsFrom weapon's rows (creator 0.3.24 test).
        var gen = ps["WeaponGenParam"];
        int genRows = 0;
        foreach (var it in items.Where(i => i.gemSlotsFrom > 0))
            for (int lvl = 0; lvl <= 10; lvl++)
            {
                var src = gen.Rows.FirstOrDefault(r => r.ID == it.gemSlotsFrom + lvl * 100);
                if (src == null) { if (lvl == 0) throw new InvalidDataException("gem slot row missing " + it.gemSlotsFrom); continue; }
                int id = it.bbWeaponId + lvl * 100;
                gen.Rows.RemoveAll(r => r.ID == id);
                var row = new PARAM.Row(id, it.key + (lvl > 0 ? "+" + lvl : ""), gen.AppliedParamdef);
                for (int c = 0; c < row.Cells.Count; c++) row.Cells[c].Value = src.Cells[c].Value;
                Set(row, "wepParamId", id);
                gen.Rows.Add(row); genRows++;
            }
        gen.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        log($"gem slots: {genRows} weapon levels");

        var tmplShop = shop.Rows.First(r => r.ID == 100100);
        // The Bath Messengers have five copies of their lineup (100000, 110000, ... 140000; the game picks one by
        // the player's progress), each read from its first ID until the first missing one. New rows go straight
        // after the first block of every copy.
        int NextFree(int blockStart) { int id = blockStart; while (shop.Rows.Any(r => r.ID == id)) id++; return id; }
        int lineup = 0;
        PARAM.Row AddShop(string name, int equipId, int price, int qwc)
        {
            int id = NextFree(lineup + (qwc == -1 ? 0 : DiagBadgeBlock - ShopBlock));
            var row = new PARAM.Row(id, name, shop.AppliedParamdef);
            for (int c = 0; c < row.Cells.Count; c++) row.Cells[c].Value = tmplShop.Cells[c].Value;
            Set(row, "equipId", equipId);
            Set(row, "value", price);
            Set(row, "qwcId", qwc);
            Set(row, "eventFlag", -1);
            Set(row, "equipType", (byte)0);
            shop.Rows.Add(row);
            return row;
        }
        foreach (var copy in Lineups)
        {
            lineup = copy;
            foreach (var it in items)
                foreach (var q in it.shopQwc) AddShop(it.key, it.bbWeaponId, it.shopPrice, q);
        }
        lineup = ShopBlock;
        {
            // Check copies of the Pale Knight on other moveset numbers (check builds). Their weapon rows and names are
            // always kept, so a save that already holds one stays valid; only check builds (--diag) sell them
            // (creator 0.3.30: the test version is no longer needed).
            foreach (var (cid, cat, both, label, srcId) in DiagCopies)
            {
                var srcRow = wep.Rows.First(r => r.ID == srcId);
                wep.Rows.RemoveAll(r => r.ID == cid);
                var cl = new PARAM.Row(cid, label, wep.AppliedParamdef);
                for (int c = 0; c < cl.Cells.Count; c++) cl.Cells[c].Value = srcRow.Cells[c].Value;
                Set(cl, "originEquipWep", cid);
                Set(cl, "wepmotionCategory", (byte)cat);
                Set(cl, "wepmotionBothHandId", (byte)both);
                // No stat requirements, so anyone can equip the check copies.
                foreach (var c in cl.Cells.Where(x => x.Def.InternalName.StartsWith("proper")))
                    c.Value = Convert.ChangeType(0, c.Value.GetType());
                wep.Rows.Add(cl);
                if (Diag) foreach (var copy in Lineups) { lineup = copy; AddShop(label, cid, 10, -1); }
            }
            wep.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        shop.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        ForgeShots.Apply(ps, items, log);

        foreach (var f in bnd.Files)
        {
            var name = Path.GetFileNameWithoutExtension(f.Name);
            if (name == "EquipParamWeapon") f.Bytes = wep.Write();
            if (name == "ShopLineupParam") f.Bytes = shop.Write();
            if (name == "WeaponGenParam") f.Bytes = gen.Write();
            if (name == "BehaviorParam_PC" || name == "Bullet" || name == "SpEffectParam" || name == "AtkParam_Pc") f.Bytes = ps[name].Write();
        }
        var outParam = Path.Combine(outDvd, "param", "gameparam", "gameparam.parambnd.dcx");
        Directory.CreateDirectory(Path.GetDirectoryName(outParam)!);
        bnd.Write(outParam);
        log($"params: {items.Count} weapons x11 levels, {items.Count} shop rows");

        // Texts in every language folder (Lies of P's English words; the item names are proper names).
        foreach (var langDir in Directory.GetDirectories(Path.Combine(dvd, "msg")))
        {
            var src = Layers.Source(Path.Combine("msg", Path.GetFileName(langDir), "item.msgbnd.dcx"));
            if (!File.Exists(src)) continue;
            var mb = BND4.Read(src);
            foreach (var f in mb.Files)
            {
                var fmgId = f.ID;
                if (fmgId != 11 && fmgId != 21 && fmgId != 25) continue;
                var fmg = FMG.Read(f.Bytes);
                // Upgrade suffix as this language writes it (" +1", Japanese "＋１"): the Holy Moonlight Sword's
                // +N name minus its +0 name (creator 0.3.24 test: upgraded weapons showed no +1..+10).
                string Suffix(int lvl)
                {
                    string? b0 = fmg[26000000], bn = fmg[26000000 + lvl * 100];
                    return b0 != null && bn != null && bn.StartsWith(b0) && bn.Length > b0.Length ? bn[b0.Length..] : $" +{lvl}";
                }
                foreach (var it in items)
                {
                    string s = fmgId == 11 ? T(it.nameFrom) : fmgId == 21 ? FirstLine(T(it.infoFrom)) : T(it.infoFrom) + "\n\n" + T(it.captionFrom);
                    for (int lvl = 0; lvl <= 10; lvl++)
                    {
                        int id = it.bbWeaponId + lvl * 100;
                        fmg.Entries.RemoveAll(e => e.ID == id);
                        fmg.Entries.Add(new FMG.Entry(id, fmgId == 11 && lvl > 0 ? s + Suffix(lvl) : s));
                    }
                }
                foreach (var (cid, _, _, label, _) in DiagCopies)
                    {
                        fmg.Entries.RemoveAll(e => e.ID == cid);
                        fmg.Entries.Add(new FMG.Entry(cid, label));
                    }
                fmg.Entries.Sort((a, b) => a.ID.CompareTo(b.ID));
                f.Bytes = fmg.Write();
            }
            var dst = Path.Combine(outDvd, "msg", Path.GetFileName(langDir), "item.msgbnd.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            mb.Write(dst);
        }
        log("texts: names, descriptions and lore from Lies of P");

        // Menu icons: each item's own Lies of P item picture, as a Bloodborne icon (menu/menu_knowledge_<id>).
        foreach (var it in items.Where(i => i.iconFrom != ""))
        {
            var like = TPF.Read(Layers.Source(Path.Combine("menu", $"menu_knowledge_{it.iconTemplate:D5}.tpf.dcx")));
            var t0 = like.Textures[0];
            int size = t0.Header?.Width ?? 400;
            var pic = LopTex.FitSquare(LopTex.Load(lop, it.iconFrom, 1024), size);
            var name = $"MENU_Knowledge_{it.iconId:D5}";
            like.Textures[0] = LopTex.ToTpf(pic, name, t0, t0.Mipmaps > 1);
            var dstIcon = Path.Combine(outDvd, "menu", $"menu_knowledge_{it.iconId:D5}.tpf.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(dstIcon)!);
            like.Write(dstIcon);
            log($"icon: {it.key} -> {name} from {Path.GetFileName(it.iconFrom)}");
        }

        // Inventory grid/list icons: the menu draws them from icon sheets in menu/common.tpf (MENU_Icon_<group><sheet>,
        // 12 x 12 cells of 80 px; icon 2116 = sheet 02001 cell 116). The item's picture goes into its own cell
        // (creator, 0.3.4 test: the grid showed the Butcher set that cell held).
        var gridItems = items.Where(i => i.iconFrom != "").ToList();
        if (gridItems.Count > 0)
        {
            var common = TPF.Read(Layers.Source(Path.Combine("menu", "common.tpf.dcx")));
            foreach (var g in gridItems.GroupBy(i => $"MENU_Icon_{i.iconId / 1000:D2}{(i.iconId % 1000) / 144 + 1:D3}"))
            {
                int ti = common.Textures.FindIndex(t => t.Name == g.Key);
                if (ti < 0) throw new InvalidDataException("icon sheet missing: " + g.Key);
                var t0 = common.Textures[ti];
                var ddsFile = BCnEncoder.Shared.ImageFiles.DdsFile.Load(new MemoryStream(t0.Headerize()));
                var px = new BCnEncoder.Decoder.BcDecoder().Decode(ddsFile);
                var sheet = new LopTex.Rgba { W = (int)ddsFile.header.dwWidth, H = (int)ddsFile.header.dwHeight };
                sheet.Px = new byte[sheet.W * sheet.H * 4];
                for (int k = 0; k < px.Length; k++) { sheet.Px[k * 4] = px[k].r; sheet.Px[k * 4 + 1] = px[k].g; sheet.Px[k * 4 + 2] = px[k].b; sheet.Px[k * 4 + 3] = px[k].a; }
                foreach (var it in g)
                {
                    int cell = (it.iconId % 1000) % 144, cx = cell % 12 * 80, cy = cell / 12 * 80;
                    var pic = LopTex.FitSquare(LopTex.Load(lop, it.iconFrom, 1024), 80);
                    for (int y = 0; y < 80; y++) for (int x = 0; x < 80; x++)
                    {
                        int s = (y * pic.W + x) * 4, d = ((cy + y) * sheet.W + cx + x) * 4;
                        if (x >= pic.W || y >= pic.H) { sheet.Px[d] = sheet.Px[d + 1] = sheet.Px[d + 2] = sheet.Px[d + 3] = 0; continue; }
                        for (int c = 0; c < 4; c++) sheet.Px[d + c] = pic.Px[s + c];
                    }
                    log($"grid icon: {it.key} -> {g.Key} cell {cell}");
                }
                common.Textures[ti] = LopTex.ToTpf(sheet, t0.Name, t0, t0.Mipmaps > 1);
            }
            var dstCommon = Path.Combine(outDvd, "menu", "common.tpf.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(dstCommon)!);
            common.Write(dstCommon);
        }
    }

    static string FirstLine(string s) { var i = s.IndexOf('\n'); return i < 0 ? s : s[..i]; }

    static void Set(PARAM.Row row, string field, object v)
    {
        var c = row.Cells.FirstOrDefault(x => x.Def.InternalName == field) ?? throw new InvalidDataException("no field " + field);
        c.Value = Convert.ChangeType(v, c.Value.GetType());
    }

    static void Scale(PARAM.Row row, PARAM.Row src, PARAM wep, int templateBase, string field, int newBase)
    {
        var baseRow = wep.Rows.First(r => r.ID == templateBase);
        double b = Convert.ToDouble(baseRow.Cells.First(x => x.Def.InternalName == field).Value);
        double v = Convert.ToDouble(src.Cells.First(x => x.Def.InternalName == field).Value);
        double result = b == 0 ? (newBase == 0 ? 0 : newBase * (1 + 0.0 * v)) : newBase * (v / b);
        var cell = row.Cells.First(x => x.Def.InternalName == field);
        cell.Value = Convert.ChangeType(Math.Round(result), cell.Value.GetType());
    }

    // English texts from the player's Lies of P (LOP_Game.locres).
    public static Dictionary<string, string> LopText(IFileProvider p)
    {
        using var ar = p.CreateReader("LiesofP/Content/Localization/LOP_Game/en/LOP_Game.locres");
        var loc = new FTextLocalizationResource(ar);
        var d = new Dictionary<string, string>();
        foreach (var ns in loc.Entries)
            foreach (var kv in ns.Value)
                d[ns.Key.Str + ":" + kv.Key.Str] = kv.Value.LocalizedString;
        return d;
    }
}





