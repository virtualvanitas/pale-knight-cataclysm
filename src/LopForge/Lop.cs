using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace LopForge;

// Opens the player's own Lies of P archives (read only).
public static class Lop
{
    public static DefaultFileProvider Open(string lopFolder)
    {
        var exe = Path.Combine(lopFolder, "LiesofP", "Binaries", "Win64", "LOP-Win64-Shipping.exe");
        var paks = Path.Combine(lopFolder, "LiesofP", "Content", "Paks");
        var key = KeyFinder.Find(exe, Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"))
                  ?? throw new InvalidDataException("Could not read the archive key from Lies of P.");
        var p = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_LiesofP), StringComparer.OrdinalIgnoreCase);
        var usmap = Path.Combine(AppContext.BaseDirectory, "data", "LiesofP.usmap");
        if (File.Exists(usmap))
            p.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(usmap);
        p.Initialize();
        p.SubmitKey(new FGuid(), new FAesKey(key));
        p.PostMount();
        return p;
    }
}
