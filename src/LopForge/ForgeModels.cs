using System.Numerics;
using CUE4Parse.FileProvider;
using SoulsFormats;

namespace LopForge;

// Weapon models: each item's Lies of P mesh and textures (from the player's own copy) placed into a copy of
// the template Bloodborne weapon's parts file (from the player's own copy), under the item's new model number.
public static class ForgeModels
{
    internal sealed class Piece
    {
        public List<Vector3> P = new(), N = new();
        public List<Vector4> T = new();
        public List<Vector2> Uv = new();
        public List<Vector2> Uv2 = new();
        public List<int> Tris = new();
        // Where the template's hit/effect points go: rotated by Align about Origin, stretched along Along.
        public Quaternion Align = Quaternion.Identity; public Vector3 Origin, Along = -Vector3.UnitY;
        public float LengthOverride;   // >0: the length the template's points stretch to (the model as Lies of P ships it)
        // Moving part (items partAnimFrom): which points ride the weapon's own barrel bone, and how far it slides out.
        public float[]? Moving; public Vector3 MoveDelta;   // Moving: each point's share of the slide (0 = stays)
    }

    public static void Build(IFileProvider lop, string dvd, string outDvd, List<ItemRow> items, Action<string> log)
    {
        BbSkel ??= Retarget.BbSkel.From(BbHavok.Find<HKX2.hkaSkeleton>(BbHavok.Read(BND4.Read(Layers.Source(Path.Combine("chr", "c0000.anibnd.dcx"))).Files.First(f => f.Name.EndsWith("skeleton.HKX", StringComparison.OrdinalIgnoreCase)).Bytes))!);
        foreach (var it in items)
        {
            var mesh = LopMesh.Load(lop, it.lopMesh);
            // A re-posed model (shorter barrels) keeps the aim of its points from the model as Lies of P ships it; only
            // their length follows the new shape.
            Piece? frame = string.IsNullOrWhiteSpace(it.meshBoneOffsets) ? null : Place(mesh, it);
            PoseBones(mesh, it.meshBoneOffsets);
            var piece = Place(mesh, it);
            if (frame != null)
            {
                piece.Along = frame.Along; piece.Align = frame.Align; piece.Origin = frame.Origin;
                piece.LengthOverride = frame.P.Max(p => Vector3.Dot(p - frame.Origin, frame.Along));
                // The points the re-pose moved are the sliding part (the Cataclysm's barrels); the move undone is how
                // far they slide out again (Lies of P's charged pose), in the weapon's own space.
                // Points only partly on the moving bone in Lies of P (the barrel tubes stretch: front end on the slider,
                // back end on the base) get that share of the slide as their weight.
                if (it.partAnimFrom > 0 && frame.P.Count == piece.P.Count)
                {
                    var diffs = Enumerable.Range(0, piece.P.Count).Select(i => frame.P[i] - piece.P[i]).ToArray();
                    var full = diffs.MaxBy(d => d.Length());
                    if (full.Length() < 0.01f) throw new InvalidDataException("moving part: no points moved for " + it.key);
                    piece.MoveDelta = full;
                    piece.Moving = new float[piece.P.Count];
                    int n = 0, whole = 0;
                    for (int i = 0; i < diffs.Length; i++)
                    {
                        float w = Math.Clamp(Vector3.Dot(diffs[i], full) / full.LengthSquared(), 0, 1);
                        if (w < 0.01f) continue;
                        piece.Moving[i] = w; n++; if (w > 0.99f) whole++;
                    }
                    log($"moving part: {it.key} {n} points ride the slide ({whole} fully), {full.Length():0.000} m");
                }
            }
            int axis = it.fit == "forearm-gun" ? 2 : 1;   // the weapon's length runs along -Z (guns) or -Y (blades)

            var bc = LopTex.Load(lop, it.texBC, it.texSize);
            var arm = LopTex.Load(lop, it.texARM, it.texSize);
            var nrm = LopTex.Load(lop, it.texN, it.texSize);
            var maps = LopTex.ToBloodborne(bc, arm, nrm, it.texSize);

            foreach (var suffix in new[] { "", "_l" })
            {
                var src = Layers.Source(Path.Combine("parts", $"wp_a_{it.templateModelId:D4}{suffix}.partsbnd.dcx"));
                var dst = Path.Combine(outDvd, "parts", $"wp_a_{it.equipModelId:D4}{suffix}.partsbnd.dcx");
                var bnd = BND4.Read(src);
                string oldTag = $"WP_A_{it.templateModelId:D4}", newTag = $"WP_A_{it.equipModelId:D4}";
                PartAnim? pa = piece.Moving != null ? PartAnim.From(it, suffix) : null;
                foreach (var f in bnd.Files)
                {
                    var lower = f.Name.ToLowerInvariant();
                    if (lower.EndsWith(".tpf"))
                    {
                        var tpf = TPF.Read(f.Bytes);
                        var size = suffix == "_l" ? 512 : it.texSize;
                        var m = suffix == "_l" ? LopTex.ToBloodborne(bc, arm, nrm, size) : maps;
                        for (int i = 0; i < tpf.Textures.Count; i++)
                        {
                            var t = tpf.Textures[i];
                            var name = t.Name.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
                            // low-detail copies are named ..._a_l etc.
                            var kind = t.Name.EndsWith("_l", StringComparison.OrdinalIgnoreCase) ? t.Name[..^2] : t.Name;
                            var img = kind.EndsWith("_a") ? m.a : kind.EndsWith("_r") ? m.r : kind.EndsWith("_s") ? m.s : kind.EndsWith("_n") ? m.n : null;
                            if (img != null) tpf.Textures[i] = LopTex.ToTpf(img, name, t);
                            else t.Name = name;
                        }
                        f.Bytes = tpf.Write();
                    }
                    else if (lower.EndsWith(".flver"))
                    {
                        var fl = FLVER2.Read(f.Bytes);
                        bool secondary = Path.GetFileNameWithoutExtension(f.Name).EndsWith("_1") && it.fit == "blade-z";
                        Fill(fl, piece, secondary, axis, oldTag, newTag);
                        AddReach(fl, piece, it.reachDummies);
                        AddBack(fl, piece, it.backDummies);
                        // The held model (not the _1 copy, static in the Hunter Pistol too) rides the package's bones.
                        var stemName = Path.GetFileNameWithoutExtension(f.Name);
                        if (pa != null && !stemName.EndsWith("_1") && !stemName.EndsWith("_1_L", StringComparison.OrdinalIgnoreCase)) pa.Rig(fl, piece);
                        f.Bytes = fl.Write();
                    }
                    f.Name = f.Name.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
                }
                // The template's second model (the Moonlight Sword's sheath, worn on the back) is emptied above, but its
                // cloth collision shape stayed: the Hunter's coat draped over an invisible sheath (creator 2026-11-13).
                if (it.fit == "blade-z")
                {
                    int dropped = bnd.Files.RemoveAll(x => Path.GetFileName(x.Name).EndsWith("_1_Collidable.hkx", StringComparison.OrdinalIgnoreCase));
                    if (dropped > 0) log($"model: {it.key}{suffix} sheath collision removed ({dropped})");
                }
                if (pa != null)
                {
                    bnd.Files.RemoveAll(x => x.Name.EndsWith(".anibnd", StringComparison.OrdinalIgnoreCase));
                    bnd.Files.Add(pa.Package(it, piece));
                    bnd.Files.Sort((a, b) => a.ID.CompareTo(b.ID));
                    log($"part animation: {it.key}{suffix} barrels slide out on {pa.ExtendCount} buffed-shot moves");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                bnd.Write(dst);
            }
            log($"model: {it.key} -> wp_a_{it.equipModelId:D4} ({piece.P.Count} points, {piece.Tris.Count / 3} triangles)");
        }
    }

    // The Lies of P mesh in Bloodborne's weapon space (metres, handle at the origin).
    static Piece Place(LopMesh m, ItemRow it)
    {
        var keep = new HashSet<int>();
        for (int i = 0; i < m.Bones.Count; i++)
            if (it.keepBones.Count == 0 || it.keepBones.Contains(m.Bones[i].Name)) keep.Add(i);
        int Dom(LopMesh.Vert v) { int b = 0; for (int k = 1; k < 4; k++) if (v.Bw[k] > v.Bw[b]) b = k; return v.Bi[b]; }

        Func<Vector3, Vector3> pos, dir;
        if (it.fit == "blade-z")
        {
            // Edge forward: turned half a turn about the blade (creator check 2026-11-09: the edge was upside down).
            // Grip (creator 2026-11-10): the hand sits gripShift metres further toward the trigger (the model moves toward
            // the pommel, +Y), and the blade tilts gripTilt degrees about the hand toward +X, which is down in the
            // shoulder-carry pose, so it rests on the shoulder.
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, it.gripTilt * MathF.PI / 180f);
            var shift = new Vector3(0, it.gripShift, 0);
            pos = p => Vector3.Transform(new Vector3(-p.X, -p.Z, -p.Y) / 100f + shift, tilt);
            dir = d => Vector3.Transform(new Vector3(-d.X, -d.Z, -d.Y), tilt);
        }
        else if (it.fit == "forearm-gun")
        {
            // Back of the gauntlet = forearm/twist points; muzzle = the moving barrel parts.
            var back = Centroid(m, v => { var n = m.Bones[Dom(v)].Name; return n.Contains("Forearm") || n.Contains("ForeTwist"); });
            var muzzle = Centroid(m, v => { var n = m.Bones[Dom(v)].Name; return n.StartsWith("BN_ChargeShotgun_") && n != "BN_ChargeShotgun_Base"; });
            var f = Vector3.Normalize(muzzle - back);
            var u = Vector3.Normalize(Vector3.UnitZ - f * Vector3.Dot(Vector3.UnitZ, f));
            var s = Vector3.Cross(u, f);
            var origin = back + (muzzle - back) * 0.35f;
            dir = d => new Vector3(-Vector3.Dot(d, s), Vector3.Dot(d, u), -Vector3.Dot(d, f));
            pos = p => dir(p - origin) / 100f;
        }
        else if (it.fit == "lop-hand")
        {
            // Exactly where it sits on Pino's arm, relative to the left hand: the converted Lies of P moves turn the
            // Hunter's hand as Pino's (times the hand's roll fix K), so the model is placed by the inverse of that fix,
            // then expressed in the Hunter's left weapon bone (creator: on the arm like in Lies of P, aimed right).
            var (mp, mr) = Retarget.Model(m.Bones.Select(b => b.Parent).ToList(), i => m.Bones[i].T, i => m.Bones[i].R);
            int h = m.Bones.FindIndex(b => b.Name == "Bip001-L-Hand");
            if (h < 0 || BbSkel == null) throw new InvalidDataException("lop-hand fit needs the hand bones");
            var (_, bRestR) = Retarget.Model(BbSkel.Parent, i => BbSkel.T[i], i => BbSkel.R[i]);
            int bh = BbSkel.Index("L_Hand"), bw = BbSkel.Index("L_Weapon");
            var k = ForgeAnims.HandFix(mr[h], bRestR[bh]);
            var toHand = Quaternion.Inverse(mr[h]); var unK = Quaternion.Inverse(k); var unW = Quaternion.Inverse(BbSkel.R[bw]);
            var tw = BbSkel.T[bw]; var ph = mp[h];
            dir = d => Vector3.Transform(Vector3.Transform(Vector3.Transform(d, toHand), unK), unW);
            pos = p => Vector3.Transform(Vector3.Transform(Vector3.Transform(p - ph, toHand), unK) / 100f - tw, unW);
        }
        else if (it.fit == "forearm-sleeve")
        {
            // Over the Hunter's left forearm like Pino's Legion arm (creator, 0.3.4 test: it faced backwards; it should
            // slide over the forearm): Pino's forearm (elbow -> wrist, bind pose) is laid along the Hunter's (rest pose),
            // elbow on elbow, scaled to the Hunter's forearm length, then expressed in the Hunter's left weapon bone.
            var (mp, _) = Retarget.Model(m.Bones.Select(b => b.Parent).ToList(), i => m.Bones[i].T, i => m.Bones[i].R);
            int le = m.Bones.FindIndex(b => b.Name == "Bip001-L-Forearm"), lh = m.Bones.FindIndex(b => b.Name == "Bip001-L-Hand");
            if (le < 0 || lh < 0 || BbSkel == null) throw new InvalidDataException("forearm-sleeve fit needs the forearm bones");
            var (bp, br) = Retarget.Model(BbSkel.Parent, i => BbSkel.T[i], i => BbSkel.R[i]);
            int be = BbSkel.Index("L_Forearm"), bh = BbSkel.Index("L_Hand"), bw = BbSkel.Index("L_Weapon");
            Vector3 G(Vector3 ue) => ForgeAnims.Gp(ue);
            Vector3 lopE = G(mp[le]), lopH = G(mp[lh]);
            Vector3 dL = Vector3.Normalize(lopH - lopE), dB = Vector3.Normalize(bp[bh] - bp[be]);
            float scale = Vector3.Distance(bp[be], bp[bh]) / MathF.Max(1e-4f, Vector3.Distance(lopE, lopH));
            // Roll: the back of Pino's hand (bind pose, perpendicular to the forearm) onto the back of the Hunter's.
            var q1 = FromTo(dL, dB);
            Vector3 Perp(Vector3 v, Vector3 axis) { var x = v - axis * Vector3.Dot(v, axis); return x.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(x); }
            var upL = Perp(Vector3.Transform(Vector3.UnitY, q1), dB);
            var upB = Perp(Vector3.UnitY, dB);
            float ang = MathF.Atan2(Vector3.Dot(Vector3.Cross(upL, upB), dB), Vector3.Dot(upL, upB));
            var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(dB, ang) * q1);
            q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(dB, it.sleeveRoll * MathF.PI / 180f) * q);
            // The game hangs the weapon on the body model's attach point (attachDummy, e.g. 345 for a left-hand gun), whose
            // frame is turned against the hand bone: the model is expressed in that frame (0.3.4 used the bone and faced backwards).
            Quaternion frameR = br[bw]; Vector3 frameP = bp[bw];
            if (it.attachDummy > 0) (frameP, frameR) = BodyDummy((short)it.attachDummy);
            var unW = Quaternion.Inverse(frameR); var pw = frameP; var be0 = bp[be];
            dir = d => Vector3.Transform(Vector3.Transform(new Vector3(-d.Y, d.Z, -d.X), q), unW);
            pos = p => Vector3.Transform(be0 + Vector3.Transform((G(p) - lopE) * scale, q) - pw, unW);
        }
        else throw new InvalidDataException("unknown fit " + it.fit);

        var o = new Piece();
        var map = new int[m.Verts.Count];
        for (int i = 0; i < m.Verts.Count; i++)
        {
            var v = m.Verts[i];
            if (!keep.Contains(Dom(v))) { map[i] = -1; continue; }
            map[i] = o.P.Count;
            o.P.Add(pos(v.P));
            o.N.Add(Vector3.Normalize(dir(v.N)));
            var t = dir(new Vector3(v.Tan.X, v.Tan.Y, v.Tan.Z));
            o.T.Add(new Vector4(t, v.Tan.W == 0 ? 1 : v.Tan.W));
            o.Uv.Add(v.Uv);
        }
        foreach (var s in m.Sections)
            for (int i = 0; i + 2 < s.Tris.Length; i += 3)
            {
                int a = map[s.Tris[i]], b = map[s.Tris[i + 1]], c = map[s.Tris[i + 2]];
                if (a < 0 || b < 0 || c < 0) continue;
                o.Tris.Add(a); o.Tris.Add(b); o.Tris.Add(c);
            }
        if (o.Tris.Count == 0) throw new InvalidDataException("no triangles kept for " + it.key);
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var p in o.P) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        int along = it.fit == "forearm-gun" ? 2 : 1;
        foreach (var p in o.P)
        {
            float u = (p.X - lo.X) / MathF.Max(1e-4f, hi.X - lo.X);
            float w = along == 1 ? (p.Y - lo.Y) / MathF.Max(1e-4f, hi.Y - lo.Y) : (p.Z - lo.Z) / MathF.Max(1e-4f, hi.Z - lo.Z);
            o.Uv2.Add(new Vector2(0.02f + 0.96f * u, 0.02f + 0.96f * w));
        }
        if (it.fit == "blade-z")
        {
            o.Align = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, it.gripTilt * MathF.PI / 180f);
            o.Along = Vector3.Transform(-Vector3.UnitY, o.Align);
        }
        else
        {
            // Barrel: from the back of the gauntlet to the muzzle parts; the template gun's points follow it.
            var back = pos(Centroid(m, v => { var n = m.Bones[Dom(v)].Name; return n.Contains("Forearm") || n.Contains("ForeTwist"); }));
            var muzzle = pos(Centroid(m, v => { var n = m.Bones[Dom(v)].Name; return n.StartsWith("BN_ChargeShotgun_") && n != "BN_ChargeShotgun_Base"; }));
            o.Along = Vector3.Normalize(muzzle - back);
            o.Origin = Vector3.Zero;   // the template gun's points are measured from its grip, as ours from the hand
            o.Align = FromTo(-Vector3.UnitZ, o.Along);

        }
        return o;
    }

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        var axis = Vector3.Cross(a, b); float d = Vector3.Dot(a, b);
        if (axis.LengthSquared() < 1e-8f) return d > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return Quaternion.Normalize(new Quaternion(axis, 1 + d));
    }

    public static Retarget.BbSkel? BbSkel;

    // A dummy point of the Hunter's body model in model space (bind pose): Z = its forward, Y = its up.
    public static (Vector3 p, Quaternion r) BodyDummy(short id, string? chrbndPath = null)
    {
        var chrbnd = BND4.Read(chrbndPath ?? Layers.Source(Path.Combine("chr", "c0000.chrbnd.dcx")));
        var fl = FLVER2.Read(chrbnd.Files.First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var d = fl.Dummies.FirstOrDefault(x => x.ReferenceID == id) ?? throw new InvalidDataException("body has no dummy " + id);
        var z = Vector3.Normalize(d.Forward);
        var y = Vector3.Normalize(d.Upward - z * Vector3.Dot(d.Upward, z));
        var x = Vector3.Cross(y, z);
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return (d.Position, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)));
    }

    static Vector3 Centroid(LopMesh m, Func<LopMesh.Vert, bool> pick)
    {
        var sum = Vector3.Zero; int n = 0;
        foreach (var v in m.Verts) if (pick(v)) { sum += v.P; n++; }
        if (n == 0) throw new InvalidDataException("centroid: no points");
        return sum / n;
    }

    static readonly Piece Empty = new()
    {
        P = { Vector3.Zero, Vector3.Zero, Vector3.Zero },
        N = { Vector3.UnitY, Vector3.UnitY, Vector3.UnitY },
        T = { Vector4.UnitX + Vector4.UnitW, Vector4.UnitX + Vector4.UnitW, Vector4.UnitX + Vector4.UnitW },
        Uv = { Vector2.Zero, Vector2.Zero, Vector2.Zero },
        Uv2 = { Vector2.Zero, Vector2.Zero, Vector2.Zero },
        Tris = { 0, 1, 2 },
    };

    static void Fill(FLVER2 fl, Piece main, bool secondaryFile, int axis, string oldTag, string newTag)
    {
        float tmplLen = 0;
        foreach (var me in fl.Meshes) foreach (var v in me.Vertices) tmplLen = MathF.Max(tmplLen, -Get(v.Position, axis));
        float newLen = 0;
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int mi = 0; mi < fl.Meshes.Count; mi++)
        {
            var me = fl.Meshes[mi];
            var piece = secondaryFile || mi > 0 ? Empty : main;
            var proto = me.Vertices[0];
            var verts = new List<FLVER.Vertex>(piece.P.Count);
            var mmin = new Vector3(float.MaxValue); var mmax = new Vector3(float.MinValue);
            for (int i = 0; i < piece.P.Count; i++)
            {
                var v = new FLVER.Vertex(proto);
                v.Position = piece.P[i];
                v.Normal = piece.N[i];
                for (int k = 0; k < v.Tangents.Count; k++) v.Tangents[k] = piece.T[i];
                v.UVs[0] = new Vector3(piece.Uv[i], 0);
                // Second UV set (blood and wear): a gap-free unwrap of the weapon, across x and along its length, like the game's own.
                for (int k = 1; k < v.UVs.Count; k++) v.UVs[k] = new Vector3(piece.Uv2[i], 0);
                for (int k = 0; k < v.Colors.Count; k++) v.Colors[k] = proto.Colors[k];
                verts.Add(v);
                mmin = Vector3.Min(mmin, v.Position); mmax = Vector3.Max(mmax, v.Position);
            }
            me.Vertices = verts;
            foreach (var fs in me.FaceSets) { fs.TriangleStrip = false; fs.Indices = new List<int>(piece.Tris); }
            if (me.BoundingBox != null) { me.BoundingBox.Min = mmin; me.BoundingBox.Max = mmax; }
            if (me.NodeIndex >= 0 && me.NodeIndex < fl.Nodes.Count) { fl.Nodes[me.NodeIndex].BoundingBoxMin = mmin; fl.Nodes[me.NodeIndex].BoundingBoxMax = mmax; }
            // Length along the blade/barrel direction.
            if (piece == main) foreach (var p in piece.P) newLen = MathF.Max(newLen, Vector3.Dot(p - main.Origin, main.Along));
            min = Vector3.Min(min, mmin); max = Vector3.Max(max, mmax);
        }
        fl.Header.BoundingBoxMin = min; fl.Header.BoundingBoxMax = max;

        // Hit, trail and effect points sit along the blade/barrel: stretch them to the new length.
        if (main.LengthOverride > 0) newLen = main.LengthOverride;
        if (tmplLen > 0.01f && newLen > 0.01f)
        {
            float k = newLen / tmplLen;
            foreach (var d in fl.Dummies)
            {
                if (Get(d.Position, axis) < 0) d.Position = Set(d.Position, axis, Get(d.Position, axis) * k);
                // ...and turn with the blade/barrel, so glows, hit points and the muzzle stay on it.
                d.Position = main.Origin + Vector3.Transform(d.Position, main.Align);
                d.Forward = Vector3.Transform(d.Forward, main.Align);
                d.Upward = Vector3.Transform(d.Upward, main.Align);
            }
        }
        foreach (var n in fl.Nodes) n.Name = n.Name.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
        foreach (var mat in fl.Materials)
        {
            mat.Name = mat.Name.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
            foreach (var t in mat.Textures) t.Path = t.Path.Replace(oldTag, newTag, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Moves a Lies of P bone to another local position (items sheet meshBoneOffsets "bone:x,y,z;..."), carrying the
    // vertices it and its child bones drive: e.g. the Cataclysm's barrels from the charged (rest) pose to the normal one.
    static void PoseBones(LopMesh m, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // Component-space rotation of every bone (rest pose).
        var rot = new Quaternion[m.Bones.Count];
        for (int i = 0; i < m.Bones.Count; i++) { int p = m.Bones[i].Parent; rot[i] = p < 0 ? m.Bones[i].R : Quaternion.Normalize(rot[p] * m.Bones[i].R); }
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':'); var xyz = kv[1].Split(',').Select(s => float.Parse(s, inv)).ToArray();
            int b = m.Bones.FindIndex(x => x.Name == kv[0]);
            if (b < 0) throw new InvalidDataException("mesh has no bone " + kv[0]);
            var local = new Vector3(xyz[0], xyz[1], xyz[2]) - m.Bones[b].T;
            int pb = m.Bones[b].Parent;
            var delta = pb < 0 ? local : Vector3.Transform(local, rot[pb]);
            bool Under(int i) { for (; i >= 0; i = m.Bones[i].Parent) if (i == b) return true; return false; }
            var under = Enumerable.Range(0, m.Bones.Count).Select(Under).ToArray();
            foreach (var v in m.Verts)
            {
                float w = 0; for (int k = 0; k < 4; k++) if (v.Bw[k] > 0 && under[v.Bi[k]]) w += v.Bw[k];
                if (w > 0) v.P += delta * w;
            }
        }
    }

    // Extra points past the blade tip (items sheet reachDummies "id:metres,..."), on every part that has the tip (100),
    // along the blade: the untransformed blast hits from the tip out through them (shots sheet blast).
    static void AddReach(FLVER2 fl, Piece main, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var tip = fl.Dummies.FirstOrDefault(d => d.ReferenceID == 100);
        if (tip == null) return;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            short id = short.Parse(kv[0]); float m = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            fl.Dummies.RemoveAll(d => d.ReferenceID == id);
            // A copy of the tip point (same bones and flags), moved along the blade.
            var nd = (FLVER.Dummy)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tip, null)!;
            nd.Position = tip.Position + main.Along * m;
            nd.ReferenceID = id;
            fl.Dummies.Add(nd);
        }
    }

    // Points from the hand (120) away from the blade (items sheet backDummies "id:metres,..."), each a copy of the tip
    // point turned around, so a shot launched from one flies opposite the blade. The Pale Knight's held R2 (Lies of P's
    // charge dash) fires with the blade pointing behind the Hunter; from these points its pellets go forward
    // (creator 2026-11-18).
    static void AddBack(FLVER2 fl, Piece main, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var tip = fl.Dummies.FirstOrDefault(d => d.ReferenceID == 100);
        var hand = fl.Dummies.FirstOrDefault(d => d.ReferenceID == 120);
        if (tip == null || hand == null) return;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            short id = short.Parse(kv[0]); float m = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            fl.Dummies.RemoveAll(d => d.ReferenceID == id);
            var nd = (FLVER.Dummy)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tip, null)!;
            nd.Position = hand.Position - main.Along * m;
            nd.Forward = Vector3.Normalize(-main.Along) * Math.Max(0.001f, tip.Forward.Length());
            nd.ReferenceID = id;
            fl.Dummies.Add(nd);
        }
    }

    static float Get(this Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
    static Vector3 Set(Vector3 v, int a, float x) => a == 0 ? new(x, v.Y, v.Z) : a == 1 ? new(v.X, x, v.Z) : new(v.X, v.Y, x);
}


