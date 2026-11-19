using System.Text.Json;
using SoulsFormats;

namespace LopForge;

// The Pale Knight's shots and Strengthen Bullet buff, from sheets/shots.json, in the player's own params.
public sealed class ShotRow
{
    public string key { get; set; } = "";
    public int behaviorFrom { get; set; }
    public int moonBullet { get; set; }
    public int newBullet { get; set; }
    public int visualFrom { get; set; }
    public int behaviorTo { get; set; }   // a new behaviour (copy of behaviorFrom's) instead of replacing that one's bullet
    public int carrierJudge { get; set; }    // >0: a hit along the shot line carrying the whole shot's damage, normal form (judge in the Pale Knight's variation)
    public int carrierJudgeB { get; set; }   // the same from the transformed blade
    public int carrierJudgeRev { get; set; }    // >0: the carrier for this shot fired from a reversed point (shots sheet reversePoints), normal form (blastRev shape)
    public int carrierJudgeRevB { get; set; }   // the same, transformed form (blastRevB shape)
    public string note { get; set; } = "";
}
public sealed class ShotSheet
{
    public sealed class Var { public int from { get; set; } public int to { get; set; } }
    public sealed class Buff { public int spEffect { get; set; } public int copyOf { get; set; } public float endurance { get; set; } public int slot { get; set; } public int countsAs { get; set; } }
    public Var behaviorVariation { get; set; } = new();
    public Buff buff { get; set; } = new();
    public List<ShotRow> rows { get; set; } = new();
    // Other weapons' behaviours copied into the Pale Knight's (e.g. the Rifle Spear's shot 101000500 -> 109600500).
    public sealed class Extra { public int from { get; set; } public int to { get; set; } public string note { get; set; } = ""; }
    public List<Extra> extraBehaviors { get; set; } = new();
    public sealed class Melee { public int atkFrom { get; set; } public int atkTo { get; set; } public int mapHitType { get; set; } public List<int> judges { get; set; } = new(); public bool fullBlade { get; set; } public string note { get; set; } = ""; }
    public Melee? melee { get; set; }
    public sealed class Mixed { public int atkBase { get; set; } public List<int> judges { get; set; } = new(); public List<string> types { get; set; } = new(); public string note { get; set; } = ""; }
    public Mixed? mixedDamage { get; set; }   // judges whose own attack copy deals every listed damage type at the largest of their corrections
    public sealed class Fx { public int id { get; set; } public int dummy { get; set; } }
    public sealed class Seg { public int from { get; set; } public int to { get; set; } public float radius { get; set; } }
    public sealed class Blast { public int judge { get; set; } public int behaviorFrom { get; set; } public int atkFrom { get; set; } public int atkTo { get; set; } public int mapHitType { get; set; } public int stamina { get; set; } public List<Seg> segments { get; set; } = new(); public List<Fx> sfx { get; set; } = new(); public string note { get; set; } = ""; }
    public Blast? blast { get; set; }
    public Blast? blastB { get; set; }
    public Blast? blastRev { get; set; }    // the blast shape from the hand away from the blade (items backDummies), normal form
    public Blast? blastRevB { get; set; }   // the same on the transformed blade
    public List<int> reversePoints { get; set; } = new();   // model points (0-999) that face away from the blade (items backDummies)
    public bool splitPellets { get; set; }
    public float pelletStrength { get; set; } = 1f;
    public List<string> pelletDamageTypes { get; set; } = new();   // AtkParam correction fields the pellets deal (empty = the gun pellet's own)
    public sealed class LeftShot { public string weapon { get; set; } = ""; public int variationFrom { get; set; } public int variationTo { get; set; } public int judge { get; set; } public int behaviorFrom { get; set; } public int specialJudge { get; set; } public int specialBulletFrom { get; set; } public int specialBullet { get; set; } public int specialBlastBullet { get; set; } public int specialAtkBase { get; set; } public float specialDirectCorrection { get; set; } public float specialBlastCorrection { get; set; } public string note { get; set; } = ""; }
    public LeftShot? leftShot { get; set; }
    public sealed class Cost { public int fromSlot { get; set; } public int judge { get; set; } public string note { get; set; } = ""; }
    public Cost? bulletCost { get; set; }
    public static ShotSheet Load() => JsonSerializer.Deserialize<ShotSheet>(File.ReadAllText(Path.Combine(Sheets.Dir, "shots.json")))!;
}

