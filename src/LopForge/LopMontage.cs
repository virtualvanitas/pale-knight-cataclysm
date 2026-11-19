using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.UObject;

namespace LopForge;

// A Lies of P attack montage: which animation it plays and the timing marks we re-time Bloodborne's events to.
public sealed class LopMontage
{
    public string Path = "";
    public float Length;
    public string Sequence = "";
    public float SeqStart, SeqEnd, Rate = 1;
    public List<(float t, float dur, string name, string cls)> Marks = new();

    public float First(string name, float dflt = -1) => Marks.Where(m => m.name == name).Select(m => m.t).DefaultIfEmpty(dflt).Min();
    public float End(string name, float dflt = -1) => Marks.Where(m => m.name == name).Select(m => m.t + m.dur).DefaultIfEmpty(dflt).Max();

    public static LopMontage Load(IFileProvider p, string path)
    {
        var mt = p.LoadPackageObject<UAnimMontage>(path + "." + System.IO.Path.GetFileName(path));
        var r = new LopMontage { Path = path, Length = mt.SequenceLength };
        var seg = mt.SlotAnimTracks?.FirstOrDefault()?.AnimTrack.AnimSegments?.FirstOrDefault();
        if (seg != null)
        {
            r.Sequence = seg.AnimReference?.ResolvedObject?.GetPathName() ?? "";
            r.SeqStart = seg.AnimStartTime; r.SeqEnd = seg.AnimEndTime; r.Rate = seg.AnimPlayRate;
        }
        if (mt.Notifies != null)
            foreach (var n in mt.Notifies)
                r.Marks.Add((n.LinkValue, n.Duration, n.NotifyName.ToString(), n.NotifyStateClass?.Name ?? n.Notify?.Name ?? ""));
        r.Marks.Sort((a, b) => a.t.CompareTo(b.t));
        return r;
    }
}
