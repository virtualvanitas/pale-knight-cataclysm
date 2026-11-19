using CUE4Parse.FileProvider;
using SoulsFormats;

namespace LopForge;

// Movesets: one new TAE category per weapon (moves sheet bbCategory), copied from the template weapon's category in
// the player's own Bloodborne. Every slot the moves sheet lists plays the Lies of P animation converted from the
// player's own Lies of P (ForgeAnims), with the template slot's events (hits, cancels, sounds, shots) re-timed onto
// the Lies of P move's own hit mark; slots of the same family follow it; every other slot plays the template's.
public static class ForgeMoves
{
    static Dictionary<int, List<(int sfx, int dummy)>> BlastSfx = new();
    static ShotSheet.Cost? Cost;
    static Dictionary<int, (int a, int b, int ra, int rb)> Carriers = new();   // Pale Knight shot judge -> its carrier hit judges (normal, transformed form; from a reversed point)
    static HashSet<int> ReversePoints = new();
    static int ShotCat = -1;                         // the Pale Knight's moveset number
    public static void Build(IFileProvider lop, string dvd, string outDvd, List<MoveRow> moves, Action<string> log)
    {
        BlastSfx = new[] { ShotSheet.Load().blast, ShotSheet.Load().blastB }.Where(b => b is { judge: > 0 }).ToDictionary(b => b!.judge, b => b!.sfx.Select(x => (x.id, x.dummy)).ToList());
        Cost = ShotSheet.Load().bulletCost;
        var shSheet = ShotSheet.Load();
        int shotBase = 100_000_000 + shSheet.behaviorVariation.from * 1000;
        Carriers = shSheet.splitPellets ? shSheet.rows.Where(r => r.carrierJudge > 0).ToDictionary(r => r.behaviorFrom - shotBase, r => (r.carrierJudge, r.carrierJudgeB, r.carrierJudgeRev, r.carrierJudgeRevB)) : new();
        ReversePoints = shSheet.reversePoints.ToHashSet();
        ShotCat = moves.FirstOrDefault(m => m.weapon == "PaleKnight")?.bbCategory ?? -1;
        var src = Layers.Source(Path.Combine("chr", "c0000.anibnd.dcx"));
        var bnd = BND4.Read(src);
        var skel = Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(bnd.Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        var a9xPath = Layers.Source(Path.Combine("chr", "c0000_a9x.anibnd.dcx"));
        var a9x = BND4.Read(a9xPath);
        // Every animation file of the player's c0000 (to read the template slots' own lengths).
        var hkxById = new Dictionary<int, BinderFile>();
        foreach (var f in Directory.GetFiles(Path.Combine(dvd, "chr"), "c0000_*.anibnd.dcx"))
            foreach (var bf in BND4.Read(Layers.Source(Path.Combine("chr", Path.GetFileName(f)))).Files)
                if (bf.Name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) hkxById[bf.ID] = bf;
        var tmplHkx = hkxById.Values.First(f => f.Name.EndsWith("a112_030000.hkx", StringComparison.OrdinalIgnoreCase)).Bytes;
        var cache = new Dictionary<string, LopAnim>();
        // Learn how the Hunter's skinning helpers follow the main bones, from the player's own Bloodborne moves.
        var rig = new SupportRig(skel);
        var samples = new[] { 1143030000, 1143030010, 1143032200, 1143033300, 1143042100, 1143042200, 1143043300, 1112030000, 1129051000, 1129052550 }
            .Where(hkxById.ContainsKey).Select(id => { try { return BbAnim.Read(hkxById[id].Bytes); } catch { return null; } }).Where(x => x != null).Cast<BbAnim>().ToList();
        rig.Fit(samples, log);
        ForgeAnims.Rig = rig;
        var newStems = new List<string>();
        var taeCache = new Dictionary<int, TAE>();
        if (false)
        {
            // Check build: the Pale Knight's moves again on moveset 131 (a number the base game already loads).
            moves = moves.Concat(moves.Where(m => m.weapon == "PaleKnight").Select(m => new MoveRow
            {
                weapon = m.weapon, bbCategory = 131, bbAnimId = m.bbAnimId, role = m.role, templateCategory = m.templateCategory,
                templateAnimId = m.templateAnimId, lopMontage = m.lopMontage, hitMark = m.hitMark, effect = m.effect,
                stripHits = m.stripHits, spEffect = m.spEffect, hitsFrom = m.hitsFrom, shotFrom = m.shotFrom,
            })).ToList();
        }

        // Rows on shared moves (templateCategory 0: a00.tae, e.g. the idle transform 19560) are added to their weapon's
        // own moveset after it is built, with the shared move's events.
        var common = moves.Where(m => m.templateCategory == 0).ToList();
        var a00 = TAE.Read(bnd.Files.First(f => f.Name.EndsWith("\\a00.tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        foreach (var group in moves.Where(m => m.templateCategory != 0).GroupBy(m => (m.bbCategory, m.templateCategory)))
        {
            var (cat, tcat) = group.Key;
            var tmplFile = bnd.Files.FirstOrDefault(f => f.Name.EndsWith($"\\a{tcat}.tae", StringComparison.OrdinalIgnoreCase))
                           ?? throw new InvalidDataException($"template moveset a{tcat} missing");
            var tae = TAE.Read(tmplFile.Bytes);
            var pristine = TAE.Read(tmplFile.Bytes);   // the template's own events, for rows that take another slot's (eventsFrom)
            tae.ID = 2000 + cat;

            // New slots (moves sheet newSlot): the template slot's family (base, +2/+3/+5 variants, +10.. buffed) copied
            // under this row's number, before anything else, so the loop below builds them like the template's own.
            foreach (var row in group.Where(r => r.newSlot))
            {
                int t0 = row.templateAnimId, n0 = row.bbAnimId;
                if (tae.Animations.Any(x => x.ID >= n0 && x.ID < n0 + 20)) throw new InvalidDataException($"new slot family {n0} already exists in a{tcat}");
                var fam = pristine.Animations.Where(x => x.ID >= t0 && x.ID < t0 + 20).ToList();
                if (!fam.Any(x => x.ID == t0)) throw new InvalidDataException($"new slot {n0}: template slot {t0} missing in a{tcat}");
                long Remap(long id) => id == tcat * 1_000_000L + t0 ? tcat * 1_000_000L + n0 : id;   // only the base follows; other imports stay the template's
                foreach (var src0 in fam)
                {
                    TAE.Animation.AnimMiniHeader mh = src0.MiniHeader switch
                    {
                        TAE.Animation.AnimMiniHeader.Standard s => new TAE.Animation.AnimMiniHeader.Standard { ImportsHKX = s.ImportsHKX, ImportHKXSourceAnimID = s.ImportsHKX ? (int)Remap(s.ImportHKXSourceAnimID) : s.ImportHKXSourceAnimID, IsLoopByDefault = s.IsLoopByDefault, AllowDelayLoad = s.AllowDelayLoad },
                        TAE.Animation.AnimMiniHeader.ImportOtherAnim io => new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = (int)Remap(io.ImportFromAnimID), Unknown = io.Unknown },
                        _ => throw new InvalidDataException("unknown mini header")
                    };
                    var na = new TAE.Animation(n0 + (src0.ID - t0), mh, src0.AnimFileName) { Events = src0.Events.Select(Clone).ToList() };
                    tae.Animations.Add(na);
                }
                tae.Animations.Sort((x, y) => x.ID.CompareTo(y.ID));
                log($"moveset a{cat}: new slot family {n0} from {t0} ({fam.Count} slots)");
            }

            // Convert the sheet's rows.
            var ours = new Dictionary<int, (string file, ForgeAnims.Result r, MoveRow row)>();
            foreach (var row in group)
            {
                var r = ForgeAnims.Convert(lop, row, skel, tmplHkx, cache);
                string stem = $"a{cat}_{row.bbAnimId:D6}";
                newStems.Add(stem);
                int fileId = 1_000_000_000 + cat * 1_000_000 + row.bbAnimId;
                a9x.Files.RemoveAll(f => f.ID == fileId);
                a9x.Files.Add(new BinderFile(Binder.FileFlags.Flag1, fileId, $"N:\\SPRJ\\data\\INTERROOT_ps4\\chr\\c0000\\hkx\\a{cat}\\{stem}.hkx", r.Hkx));
                ours[row.newSlot ? row.bbAnimId : row.templateAnimId] = (stem, r, row);
            }

            int mine = 0, follow = 0, kept = 0; var keptIds = new List<int>();
            foreach (var a in tae.Animations)
            {
                // Which converted move (if any) this slot plays.
                (string file, ForgeAnims.Result r, MoveRow row)? use = null;
                if (ours.TryGetValue((int)a.ID, out var u)) use = u;
                else if (a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX && ours.TryGetValue(st.ImportHKXSourceAnimID % 1_000_000, out var u2) && st.ImportHKXSourceAnimID / 1_000_000 == tcat) use = u2;
                else if (ours.TryGetValue((int)(a.ID / 10 * 10), out var u3)) use = u3;
                // A slot that plays another slot of the template outright (e.g. dodge attack 32210 = 32200, 32220 = 32240)
                // plays that slot's Lies of P move (creator, 0.3.5 test: some dodge attacks were the Moonlight Sword's).
                else if (a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io0 && io0.ImportFromAnimID / 1_000_000 == tcat && ours.TryGetValue((int)(io0.ImportFromAnimID % 1_000_000), out var u4)) use = u4;

                if (use == null)
                {
                    // Kept from the template: this moveset gets its own copy of the template's animation file (from the
                    // player's own Bloodborne), registered like the converted ones, so the behaviour has a clip for it.
                    var (srcId, srcEvents) = ResolveSource(a, tcat, tae, bnd, taeCache);
                    if (hkxById.TryGetValue((int)(1_000_000_000L + srcId), out var srcFile))
                    {
                        string kstem = $"a{cat}_{a.ID:D6}";
                        int kid = 1_000_000_000 + cat * 1_000_000 + (int)a.ID;
                        a9x.Files.RemoveAll(f => f.ID == kid);
                        a9x.Files.Add(new BinderFile(Binder.FileFlags.Flag1, kid, $"N:\\SPRJ\\data\\INTERROOT_ps4\\chr\\c0000\\hkx\\a{cat}\\{kstem}.hkx", srcFile.Bytes));
                        newStems.Add(kstem);
                        if (a.Events.Count == 0 && srcEvents != null) a.Events = srcEvents.Select(Clone).ToList();
                        a.MiniHeader = new TAE.Animation.AnimMiniHeader.Standard { ImportsHKX = false, ImportHKXSourceAnimID = -1, IsLoopByDefault = (a.MiniHeader as TAE.Animation.AnimMiniHeader.Standard)?.IsLoopByDefault ?? false, AllowDelayLoad = false };
                        a.AnimFileName = kstem + ".hkt";
                    }
                    else if (a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard)
                    {
                        a.MiniHeader = new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = (int)(tcat * 1_000_000L + a.ID) };
                        a.AnimFileName = "";
                    }
                    keptIds.Add((int)a.ID); kept++;
                    continue;
                }
                var (file, res, row) = use.Value;
                if (Environment.GetEnvironmentVariable("LOP_SLOTLOG") == "1") log($"  slot a{cat}_{a.ID} plays row {row.bbAnimId} ({(ours.ContainsKey((int)a.ID) ? "own" : a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim ? "imports" : "variant")}), {a.Events.Count} template events");
                // The template slot's own events, or those of the slot it imports.
                var events = a.Events;
                var tmplLen = TemplateLength(a, tae, tcat, hkxById);
                if (a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io)
                {
                    var from = tae.Animations.FirstOrDefault(x => tcat * 1_000_000L + x.ID == io.ImportFromAnimID);
                    if (from != null && events.Count == 0) events = from.Events.Select(Clone).ToList();
                }
                if (row.eventsFrom > 0)
                {
                    // Another template slot's events outright (creator 0.3.13: forward dodge attacks never hit while the
                    // side ones, same Lies of P move, do; this gives them the side slot's events).
                    var ef = pristine.Animations.FirstOrDefault(x => x.ID == row.eventsFrom) ?? throw new InvalidDataException($"eventsFrom {row.eventsFrom} missing in a{tcat}");
                    events = ef.Events.Select(Clone).ToList();
                    tmplLen = TemplateLength(ef, pristine, tcat, hkxById);
                }
                if (row.hitsFrom > 0)
                {
                    // This move's hits/shots come from another template slot (e.g. Full Salvo fires the charged blast).
                    var hs = pristine.Animations.FirstOrDefault(x => x.ID == row.hitsFrom);   // the template's own events, not an already-built slot's
                    if (hs != null)
                    {
                        events = events.Where(e => e.Type != 1 && e.Type != 2).Select(Clone).ToList();
                        float hk = hs.Events.Where(e => e.Type == 1 || e.Type == 2).Select(e => e.StartTime).DefaultIfEmpty(0).Min();
                        float own = a.Events.Where(e => e.Type == 1 || e.Type == 2).Select(e => e.StartTime).DefaultIfEmpty(hk).Min();
                        events.AddRange(hs.Events.Where(e => e.Type == 1 || e.Type == 2).Select(e => { var c = Clone(e); c.StartTime += own - hk; c.EndTime += own - hk; return c; }));
                    }
                }
                Retime(events, tmplLen, res, row);
                if (row.stripHits) events.RemoveAll(e => e.Type == 1 || e.Type == 2);
                if (row.stripShots) events.RemoveAll(e => e.Type == 318);
                if (row.shotFrom > 0)
                {
                    // A shot (the attack of a firing template slot) wherever the Lies of P move fires.
                    var ss = pristine.Animations.FirstOrDefault(x => x.ID == row.shotFrom)
                             ?? throw new InvalidDataException($"shot slot {row.shotFrom} missing in a{tcat}");
                    var se = ss.Events.Where(e => (!row.shotOnly && (e.Type == 1 || e.Type == 2)) || e.Type == 318).ToList();   // 318 = the projectile itself
                    var fireAt = res.Montage.Marks.Where(m => m.name == "LProjectile").Select(m => m.t).Distinct().ToList();
                    if (se.Count == 0 || fireAt.Count == 0) throw new InvalidDataException($"a{cat}_{a.ID}: no shot to add (slot {row.shotFrom} attacks {se.Count}, Lies of P fire marks {fireAt.Count})");
                    float s0 = se.Min(e => e.StartTime);
                    foreach (var t in fireAt)
                        foreach (var e in se)
                        {
                            var c = Clone(e); c.StartTime = t + (e.StartTime - s0); c.EndTime = t + (e.EndTime - s0);
                            // The projectile leaves from the blade point the sheet names for this form (10000+ = normal-form part, 12000+ = transformed part).
                            if (c.Type == 318 && row.shotDummy > 0) { var sp = (byte[])c.GetParameterBytes(false).Clone(); BitConverter.GetBytes(row.shotDummy).CopyTo(sp, 0); c = new TAE.Event(c.StartTime, c.EndTime, c.Type, c.Unk04, sp, false); }
                            // Which shot it fires (moves sheet shotJudge, e.g. 970 = the cannonball).
                            if (c.Type == 318 && row.shotJudge > 0) { var sp = (byte[])c.GetParameterBytes(false).Clone(); BitConverter.GetBytes(row.shotJudge).CopyTo(sp, 8); c = new TAE.Event(c.StartTime, c.EndTime, c.Type, c.Unk04, sp, false); }
                            events.Add(c);
                        }
                }
                if (row.shotFrom > 0)
                {
                    // A transform that fires (Full Salvo, back to the normal form): the form change waits until the shot is
                    // out, since a right-hand shot only fires in the transformed form (creator 0.3.12: Full Salvo didn't fire).
                    var shots = events.Where(e => e.Type == 318).Select(e => e.StartTime).ToList();
                    foreach (var fc in events.Where(e => e.Type == 32).ToList())
                    {
                        var near = shots.Where(s => fc.StartTime >= s - 0.05f && fc.StartTime < s + 0.15f).ToList();
                        if (near.Count == 0) continue;
                        float d = near.Max() + 0.15f - fc.StartTime;
                        fc.StartTime += d; fc.EndTime += d;
                    }
                }
                if (row.blastJudge > 0)
                {
                    // The untransformed blast (shots sheet blast): a hit along and past the blade at every Lies of P fire mark.
                    var proto = tae.Animations.SelectMany(x => x.Events).First(e => e.Type == 1);
                    var fireAt = res.Montage.Marks.Where(m => m.name == "LProjectile").Select(m => m.t).Distinct().ToList();
                    if (fireAt.Count == 0) throw new InvalidDataException($"a{cat}_{a.ID}: blast row {row.bbAnimId} has no Lies of P fire mark");
                    foreach (var t in fireAt)
                    {
                        var bp = (byte[])proto.GetParameterBytes(false).Clone();
                        BitConverter.GetBytes(row.blastJudge).CopyTo(bp, 8);
                        events.Add(new TAE.Event(t, t + 0.1f, 1, proto.Unk04, bp, false));
                        // The blast's sparks along its path (shots sheet blast.sfx), as effect events like the guns' own.
                        var fxProto = tae.Animations.SelectMany(x => x.Events).FirstOrDefault(e => e.Type == 96 && e.GetParameterBytes(false).Length >= 16);
                        if (fxProto != null)
                            foreach (var (sfx, dmy) in BlastSfx.GetValueOrDefault(row.blastJudge, new()))
                            {
                                var fp = new byte[fxProto.GetParameterBytes(false).Length];
                                BitConverter.GetBytes(sfx).CopyTo(fp, 0); BitConverter.GetBytes(dmy).CopyTo(fp, 4);
                                events.Add(new TAE.Event(t, t + 0.5f, 96, fxProto.Unk04, fp, false));
                            }
                    }
                }
                if (row.spEffect > 0)
                {
                    // Apply the buff where the Lies of P move lands (its first mark after the wind-up), else halfway.
                    var p = new byte[16]; BitConverter.GetBytes(row.spEffect).CopyTo(p, 0);
                    float at = res.Duration * 0.5f;
                    events.Add(new TAE.Event(at, at + 0.1f, 66, 0, p, false));
                }
                if (row.fxFrom != "") events.AddRange(GunFx(bnd, row, res));
                if (row.trailFxFrom != "") events.AddRange(TrailFx(bnd, row, events, $"a{cat}_{a.ID}", log).ToList());
                if (row.bulletCost && Cost is { judge: > 0 } cost)
                {
                    // Every shot costs a Quicksilver Bullet: the Moonlight Sword L2's bullet-use event beside each one.
                    var useEv = pristine.Animations.FirstOrDefault(x => x.ID == cost.fromSlot)?.Events
                                  .FirstOrDefault(e => e.Type == 318 && BitConverter.ToInt32(e.GetParameterBytes(false), 8) == cost.judge)
                              ?? throw new InvalidDataException($"bullet-use event {cost.judge} not in slot {cost.fromSlot}");
                    foreach (var s in events.Where(e => e.Type == 318 && BitConverter.ToInt32(e.GetParameterBytes(false), 8) != cost.judge).ToList())
                    {
                        var c = Clone(useEv); float len = c.EndTime - c.StartTime;
                        c.StartTime = Math.Max(0, s.StartTime - 0.1f); c.EndTime = c.StartTime + len;   // just before the shot, as the L2 does
                        events.Add(c);
                    }
                }
                a.Events = events;
                // Loops (e.g. holding a charge) stay loops.
                bool loop = (a.MiniHeader as TAE.Animation.AnimMiniHeader.Standard)?.IsLoopByDefault ?? false;
                a.MiniHeader = new TAE.Animation.AnimMiniHeader.Standard { ImportsHKX = false, ImportHKXSourceAnimID = -1, IsLoopByDefault = loop, AllowDelayLoad = false };
                a.AnimFileName = file + ".hkt";
                if (ours.ContainsKey((int)a.ID)) mine++;
                else
                {
                    // A variant (with/without bullets, or with a right-hand weapon) has its own clip in the behaviour,
                    // which plays the animation named after its own slot: give it its own copy of the converted move,
                    // or the game finds no animation and holds the bind pose.
                    string vstem = $"a{cat}_{a.ID:D6}";
                    int vid = 1_000_000_000 + cat * 1_000_000 + (int)a.ID;
                    a9x.Files.RemoveAll(f => f.ID == vid);
                    a9x.Files.Add(new BinderFile(Binder.FileFlags.Flag1, vid, $"N:\\SPRJ\\data\\INTERROOT_ps4\\chr\\c0000\\hkx\\a{cat}\\{vstem}.hkx", res.Hkx));
                    newStems.Add(vstem);
                    a.AnimFileName = vstem + ".hkt";
                    follow++;
                }
            }
            foreach (var row in common.Where(m => m.bbCategory == cat))
            {
                var src0 = a00.Animations.FirstOrDefault(x => x.ID == row.templateAnimId) ?? throw new InvalidDataException($"shared move {row.templateAnimId} missing");
                // Played over exactly the shared move's length, so every one of its timed events (the mode change
                // among them) happens when the game expects it.
                float sharedLen = TemplateLength(src0, a00, 0, hkxById);
                row.duration = sharedLen;
                var r = ForgeAnims.Convert(lop, row, skel, tmplHkx, cache);
                string stem = $"a{cat}_{row.bbAnimId:D6}";
                int fileId = 1_000_000_000 + cat * 1_000_000 + row.bbAnimId;
                a9x.Files.RemoveAll(f => f.ID == fileId);
                a9x.Files.Add(new BinderFile(Binder.FileFlags.Flag1, fileId, $"N:\\SPRJ\\data\\INTERROOT_ps4\\chr\\c0000\\hkx\\a{cat}\\{stem}.hkx", r.Hkx));
                newStems.Add(stem);
                var ev = src0.Events.Select(Clone).ToList();
                if (row.stripHits) ev.RemoveAll(e => e.Type == 1 || e.Type == 2);
                if (row.spEffect > 0) { var sp = new byte[16]; BitConverter.GetBytes(row.spEffect).CopyTo(sp, 0); float at = r.Duration * 0.5f; ev.Add(new TAE.Event(at, at + 0.1f, 66, 0, sp, false)); }
                tae.Animations.RemoveAll(x => x.ID == row.bbAnimId);
                var an = new TAE.Animation(row.bbAnimId, new TAE.Animation.AnimMiniHeader.Standard { ImportsHKX = false, ImportHKXSourceAnimID = -1, IsLoopByDefault = false, AllowDelayLoad = false }, stem + ".hkt") { Events = ev };
                tae.Animations.Add(an);
                mine++;
            }
            tae.Animations.Sort((x, y) => x.ID.CompareTo(y.ID));
            // Every Pale Knight pellet shot carries its damage as a hit along the shot line (shots sheet carrierJudge /
            // carrierJudgeB by the form the shot leaves from: 10000+ launch points are the normal blade, the rest the
            // transformed one), at the shot's moment. A hit, not a second shot: each shot event costs a Quicksilver Bullet.
            if (cat == ShotCat && Carriers.Count > 0)
            {
                int added = 0;
                var proto = pristine.Animations.SelectMany(x => x.Events).First(e => e.Type == 1);
                foreach (var an in tae.Animations)
                    foreach (var e in an.Events.Where(e => e.Type == 318).ToList())
                    {
                        var ep = e.GetParameterBytes(false);
                        if (!Carriers.TryGetValue(BitConverter.ToInt32(ep, 8), out var cj)) continue;
                        int dmy = BitConverter.ToInt32(ep, 0);
                        bool normal = dmy >= 10000 && dmy < 11000, rev = ReversePoints.Contains(dmy % 1000);
                        // A shot from a reversed point (held R2: the pellets fly forward, away from the blade) carries its
                        // damage along that way too (shots sheet blastRev / blastRevB).
                        if (rev && (normal ? cj.ra : cj.rb) <= 0) throw new InvalidDataException($"a{cat}_{an.ID}: shot {BitConverter.ToInt32(ep, 8)} leaves from reversed point {dmy} but its shots row has no reversed carrier");
                        int judge = rev ? (normal ? cj.ra : cj.rb) : normal ? cj.a : cj.b;
                        if (an.Events.Any(x => x.Type == 1 && BitConverter.ToInt32(x.GetParameterBytes(false), 8) == judge && Math.Abs(x.StartTime - e.StartTime) < 0.001f)) continue;
                        var bp = (byte[])proto.GetParameterBytes(false).Clone();
                        BitConverter.GetBytes(judge).CopyTo(bp, 8);
                        // Never at the same time as another hit of the move (blade strike, blast): two hits running at once
                        // keep re-hitting (creator 0.3.23 and 0.3.29: one-hit kills), so it waits until the other ends.
                        float at = e.StartTime;
                        for (int guard = 0; guard < 8; guard++)
                        {
                            var other = an.Events.Where(x => x.Type == 1 && x.StartTime < at + 0.1f && x.EndTime > at).OrderBy(x => x.EndTime).LastOrDefault();
                            if (other == null) break;
                            at = other.EndTime + 1f / 30f;
                        }
                        an.Events.Add(new TAE.Event(at, at + 0.1f, 1, proto.Unk04, bp, false)); added++;
                    }
                log($"moveset a{cat}: {added} carrier hits on the pellet shots");
                // Any two hits still running at once in this moveset are reported (they re-hit: one-hit kills).
                foreach (var an in tae.Animations)
                {
                    var hits = an.Events.Where(x => x.Type == 1).OrderBy(x => x.StartTime).ToList();
                    for (int i = 0; i < hits.Count; i++) for (int k = i + 1; k < hits.Count; k++)
                        if (hits[k].StartTime < hits[i].EndTime - 0.001f)
                            log($"  WARNING a{cat}_{an.ID}: hits {BitConverter.ToInt32(hits[i].GetParameterBytes(false), 8)} ({hits[i].StartTime:0.00}-{hits[i].EndTime:0.00}) and {BitConverter.ToInt32(hits[k].GetParameterBytes(false), 8)} ({hits[k].StartTime:0.00}-{hits[k].EndTime:0.00}) overlap");
                }
            }
            // Every event in an event group, or the game never runs it.
            var learned = LearnGroupTypes(pristine);
            int grouped = tae.Animations.Sum(x => FixGroups(x, learned));
            log($"moveset a{cat}: {grouped} added or copied events put in event groups");
            // Every slot of the moveset gets a name and file path in the lists, as the game's own movesets do.
            newStems.AddRange(tae.Animations.Select(x => $"a{cat}_{x.ID:D6}"));
            var name = tmplFile.Name.Replace($"a{tcat}.tae", $"a{cat}.tae", StringComparison.OrdinalIgnoreCase);
            int id = 5000000 + cat;
            bnd.Files.RemoveAll(f => f.ID == id);
            bnd.Files.Add(new BinderFile(tmplFile.Flags, id, name, tae.Write()));
            log($"moveset: a{cat} from a{tcat}: {mine} Lies of P moves, {follow} variants following them, {kept} kept from the template: {string.Join(",", keptIds)}");
        }
        newStems = newStems.Distinct().ToList();
        // The behaviour's selectors get a clip for each new moveset, cloned from its template's.
        var behFile = bnd.Files.First(f => f.Name.EndsWith(@"Export\Behaviors\c0000.hkx", StringComparison.OrdinalIgnoreCase));
        var clipNames = new List<string>();
        behFile.Bytes = BehaviorClips.AddMovesets(behFile.Bytes, moves.Where(m => m.templateCategory != 0).Select(m => (m.bbCategory, m.templateCategory)).Distinct(), log, clipNames);
        foreach (var g in common.GroupBy(m => m.bbCategory))
            behFile.Bytes = BehaviorClips.AddCommonOverrides(behFile.Bytes, g.Key, g.Select(m => m.bbAnimId).ToHashSet(), log, clipNames);
        // Selectors that ask for a new slot family (moves sheet selectorFrom, e.g. the left-gun roll shots to the left
        // and right): every gun they hold gets the new slot; the new moveset's own are its new rows (newSlot), every
        // other gun's plays exactly its old move (ImportOtherAnim of the old slot, as Bloodborne's own movesets do).
        foreach (var row in moves.Where(m => m.selectorFrom != ""))
        {
            if (!row.newSlot) throw new InvalidDataException($"row {row.bbCategory}:{row.bbAnimId}: selectorFrom needs newSlot");
            var moved = new List<(int cat, int oldId, int newId)>();
            behFile.Bytes = BehaviorClips.RetargetSelectors(behFile.Bytes, new System.Text.RegularExpressions.Regex(row.selectorFrom), row.templateAnimId, row.bbAnimId, log, clipNames, moved);
            foreach (var g in moved.Where(x => x.cat != row.bbCategory).Distinct().GroupBy(x => x.cat))
            {
                var tf = bnd.Files.FirstOrDefault(f => f.ID == 5000000 + g.Key) ?? throw new InvalidDataException($"moveset a{g.Key} (in a selector of {row.bbAnimId}) has no TAE");
                var t = TAE.Read(tf.Bytes);
                int addedSlots = 0;
                foreach (var (c, oldId, newId) in g)
                {
                    if (t.Animations.Any(x => x.ID == newId)) continue;
                    if (!t.Animations.Any(x => x.ID == oldId)) log($"  note: a{c} has no slot {oldId}; its new slot {newId} imports it anyway (as its selector clip did)");
                    t.Animations.Add(new TAE.Animation(newId, new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = c * 1_000_000 + oldId }, ""));
                    newStems.Add($"a{c:D3}_{newId:D6}");
                    addedSlots++;
                }
                t.Animations.Sort((x, y) => x.ID.CompareTo(y.ID));
                tf.Bytes = t.Write();
                log($"moveset a{g.Key}: {addedSlots} new slots from {row.bbAnimId} play its own old ones");
            }
        }
        newStems = newStems.Concat(clipNames).Distinct().ToList();
        // Register every new animation in the Hunter's character file, or the game never plays it.
        var charFile = bnd.Files.First(f => f.Name.EndsWith(@"Export\Characters\c0000.hkx", StringComparison.OrdinalIgnoreCase));
        int before = CharNames.Read(charFile.Bytes).Count;
        charFile.Bytes = CharNames.Add(charFile.Bytes, newStems);
        var after = CharNames.Read(charFile.Bytes);
        if (!newStems.All(after.Contains)) throw new InvalidDataException("animation names were not registered");
        log($"animation list: {before} -> {after.Count} names");
        // ...and their files in the Hunter's project file (hkbProjectStringData animation filenames).
        var projFile = bnd.Files.First(f => f.Name.EndsWith(@"Action\c0000\Export\c0000.hkx", StringComparison.OrdinalIgnoreCase));
        const string projKey = @"..\..\..\Model\chr\c0000\hkx\a000\a000_000000.hkx";
        var paths = newStems.Select(st => $@"..\..\..\Model\chr\c0000\hkx\{st[..4]}\{st}.hkx").ToList();
        int pBefore = CharNames.Read(projFile.Bytes, projKey).Count;
        projFile.Bytes = CharNames.Add(projFile.Bytes, paths, projKey);
        var pAfter = CharNames.Read(projFile.Bytes, projKey);
        if (!paths.All(pAfter.Contains)) throw new InvalidDataException("animation files were not registered");
        log($"animation files: {pBefore} -> {pAfter.Count}");
        bnd.Files.Sort((x, y) => x.ID.CompareTo(y.ID));
        a9x.Files.Sort((x, y) => x.ID.CompareTo(y.ID));
        var dst = Path.Combine(outDvd, "chr", "c0000.anibnd.dcx");
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        bnd.Write(dst);
        a9x.Write(Path.Combine(outDvd, "chr", "c0000_a9x.anibnd.dcx"));
    }

    // The animation (full id, e.g. 143032500) a template slot really plays, and the events it inherits.
    static (long id, List<TAE.Event>? events) ResolveSource(TAE.Animation a, int tcat, TAE tae, BND4 bnd, Dictionary<int, TAE> taeCache)
    {
        long id = tcat * 1_000_000L + a.ID;
        var cur = a; var curTae = tae; int curCat = tcat;
        List<TAE.Event>? ev = a.Events.Count > 0 ? a.Events : null;
        for (int hop = 0; hop < 6; hop++)
        {
            long next;
            if (cur.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io) next = io.ImportFromAnimID;
            else if (cur.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX) next = st.ImportHKXSourceAnimID;
            else break;
            int nc = (int)(next / 1_000_000);
            if (!taeCache.TryGetValue(nc, out var nt))
            {
                var tf = bnd.Files.FirstOrDefault(f => f.ID == 5000000 + nc);
                nt = tf != null ? TAE.Read(tf.Bytes) : null!;
                taeCache[nc] = nt;
            }
            id = next;
            var na = nt?.Animations.FirstOrDefault(x => x.ID == next % 1_000_000);
            if (na == null) break;
            if (ev == null && na.Events.Count > 0) ev = na.Events;
            cur = na; curTae = nt!; curCat = nc;
        }
        return (id, ev);
    }

    // Another weapon's gun shot (e.g. "a104:41000", the Rifle Spear's): its bullet, muzzle flash and weapon sounds,
    // placed at every moment the Lies of P move fires (LProjectile), timed from the shot itself.
    static readonly Dictionary<string, TAE> FxTae = new();
    static IEnumerable<TAE.Event> GunFx(BND4 bnd, MoveRow row, ForgeAnims.Result res)
    {
        var parts = row.fxFrom.Split(':');
        if (!FxTae.TryGetValue(parts[0], out var t))
            FxTae[parts[0]] = t = TAE.Read(bnd.Files.First(f => f.Name.EndsWith("\\" + parts[0] + ".tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        var an = t.Animations.First(x => x.ID == long.Parse(parts[1]));
        var shot = an.Events.FirstOrDefault(e => e.Type == 318) ?? throw new InvalidDataException($"{row.fxFrom}: no shot event");
        // With shotFrom set the projectile itself comes from our own proven firing slot; only the flash and sounds are borrowed.
        var keep = an.Events.Where(e => (e.Type == 318 && row.shotFrom == 0 && row.blastJudge == 0 && !row.fxOnly) || e.Type == 96 || e.Type == 112 || (e.Type == 128 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) == 1)).ToList();
        var fire = res.Montage.Marks.Where(m => m.name == "LProjectile").Select(m => m.t).Distinct().ToList();
        if (fire.Count == 0) throw new InvalidDataException($"{row.lopMontage}: no fire mark for {row.fxFrom}");
        foreach (var at in fire)
            foreach (var e in keep)
            {
                var c = Clone(e); c.StartTime = Math.Max(0, at + e.StartTime - shot.StartTime); c.EndTime = Math.Max(c.StartTime, at + e.EndTime - shot.StartTime);
                if (e.Type == 318 && row.shotJudge > 0)
                {
                    // The borrowed gun's own shot, with the sheet's judge (e.g. 980 = the Cataclysm's R2 follow-up).
                    var sp = c.GetParameterBytes(false); BitConverter.GetBytes(row.shotJudge).CopyTo(sp, 8);
                    c = new TAE.Event(c.StartTime, c.EndTime, c.Type, c.Unk04, sp, false);
                }
                if (e.Type == 96)
                {
                    // Effects on the other weapon's model points (1x000 + point) go on our model's point fxDummy (sheet),
                    // on its main part (10000 + point): the other weapon's own point may be on a part that isn't shown.
                    var p = c.GetParameterBytes(false); int d = BitConverter.ToInt32(p, 4);
                    if (d >= 10000 && d < 14000) { BitConverter.GetBytes(row.fxDummy >= 10000 ? row.fxDummy : 10000 + (row.fxDummy > 0 ? row.fxDummy : d % 1000)).CopyTo(p, 4); c = new TAE.Event(c.StartTime, c.EndTime, c.Type, c.Unk04, p, false); }
                }
                yield return c;
            }
    }

    // Another weapon slot's effects around its first hit (moves sheet trailFxFrom, e.g. "a145:40000": the Boom Hammer's
    // transformed R1 setting its head on fire), laid on this move's first blade hit with the same lead time: its effects
    // (type 96) and swing sounds (type 128, kind 1) that run in [hit - 0.5 s, hit + 0.1 s]. Effects on its weapon points
    // (10000-13999) go to trailFxPoints, in the order its distinct weapon points appear; body points stay. They end at
    // most trailFxTail after this move's hit ends.
    static IEnumerable<TAE.Event> TrailFx(BND4 bnd, MoveRow row, List<TAE.Event> ours, string slot, Action<string> log)
    {
        var parts = row.trailFxFrom.Split(':');
        if (!FxTae.TryGetValue(parts[0], out var t))
            FxTae[parts[0]] = t = TAE.Read(bnd.Files.First(f => f.Name.EndsWith("\\" + parts[0] + ".tae", StringComparison.OrdinalIgnoreCase)).Bytes);
        var an = t.Animations.FirstOrDefault(x => x.ID == long.Parse(parts[1])) ?? throw new InvalidDataException($"{row.trailFxFrom}: slot missing");
        var srcHit = an.Events.Where(e => e.Type == 1).OrderBy(e => e.StartTime).FirstOrDefault() ?? throw new InvalidDataException($"{row.trailFxFrom}: no hit");
        var hit = ours.Where(e => e.Type == 1).OrderBy(e => e.StartTime).FirstOrDefault() ?? throw new InvalidDataException($"{slot}: trailFx needs a blade hit");
        float shift = hit.StartTime - srcHit.StartTime, stop = hit.EndTime + row.trailFxTail;
        var points = row.trailFxPoints.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        var seen = new List<int>();
        var take = an.Events.Where(e => (e.Type == 96 || (e.Type == 128 && BitConverter.ToInt32(e.GetParameterBytes(false), 0) == 1))
                                        && e.StartTime >= srcHit.StartTime - 0.5f && e.StartTime <= srcHit.StartTime + 0.1f).ToList();
        if (!take.Any(e => e.Type == 96)) throw new InvalidDataException($"{row.trailFxFrom}: no effects around its hit");
        int n = 0;
        foreach (var e in take)
        {
            var c = Clone(e);
            c.StartTime = Math.Max(0, e.StartTime + shift);
            c.EndTime = Math.Max(c.StartTime + 0.03f, Math.Min(e.EndTime + shift, stop));
            if (e.Type == 96)
            {
                var p = (byte[])c.GetParameterBytes(false).Clone(); int d = BitConverter.ToInt32(p, 4);
                if (d >= 10000 && d < 14000)
                {
                    if (!seen.Contains(d)) seen.Add(d);
                    int k = seen.IndexOf(d);
                    if (k >= points.Count) throw new InvalidDataException($"{slot}: trailFx source point {d} has no trailFxPoints entry ({row.trailFxPoints})");
                    BitConverter.GetBytes(points[k]).CopyTo(p, 4);
                }
                c = new TAE.Event(c.StartTime, c.EndTime, c.Type, c.Unk04, p, false);
                if (e.Group != null) OrigGroup[c] = e.Group.GroupType;
            }
            n++;
            yield return c;
        }
        log($"  {slot}: {n} effects/sounds from {row.trailFxFrom} on its hit at {hit.StartTime:0.00}s (weapon points {string.Join(",", seen)} -> {row.trailFxPoints})");
    }

    static TAE.Event Clone(TAE.Event e)
    {
        var c = new TAE.Event(e.StartTime, e.EndTime, e.Type, e.Unk04, (byte[])e.GetParameterBytes(false).Clone(), false);
        if (e.Group != null) OrigGroup[c] = e.Group.GroupType;
        return c;
    }

    // Bloodborne only runs the events of a move that belong to one of its event groups (attacks in group 1, shots in
    // group 2, effects in 96...). Every event this tool adds or copies starts in no group, so the game skipped it
    // (creator 0.3.14: L2 used bullets but its added shot never fired). Each such event gets a group of the type its
    // source event had, else the type the template moveset uses for that kind of event; empty groups are dropped.
    static readonly Dictionary<TAE.Event, int> OrigGroup = new(ReferenceEqualityComparer.Instance);
    static Dictionary<int, int> LearnGroupTypes(TAE t) =>
        t.Animations.SelectMany(a => a.Events).Where(e => e.Group != null)
         .GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.GroupBy(e => e.Group.GroupType).OrderByDescending(x => x.Count()).First().Key);
    static int FixGroups(TAE.Animation a, Dictionary<int, int> learned)
    {
        int fixedCount = 0;
        foreach (var e in a.Events.Where(e => e.Group == null || !a.EventGroups.Contains(e.Group)))
        {
            int gt = OrigGroup.TryGetValue(e, out var og) ? og : learned.TryGetValue(e.Type, out var lt) ? lt : e.Type;
            var g = new TAE.EventGroup(gt);
            var gd = g.GroupData; gd.DataType = (TAE.EventGroup.EventGroupDataType)gt; g.GroupData = gd;
            a.EventGroups.Add(g);
            e.Group = g;
            fixedCount++;
        }
        a.EventGroups.RemoveAll(g => !a.Events.Any(e => e.Group == g));
        return fixedCount;
    }

    // Length of the animation the template slot plays (its own file, or the one it imports).
    static float TemplateLength(TAE.Animation a, TAE tae, int tcat, Dictionary<int, BinderFile> hkxById)
    {
        long animId = tcat * 1_000_000L + a.ID;
        for (int hop = 0; hop < 4; hop++)
        {
            if (a.MiniHeader is TAE.Animation.AnimMiniHeader.ImportOtherAnim io) animId = io.ImportFromAnimID;
            else if (a.MiniHeader is TAE.Animation.AnimMiniHeader.Standard st && st.ImportsHKX) animId = st.ImportHKXSourceAnimID;
            else break;
            var next = tae.Animations.FirstOrDefault(x => tcat * 1_000_000L + x.ID == animId);
            if (next == null) break;
            a = next;
        }
        if (hkxById.TryGetValue((int)(1_000_000_000L + animId), out var bf))
            try { return BbHavok.Find<HKX2.hkaAnimation>(BbHavok.Read(bf.Bytes))!.m_duration; } catch { }
        return a.Events.Count > 0 ? a.Events.Max(e => e.EndTime) : 2f;
    }

    // Piecewise-linear re-timing: the template's first hit (attack or shot) lands on the Lies of P move's hit mark,
    // and the template's end on the Lies of P move's end.
    static void Retime(List<TAE.Event> events, float tmplLen, ForgeAnims.Result res, MoveRow row)
    {
        var hits = events.Where(e => e.Type == 1 || e.Type == 2).OrderBy(e => e.StartTime).ToList();
        // A move with no hit mark but a hitTime (moves sheet; e.g. the Cataclysm's side dashes) puts the template's first
        // hit, or else its first shot, at that moment of the Lies of P move.
        if (hits.Count == 0 && row.hitTime >= 0) hits = events.Where(e => e.Type == 318).OrderBy(e => e.StartTime).ToList();
        float keyT = hits.Count > 0 ? hits[0].StartTime : tmplLen * 0.35f;
        float keyL = row.hitMark != "" ? res.Montage.First(row.hitMark, -1) : -1;
        if (keyL < 0 && row.hitTime >= 0) keyL = Math.Min(row.hitTime, res.Duration - 0.05f);
        if (keyL < 0) keyL = res.Duration * (keyT / Math.Max(0.01f, tmplLen));
        float endT = Math.Max(tmplLen, keyT + 0.05f), endL = Math.Max(res.Duration, keyL + 0.05f);
        // Second anchor (creator, 0.3.6 test: recovery too slow to get away): the template's recovery (the first lasting
        // control flag after its hits) lands where Lies of P lets Pino act again (its first ApplyInput_ mark after the hit).
        float lastEnd = events.Count > 0 ? events.Max(e => e.EndTime) : tmplLen;
        float hitEnd = hits.Count > 0 ? hits.Max(e => e.EndTime) : keyT;
        float recT = events.Where(e => e.Type == 0 && e.StartTime > hitEnd && e.EndTime >= lastEnd - 0.05f).Select(e => e.StartTime).DefaultIfEmpty(-1).Min();
        float recL = res.Montage.Marks.Where(m => m.name.Contains("ApplyInput_") && m.t > keyL).Select(m => m.t).DefaultIfEmpty(-1).Min();
        bool two = recT > keyT && recT < endT - 0.05f && recL > keyL && recL < endL - 0.05f;
        float Map(float t)
        {
            if (t <= keyT) return t * (keyL / Math.Max(0.001f, keyT));
            if (!two) return keyL + (t - keyT) * ((endL - keyL) / Math.Max(0.001f, endT - keyT));
            if (t <= recT) return keyL + (t - keyT) * ((recL - keyL) / (recT - keyT));
            return recL + (t - recT) * ((endL - recL) / (endT - recT));
        }
        foreach (var e in events) { float s = Map(e.StartTime), en = Map(e.EndTime); e.StartTime = s; e.EndTime = Math.Max(s, en); }
    }
}