public static class ForgeShots
{
    public static readonly Dictionary<int, int> Carriers = new();   // unused (ForgeMoves reads the sheet itself)
    public static void Apply(Dictionary<string, PARAM> ps, List<ItemRow> items, Action<string> log)
    {
        var sh = ShotSheet.Load();
        var beh = ps["BehaviorParam_PC"]; var bul = ps["Bullet"]; var sp = ps["SpEffectParam"]; var wep = ps["EquipParamWeapon"];
        int fromBase = 100_000_000 + sh.behaviorVariation.from * 1000, toBase = 100_000_000 + sh.behaviorVariation.to * 1000;

        // 1. The Pale Knight's own behaviour rows: a copy of the Holy Moonlight Sword's.
        int copied = 0;
        foreach (var r in beh.Rows.Where(r => r.ID >= fromBase && r.ID < fromBase + 1000).ToList())
        {
            int id = toBase + (r.ID - fromBase);
            beh.Rows.RemoveAll(x => x.ID == id);
            beh.Rows.Add(Copy(beh, r, id, "PaleKnight " + r.Name));
            copied++;
        }
        var carrierPlan = new List<(ShotRow row, float strength)>();
        Carriers.Clear();
        // 2. Its projectile behaviours fire the new explosive bullets.
        foreach (var s in sh.rows)
        {
            int bid = toBase + (s.behaviorFrom - fromBase);
            var b = beh.Rows.FirstOrDefault(x => x.ID == bid) ?? throw new InvalidDataException("behaviour missing " + bid);
            if (s.behaviorTo > 0)
            {
                beh.Rows.RemoveAll(x => x.ID == s.behaviorTo);
                b = Copy(beh, b, s.behaviorTo, "PaleKnight shot " + s.key);
                beh.Rows.Add(b);
            }
            Set(b, "refId", s.newBullet);
            var vis = bul.Rows.First(x => x.ID == s.visualFrom);
            var moon = bul.Rows.First(x => x.ID == s.moonBullet);
            bul.Rows.RemoveAll(x => x.ID == s.newBullet);
            var nb = Copy(bul, vis, s.newBullet, "PaleKnight shot " + s.key);
            Set(nb, "atkId_Bullet", Get(moon, "atkId_Bullet"));
            // A spread shot (the blunderbuss fires 5 pellets) carrying one beam's attack would hit with it 5 times
            // (creator 0.3.20 test: the transformed follow-up killed Vicar Amelia in one hit). Each pellet gets its own
            // copy of the attack with the damage shared out, so a full point-blank volley equals the one beam.
            // The pellets burst into more pellets (child bullets, e.g. 601 then 602), each with the gun's own attack:
            // the shot gets its own chain, and one beam's damage is shared out over every layer and every pellet in it,
            // so a full point-blank volley equals the one beam and fewer pellets land at range.
            int pellets = Convert.ToInt32(Get(nb, "numShoot"));
            if (sh.splitPellets && pellets > 1 && ps.TryGetValue("AtkParam_Pc", out var atkS))
            {
                // Damage types come from the gun's own pellet attack (the blunderbuss pellets': physical, fire...; the
                // Moonlight Sword beam's attack is magic only and the Pale Knight has no magic attack, so it did nothing:
                // creator 0.3.21 test). The strength of one full volley is the beam's (its largest correction, e.g. 280%).
                int moonAtk = Convert.ToInt32(Get(moon, "atkId_Bullet"));
                var beam = atkS.Rows.FirstOrDefault(r => r.ID == moonAtk);
                int firstChild = Convert.ToInt32(Get(nb, "HitBulletID"));
                int pelletAtk = firstChild > 0 ? Convert.ToInt32(Get(bul.Rows.First(r => r.ID == firstChild), "atkId_Bullet")) : -1;
                var a0 = atkS.Rows.FirstOrDefault(r => r.ID == pelletAtk) ?? beam;
                var corrNames = new[] { "atkPhysCorrection", "atkMagCorrection", "atkFireCorrection", "atkThunCorrection" };
                float strength = (beam == null ? 100f : corrNames.Max(f => Convert.ToSingle(Get(beam, f)))) * (sh.pelletStrength > 0 ? sh.pelletStrength : 1f);
                if (a0 != null)
                {
                    var chain = new List<PARAM.Row> { nb };
                    for (int child = Convert.ToInt32(Get(nb, "HitBulletID")); child > 0 && chain.Count < 6; )
                    {
                        var cb = bul.Rows.FirstOrDefault(r => r.ID == child); if (cb == null) break;
                        int cid = s.newBullet + chain.Count;
                        bul.Rows.RemoveAll(r => r.ID == cid);
                        var nc = Copy(bul, cb, cid, $"PaleKnight shot {s.key} pellets {chain.Count}");
                        Set(chain[^1], "HitBulletID", cid);
                        bul.Rows.Add(nc); chain.Add(nc);
                        child = Convert.ToInt32(Get(cb, "HitBulletID"));
                    }
                    int hits = 1;
                    foreach (var layer in chain)
                    {
                        hits *= Math.Max(1, Convert.ToInt32(Get(layer, "numShoot")));
                        int pid = 9690000 + layer.ID % 10000;
                        atkS.Rows.RemoveAll(r => r.ID == pid);
                        var pa = Copy(atkS, a0, pid, "PaleKnight pellet " + s.key);
                        // Each damage type the pellet deals (100% in the blunderbuss's) scaled so the whole volley is one beam.
                        // pelletDamageTypes (creator 0.3.26: the shots scale with Arcane like the Moonlight Sword's beams):
                        // only those damage types carry the shot, each at the full share; the rest are off.
                        foreach (var f in corrNames)
                        {
                            float typeRate = sh.pelletDamageTypes.Count > 0 ? (sh.pelletDamageTypes.Contains(f) ? 100f : 0f) : Convert.ToSingle(Get(a0, f));
                            // With a carrier the pellets are the look only: the carrier lands the damage in one hit.
                            Set(pa, f, s.carrierJudge > 0 ? 0f : typeRate / 100f * strength / (hits * chain.Count));
                        }
                        foreach (var f in new[] { "atkStamCorrection", "atkSuperArmorCorrection" })
                            Set(pa, f, Convert.ToSingle(Get(a0, f)) / (hits * chain.Count) * chain.Count);
                        atkS.Rows.Add(pa);
                        Set(layer, "atkId_Bullet", pid);
                    }
                    // The carrier (creator 0.3.27: arcane pellets did no damage at all): one volley shared over ~35 pellets
                    // made each hit so small that defence took all of it; the cannonball, one hit with the beam's attack, did
                    // fine. A second shot event cost a second Quicksilver Bullet and its single bullet often missed (creator
                    // 0.3.28), so the damage rides as a hit along the shot line instead (built after the blasts, below).
                    if (s.carrierJudge > 0 && beam != null) carrierPlan.Add((s, strength));
                    atkS.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
                }
            }
            bul.Rows.Add(nb);
        }
        foreach (var x in sh.extraBehaviors)
        {
            var srcRow = beh.Rows.FirstOrDefault(r => r.ID == x.from) ?? throw new InvalidDataException("behaviour missing " + x.from);
            beh.Rows.RemoveAll(r => r.ID == x.to);
            var nr = Copy(beh, srcRow, x.to, "PaleKnight " + srcRow.Name);
            Set(nr, "variationId", sh.behaviorVariation.to);
            beh.Rows.Add(nr);
        }
        // 2b. Its melee attacks are its own copies that ignore the ground and walls (mapHitType), so the long blade
        //     doesn't bounce off a slope or a wall mid-combo (creator, 0.3.4 test).
        int atkCopies = 0;
        if (sh.melee is { atkTo: > 0 } m && ps.TryGetValue("AtkParam_Pc", out var atk))
        {
            int atkFrom = m.atkFrom, atkTo = m.atkTo;
            foreach (var b in beh.Rows.Where(r => r.ID >= toBase && r.ID < toBase + 1000).ToList())
            {
                if (Convert.ToInt32(Get(b, "refType")) != 0) continue;
                if (m.judges.Count > 0 && !m.judges.Contains(b.ID - toBase)) continue;
                int rid = Convert.ToInt32(Get(b, "refId"));
                if (rid < atkFrom || rid >= atkFrom + 1000) continue;
                int nid = atkTo + (rid - atkFrom);
                if (!atk.Rows.Any(x => x.ID == nid))
                {
                    var srcAtk = atk.Rows.FirstOrDefault(x => x.ID == rid);
                    if (srcAtk == null) continue;
                    var na = Copy(atk, srcAtk, nid, "PaleKnight " + srcAtk.Name);
                    Set(na, "mapHitType", m.mapHitType);
                    if (m.fullBlade && Convert.ToInt32(Get(na, "hit0_DmyPoly1")) == 10100)
                    {
                        // The whole blade hits: the second line runs from the outer line's inner end to near the hand.
                        Set(na, "hit1_DmyPoly1", Convert.ToInt32(Get(na, "hit0_DmyPoly2")));
                        Set(na, "hit1_DmyPoly2", 10120);
                        Set(na, "hit1_Radius", Convert.ToSingle(Get(na, "hit0_Radius")));
                        Set(na, "hit1_hitType", 0);
                    }
                    atk.Rows.Add(na);
                    atkCopies++;
                }
                Set(b, "refId", nid);
            }
            atk.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        // 2b'. Mixed damage (shots mixedDamage; creator 2026-11-18: the held R2's slash deals physical, fire and arcane):
        //      each judge's own attack copy with every listed correction at the largest of them.
        if (sh.mixedDamage is { atkBase: > 0 } mx && ps.TryGetValue("AtkParam_Pc", out var atkM))
        {
            foreach (int j in mx.judges)
            {
                var b = beh.Rows.FirstOrDefault(r => r.ID == toBase + j) ?? throw new InvalidDataException($"mixedDamage: no Pale Knight behaviour judge {j}");
                if (Convert.ToInt32(Get(b, "refType")) != 0) throw new InvalidDataException($"mixedDamage: judge {j} is not an attack");
                int rid = Convert.ToInt32(Get(b, "refId")), nid = mx.atkBase + j;
                var srcM = atkM.Rows.FirstOrDefault(x => x.ID == rid) ?? throw new InvalidDataException($"mixedDamage: attack {rid} missing");
                atkM.Rows.RemoveAll(x => x.ID == nid);
                var na = Copy(atkM, srcM, nid, "PaleKnight mixed " + srcM.Name);
                float top = mx.types.Max(f => Convert.ToSingle(Get(na, f)));
                foreach (var f in mx.types) Set(na, f, top);
                atkM.Rows.Add(na);
                Set(b, "refId", nid);
                log($"shots: judge {j} -> attack {nid} ({string.Join("/", mx.types.Select(f => f.Replace("atk", "").Replace("Correction", "")))} {top})");
            }
            atkM.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        // 2c. The untransformed blast: a hit from the blade tip out through the reach points (Bloodborne fires no
        //     right-hand projectile in the normal form), as its own behaviour judge and attack.
        //     blastB: the same on the transformed blade (its out-of-ammo moves).
        foreach (var bl in new[] { sh.blast, sh.blastB, sh.blastRev, sh.blastRevB }.Where(b => b is { judge: > 0 }).Cast<ShotSheet.Blast>())
        {
            if (!ps.TryGetValue("AtkParam_Pc", out var atk2)) break;
            var srcB = beh.Rows.FirstOrDefault(r => r.ID == bl.behaviorFrom) ?? throw new InvalidDataException("behaviour missing " + bl.behaviorFrom);
            int bid = toBase + bl.judge;
            beh.Rows.RemoveAll(r => r.ID == bid);
            var nb = Copy(beh, srcB, bid, "PaleKnight blast");
            Set(nb, "variationId", sh.behaviorVariation.to);
            Set(nb, "behaviorJudgeId", bl.judge);
            Set(nb, "refType", 0);
            Set(nb, "refId", bl.atkTo);
            Set(nb, "stamina", bl.stamina);
            beh.Rows.Add(nb);
            var srcA = atk2.Rows.FirstOrDefault(r => r.ID == bl.atkFrom) ?? throw new InvalidDataException("attack missing " + bl.atkFrom);
            atk2.Rows.RemoveAll(r => r.ID == bl.atkTo);
            var na = Copy(atk2, srcA, bl.atkTo, "PaleKnight blast");
            for (int i = 0; i < 4; i++)
            {
                var s = i < bl.segments.Count ? bl.segments[i] : null;
                Set(na, $"hit{i}_DmyPoly1", s?.from ?? -1);
                Set(na, $"hit{i}_DmyPoly2", s?.to ?? -1);
                Set(na, $"hit{i}_Radius", s?.radius ?? 0f);
                Set(na, $"hit{i}_hitType", 0);
            }
            Set(na, "mapHitType", bl.mapHitType);
            atk2.Rows.Add(na);
            atk2.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        }
        // 2c'. The pellet shots' carriers: a hit along the shot line with the shot's whole damage (the beam's strength in
        //      pelletDamageTypes), shaped like the blast of the form the shot leaves from: carrierJudge on the normal
        //      blade (blast), carrierJudgeB on the transformed one (blastB). The move builder lays it on each shot.
        if (carrierPlan.Count > 0 && ps.TryGetValue("AtkParam_Pc", out var atk3))
        {
            var corrNames = new[] { "atkPhysCorrection", "atkMagCorrection", "atkFireCorrection", "atkThunCorrection" };
            foreach (var (row, strength) in carrierPlan)
                foreach (var (judge, bl) in new[] { (row.carrierJudge, sh.blast), (row.carrierJudgeB, sh.blastB), (row.carrierJudgeRev, sh.blastRev), (row.carrierJudgeRevB, sh.blastRevB) })
                {
                    if (judge <= 0 && (bl == sh.blastRev || bl == sh.blastRevB)) continue;   // reversed carriers only where the sheet names them
                    if (judge <= 0 || bl is not { judge: > 0 }) throw new InvalidDataException($"shot {row.key}: carrier needs carrierJudge, carrierJudgeB and both blasts");
                    int bid = toBase + judge, aid = 9609000 + judge;
                    if (beh.Rows.Any(r => r.ID == bid)) throw new InvalidDataException($"carrier judge {judge} is already a Pale Knight behaviour");
                    var nb = Copy(beh, beh.Rows.First(r => r.ID == toBase + bl.judge), bid, $"PaleKnight shot {row.key} carrier");
                    Set(nb, "behaviorJudgeId", judge);
                    Set(nb, "refId", aid);
                    beh.Rows.Add(nb);
                    atk3.Rows.RemoveAll(r => r.ID == aid);
                    var na = Copy(atk3, atk3.Rows.First(r => r.ID == bl.atkTo), aid, $"PaleKnight shot {row.key} carrier");
                    foreach (var f in corrNames)
                        Set(na, f, sh.pelletDamageTypes.Count == 0 || sh.pelletDamageTypes.Contains(f) ? strength : 0f);
                    atk3.Rows.Add(na);
                }
            beh.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            atk3.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
            log($"shots: {carrierPlan.Count} carriers (normal and transformed form)");
        }
        // 2d. The Cataclysm's own behaviour variation (copies of its template's) plus the Pale Knight R2 follow-up shot,
        //     which only it has, so R2 fires from the left arm only when the Cataclysm is equipped.
        if (sh.leftShot is { variationTo: > 0 } ls)
        {
            int lf = 100_000_000 + ls.variationFrom * 1000, lt = 100_000_000 + ls.variationTo * 1000;
            foreach (var r in beh.Rows.Where(r => r.ID >= lf && r.ID < lf + 1000).ToList())
            {
                int id = lt + (r.ID - lf);
                beh.Rows.RemoveAll(x => x.ID == id);
                var nr = Copy(beh, r, id, ls.weapon + " " + r.Name);
                Set(nr, "variationId", ls.variationTo);
                beh.Rows.Add(nr);
            }
            if (ls.judge > 0)
            {
                var srcL = beh.Rows.FirstOrDefault(r => r.ID == ls.behaviorFrom) ?? throw new InvalidDataException("behaviour missing " + ls.behaviorFrom);
                beh.Rows.RemoveAll(r => r.ID == lt + ls.judge);
                var shot = Copy(beh, srcL, lt + ls.judge, ls.weapon + " Pale Knight R2 follow-up");
                Set(shot, "variationId", ls.variationTo);
                Set(shot, "behaviorJudgeId", ls.judge);
                beh.Rows.Add(shot);
            }
            if (ls.specialJudge > 0 && ls.specialBullet > 0)
            {
                // Its special (buffed) shot fires its own bullet, a copy of another gun's (the Cannon's).
                var sb = beh.Rows.FirstOrDefault(r => r.ID == lt + ls.specialJudge) ?? throw new InvalidDataException("behaviour missing " + (lt + ls.specialJudge));
                var srcBul = bul.Rows.FirstOrDefault(r => r.ID == ls.specialBulletFrom) ?? throw new InvalidDataException("bullet missing " + ls.specialBulletFrom);
                bul.Rows.RemoveAll(r => r.ID == ls.specialBullet);
                var direct = Copy(bul, srcBul, ls.specialBullet, ls.weapon + " buffed shot");
                bul.Rows.Add(direct);
                Set(sb, "refId", ls.specialBullet);
                // Its own, stronger hits (the Cannon's are tuned for one big hit per shot): the direct hit and the blast
                // child each get an attack copy with the sheet's correction (percent of the weapon's attack).
                if (ls.specialAtkBase > 0 && ps.TryGetValue("AtkParam_Pc", out var atkP))
                {
                    PARAM.Row StrongAtk(int fromAtk, int to, float corr)
                    {
                        var a0 = atkP.Rows.FirstOrDefault(r => r.ID == fromAtk) ?? throw new InvalidDataException("attack missing " + fromAtk);
                        atkP.Rows.RemoveAll(r => r.ID == to);
                        var na = Copy(atkP, a0, to, ls.weapon + " buffed shot");
                        foreach (var f in new[] { "atkPhysCorrection", "atkMagCorrection", "atkFireCorrection", "atkThunCorrection" }) Set(na, f, corr);
                        atkP.Rows.Add(na);
                        return na;
                    }
                    int dAtk = Convert.ToInt32(Get(direct, "atkId_Bullet"));
                    StrongAtk(dAtk, ls.specialAtkBase, ls.specialDirectCorrection);
                    Set(direct, "atkId_Bullet", ls.specialAtkBase);
                    int child = Convert.ToInt32(Get(direct, "HitBulletID"));
                    if (child > 0 && ls.specialBlastBullet > 0)
                    {
                        var cb = bul.Rows.First(r => r.ID == child);
                        bul.Rows.RemoveAll(r => r.ID == ls.specialBlastBullet);
                        var blast = Copy(bul, cb, ls.specialBlastBullet, ls.weapon + " buffed shot blast");
                        StrongAtk(Convert.ToInt32(Get(cb, "atkId_Bullet")), ls.specialAtkBase + 1, ls.specialBlastCorrection);
                        Set(blast, "atkId_Bullet", ls.specialAtkBase + 1);
                        bul.Rows.Add(blast);
                        Set(direct, "HitBulletID", ls.specialBlastBullet);
                    }
                    atkP.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
                }
            }
            foreach (var it in items.Where(i => i.key == ls.weapon))
                foreach (var row in wep.Rows.Where(r => r.ID >= it.bbWeaponId && r.ID <= it.bbWeaponId + 1000))
                    Set(row, "behaviorVariationId", ls.variationTo);
        }
        // 3. Strengthen Bullet: Bloodborne's own gun-damage buff, timed.
        var src = sp.Rows.First(x => x.ID == sh.buff.copyOf);
        sp.Rows.RemoveAll(x => x.ID == sh.buff.spEffect);
        var buff = Copy(sp, src, sh.buff.spEffect, "PaleKnight Strengthen Bullet");
        Set(buff, "effectEndurance", sh.buff.endurance);
        sp.Rows.Add(buff);
        // 4. The Pale Knight's weapon rows use them.
        foreach (var it in items.Where(i => i.key == "PaleKnight"))
            foreach (var row in wep.Rows.Where(r => r.ID >= it.bbWeaponId && r.ID <= it.bbWeaponId + 1000))
                Set(row, "behaviorVariationId", sh.behaviorVariation.to);
        foreach (var p in new[] { beh, bul, sp }) p.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
        log($"shots: {copied} Pale Knight behaviours, {sh.rows.Count} explosive shots, {atkCopies} melee attacks without wall bounce, Strengthen Bullet buff {sh.buff.endurance:0}s");
    }

    static PARAM.Row Copy(PARAM p, PARAM.Row src, int id, string name)
    {
        var row = new PARAM.Row(id, name, p.AppliedParamdef);
        for (int c = 0; c < row.Cells.Count; c++) row.Cells[c].Value = src.Cells[c].Value;
        return row;
    }
    static object Get(PARAM.Row r, string f) => r.Cells.First(x => x.Def.InternalName == f).Value;
    static void Set(PARAM.Row row, string field, object v)
    {
        var c = row.Cells.FirstOrDefault(x => x.Def.InternalName == field) ?? throw new InvalidDataException("no field " + field);
        c.Value = Convert.ChangeType(v, c.Value.GetType());
    }
}
