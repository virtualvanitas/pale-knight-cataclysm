using System.Text.Json;

namespace LopForge;

// The design sheets (sheets/*.json, shipped beside the tool) are the source of truth: one row per thing.
public sealed class ItemRow
{
    public string key { get; set; } = "";
    public int bbWeaponId { get; set; }
    public int templateWeaponId { get; set; }
    public int equipModelId { get; set; }
    public int templateModelId { get; set; }
    public string slot { get; set; } = "";
    public string nameFrom { get; set; } = "";
    public string infoFrom { get; set; } = "";
    public string captionFrom { get; set; } = "";
    public int iconId { get; set; }
    public int wepmotionBothHandId { get; set; }
    public int attackBasePhysics { get; set; }
    public int attackBaseFire { get; set; }
    public int correctStrength { get; set; }
    public int correctAgility { get; set; }
    public int correctMagic { get; set; }
    public int properStrength { get; set; }
    public int properAgility { get; set; }
    public float weight { get; set; }
    public int spAttribute { get; set; }
    public int shopPrice { get; set; }
    public int bulletConsumeNum { get; set; }
    public string lopMesh { get; set; } = "";
    public string lopMaterial { get; set; } = "";
    public string fit { get; set; } = "";
    public List<string> keepBones { get; set; } = new();
    public string texBC { get; set; } = "";
    public string texARM { get; set; } = "";
    public string texN { get; set; } = "";
    public int texSize { get; set; }
    public List<int> shopQwc { get; set; } = new() { -1 };
    public int wepmotionCategory { get; set; } = -1;
    public int residentSpEffectId { get; set; } = -1;
    public float gripShift { get; set; }
    public float gripTilt { get; set; }
    public float sleeveRoll { get; set; }
    public int attachDummy { get; set; }
    public int formBDummy { get; set; } = -1;
    public string iconFrom { get; set; } = "";
    public int iconTemplate { get; set; } = 1010;
    public string reachDummies { get; set; } = "";
    public bool idleComboTransform { get; set; }
    public bool ownL2 { get; set; }
    public int sortIdOffset { get; set; }   // menu sort order = the template's sortId + this (0 = share the template's)
    public string partAnimTimes { get; set; } = "";   // "slot:fire:free,..." per buffed-shot move: shot time and when the player can act again (s)
    public int partAnimFrom { get; set; }   // weapon model whose own animation package (moving barrel) is the template, e.g. 2500 Hunter Pistol; 0 = none
    public int gemSlotsFrom { get; set; }   // Bloodborne weapon whose blood gem slots (WeaponGenParam, +0..+10) this weapon copies; 0 = none
    public string backDummies { get; set; } = "";   // "id:metres,..." extra model points from the hand away from the blade, facing that way (a shot from them flies opposite the blade)
    public string meshBoneOffsets { get; set; } = "";   // "bone:x,y,z;..." the Lies of P bone's local position the model is built in   // transformed L2 is the weapon's own even with a gun in the left hand   // standing/moving L1 plays the combo-transform move (true) or Bloodborne's own transform (false)   // "id:metres past the blade tip,..." extra model points (the untransformed blast)
}

public sealed class MoveRow
{
    public string weapon { get; set; } = "";
    public int bbCategory { get; set; }
    public int bbAnimId { get; set; }
    public string role { get; set; } = "";
    public int templateCategory { get; set; }
    public int templateAnimId { get; set; }
    public string lopMontage { get; set; } = "";
    public string hitMark { get; set; } = "";
    public string effect { get; set; } = "";
    public bool verified { get; set; }
    public bool stripHits { get; set; }
    public int spEffect { get; set; } = -1;
    public int hitsFrom { get; set; }
    public int shotFrom { get; set; }
    public int shotDummy { get; set; }
    public float rootScale { get; set; } = 1;   // multiplies the Lies of P move's forward travel
    public bool stripShots { get; set; }        // drop the template slot's own projectile events before shotFrom adds its shot
    public bool shotOnly { get; set; }    // shotFrom adds only the projectile (318), not the source slot's blade hit
    public bool bulletCost { get; set; }  // every shot of the move also fires the bullet-use event (costs 1 Quicksilver Bullet)
    public int eventsFrom { get; set; }   // >0: the slot's events are that template slot's (e.g. a working side dodge attack's)
    public int shotJudge { get; set; }    // behaviour judge written into the added shot (0 = the shot slot's own)
    public int blastJudge { get; set; }   // >0: a hit with this behaviour judge (shots sheet blast) at every Lies of P fire mark
    public string fxFrom { get; set; } = "";
    public int fxDummy { get; set; }      // our model point the borrowed flash sits on (0 = keep the other weapon's; 1xxxx = a full point id, e.g. 12100 on the transformed part)
    public bool fxOnly { get; set; }      // fxFrom brings only the flash and sounds, never its projectile (e.g. the held R2: gunshot look and sound, no pellets)
    public float clipStart { get; set; }
    public float clipEnd { get; set; }
    public float duration { get; set; }   // play the Lies of P move over exactly this many seconds (0 = its own speed)
    public float hitTime { get; set; } = -1;   // seconds into the (clipped) Lies of P move where the template's first hit/shot lands, when the move has no hit mark (-1 = none)
    public string trailFxFrom { get; set; } = "";   // "aNNN:slot" another weapon's effects (type 96) and swing sounds (type 128 kind 1) around its first hit, laid on this move's first blade hit (e.g. a145:40000 Boom Hammer fire)
    public string trailFxPoints { get; set; } = "";   // our weapon model points (full ids, e.g. "10130,10100") for the borrowed effects, in the order the source's distinct weapon points appear
    public float trailFxTail { get; set; } = 0.3f;   // the borrowed effects end at most this long after the move's hit ends
    public bool newSlot { get; set; }     // the slot doesn't exist in the template: it is made from templateAnimId's family (+2/+3/+5 variants, +10 buffed)
    public string selectorFrom { get; set; } = "";   // regex: behaviour manual selectors (by name) that move from templateAnimId's family to this row's, for every gun they hold
}

public sealed class Sheet<T> { public List<string> columns { get; set; } = new(); public List<T> rows { get; set; } = new(); }

public static class Sheets
{
    public static string Dir = Path.Combine(AppContext.BaseDirectory, "sheets");
    public static List<T> Load<T>(string name) =>
        JsonSerializer.Deserialize<Sheet<T>>(File.ReadAllText(Path.Combine(Dir, name + ".json")))!.rows;
}

