using System.Globalization;
using System.Text;
using LuaDecompilerCore;
using LuaDecompilerCore.IR;
using LuaDecompilerCore.LanguageDecompilers;

namespace LopForge;

// Bloodborne's control script (action/script/c0000.hks) decides almost everything a weapon can do from lists of
// moveset numbers (R2 charge and follow-ups, L2, transform, left-hand guns, how the left hand holds a gun...), so a
// weapon on a new moveset number gets none of it. The game carries Havok Script's own compiler, so it can read the
// script as text, like the other FromSoftware games of its time. At setup this turns the player's own compiled
// script into text (DSLuaDecompiler, MIT) and routes every moveset-number lookup through one function that reports
// each new moveset as the Bloodborne weapon it is built on (the moves sheet's template). Nothing else changes.
public static class HksText
{
    const string Lookup = "env(\"装備武器カテゴリ番号取得\", ";

    public static string? Build(byte[] compiled, IReadOnlyDictionary<int, int> asCategory, Action<string> log, IReadOnlyCollection<int>? relaxedLeft = null, IReadOnlyCollection<int>? comboTransform = null, IReadOnlyCollection<int>? ownL2 = null, (int orig, int copy)? buffAlias = null)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        using var ms = new MemoryStream(compiled);
        var br = new LuaDecompilerCore.Utilities.BinaryReaderEx(false, ms);
        var lua = new LuaFile(br);
        if (lua.Version != LuaFile.LuaVersion.Lua51Hks) { log("control script: not Havok Script, left as it is"); return null; }
        var dec = new LuaDecompiler(new DecompilationOptions
        {
            IncludedFunctionIds = null, ExcludedFunctionIds = new HashSet<int>(), DumpIrPasses = new HashSet<string>(),
            DumpDotGraphPasses = new HashSet<string>(), OutputDebugComments = false, CatchPassExceptions = false,
        });
        var res = dec.DecompileLuaFunction(new HksDecompiler(), new Function(lua.MainFunction.FunctionId), lua.MainFunction);
        var src = res.DecompiledSource;
        int uses = CountOf(src, Lookup);
        if (uses < 100) { log($"control script: only {uses} moveset lookups found, left as it is"); return null; }
        var body = src.Replace(Lookup, "LopArsenal_Category(");
        // A buff that copies another counts as it wherever the script checks for it (shots sheet buff.countsAs): Strengthen
        // Bullet (9600010) as Bone Marrow Ash (2230), so a left-hand gun fires its buffed (special) shot under it.
        if (buffAlias is { } ba)
        {
            string on = $"env(\"特殊効果IDを取得\", {ba.orig}) == TRUE", off = $"env(\"特殊効果IDを取得\", {ba.orig}) == FALSE";
            int n = CountOf(body, on) + CountOf(body, off);
            body = body.Replace(on, $"({on} or env(\"特殊効果IDを取得\", {ba.copy}) == TRUE)")
                       .Replace(off, $"({off} and env(\"特殊効果IDを取得\", {ba.copy}) == FALSE)");
            log($"control script: buff {ba.copy} counts as {ba.orig} ({n} checks)");
        }
        var tail = new StringBuilder();
        // Arm weapons (worn on the forearm, like Lies of P's Legion arms) switch off the left hand's pistol-holding layer
        // (MasterBlend), which twists the wrist to point a pistol forward, so the weapon stays along the forearm. The
        // pistol's LeftWeaponAdd stays as it is: the firing moves pick their clips by it (systems: cat-relaxed-hand).
        if (relaxedLeft is { Count: > 0 })
        {
            if (!body.Contains("function Act_LeftWeaponAddAnime()", StringComparison.Ordinal)) log("control script: left-hand hold function not found, left as it is");
            else
            {
                var cond = string.Join(" or ", relaxedLeft.Select(c => $"env(\"装備武器カテゴリ番号取得\", Left) == {c}"));
                tail.Append("\n-- Lies of P Arsenal: arm weapons keep the forearm's own pose (no pistol-holding layer).\n")
                    .Append("LopArsenal_LeftWeaponAddAnime = Act_LeftWeaponAddAnime\n")
                    .Append("function Act_LeftWeaponAddAnime()\n    LopArsenal_LeftWeaponAddAnime()\n")
                    .Append($"    if {cond} then\n        hkbSetVariable(\"MasterBlend\", 0)\n    end\nend\n");
                log("control script: arm weapons keep a relaxed left hand (" + string.Join(", ", relaxedLeft) + ")");
            }
        }
        // L1 transform while standing or moving: for these movesets in their normal form, play the combo transform
        // (W_ChangeGimmick_RightSide_ModeA, which transforms reliably) instead of the shared idle transform
        // (systems: pk-idle-transform).
        if (comboTransform is { Count: > 0 })
        {
            int fa = body.IndexOf("function Act_AttackRight()", StringComparison.Ordinal);
            int fe = fa < 0 ? -1 : body.IndexOf("\nfunction ", fa + 10, StringComparison.Ordinal);
            var m = fa < 0 ? null : System.Text.RegularExpressions.Regex.Match(body[fa..(fe < 0 ? body.Length : fe)], @"\n([ \t]*)else\r?\n([ \t]*)Act_ChangeMode\(\)");
            if (m == null || !m.Success) log("control script: plain transform branch not found, standing transform left as it is");
            else
            {
                var ind = m.Groups[2].Value;
                var cond = string.Join(" or ", comboTransform.Select(c => $"env(\"装備武器カテゴリ番号取得\", Right) == {c}"));
                var guard = $"{ind}if ({cond}) and env(\"武器切替状態を取得\") == 1 then\n{ind}    hkbFireEvent(\"W_ChangeGimmick_RightSide_ModeA\")\n{ind}    return TRUE\n{ind}end\n";
                int at = fa + m.Index + m.Length - "Act_ChangeMode()".Length - ind.Length;
                body = body.Insert(at, guard);
                log("control script: L1 transform from standing/moving uses the combo transform for " + string.Join(", ", comboTransform));
            }
        }
        // L2 while transformed: these weapons are transformed but held one-handed (their look), and in that state the
        // script hands L2 to a left-hand gun before the weapon's own L2 (systems: pk-l2). So for them, transformed L2
        // always plays the weapon's own L2 (its out-of-ammo version when the Hunter can't fire).
        if (ownL2 is { Count: > 0 })
        {
            if (!body.Contains("function Act_WeaponAttack()", StringComparison.Ordinal)) log("control script: weapon attack function not found, L2 left as it is");
            else
            {
                var cond = string.Join(" or ", ownL2.Select(c => $"env(\"装備武器カテゴリ番号取得\", Right) == {c}"));
                tail.Append("\n-- Lies of P Arsenal: transformed L2 is the weapon's own, even with a gun in the left hand.\n")
                    .Append("LopArsenal_WeaponAttack = Act_WeaponAttack\n")
                    .Append("function Act_WeaponAttack()\n")
                    .Append($"    if ({cond}) and env(\"武器切替状態を取得\") ~= 1 and env(\"アクションリクエスト\", 3) == TRUE and env(\"スタミナ取得\") > 0 then\n")
                    .Append("        act(\"入力受付リセット\")\n")
                    .Append("        if env(\"銃が撃てるか\", 1) == FALSE then\n            hkbFireEvent(\"W_AtkBothL2_01_mirror\")\n        else\n            hkbFireEvent(\"W_AtkBothL2_01\")\n        end\n")
                    .Append("        return TRUE\n    end\n    return LopArsenal_WeaponAttack()\nend\n");
                log("control script: transformed L2 is the weapon's own for " + string.Join(", ", ownL2));
            }
        }
        var sb = new StringBuilder();
        sb.Append("-- Lies of P Arsenal: the new movesets count as the Bloodborne weapons they are built on.\n");
        sb.Append("function LopArsenal_Category(hand)\n    local c = env(\"装備武器カテゴリ番号取得\", hand)\n");
        bool first = true;
        foreach (var (k, v) in asCategory.OrderBy(kv => kv.Key))
        {
            sb.Append(first ? "    if" : "    elseif").Append($" c == {k} then\n        return {v}\n");
            first = false;
        }
        if (!first) sb.Append("    end\n");
        sb.Append("    return c\nend\n\n");
        var text = sb + body + tail;
        // It must read as Lua (checked with MoonSharp's parser), or the original script stays.
        try { new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.None).LoadString(text); }
        catch (Exception e) { log("control script: text version did not parse (" + e.Message.Split('\n')[0] + "), left as it is"); return null; }
        log($"control script: {uses} moveset lookups now see " + string.Join(", ", asCategory.Select(kv => $"{kv.Key} as {kv.Value}")));
        return text;
    }

    static int CountOf(string s, string what) { int n = 0, i = 0; while ((i = s.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; } return n; }
}
