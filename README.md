# Pale Knight Cataclysm

A Bloodborne mod (for the bbport-windows PC port) that adds Lies of P's **Pale Knight** and **Cataclysm** to the
Messengers' bath shop, with Lies of P's own models, textures, icons, texts and animations.

**No files from either game are in this repository or in the mod download.** The mod ships a builder,
`LopArsenal.exe`, which reads the player's own installed Lies of P and Bloodborne and writes the mod into the
bbport launcher's mods folder. Nothing runs inside the game: the game only loads the data files the builder writes
(params, animations, models, textures, menu icons, item texts and a text version of the player's own control script).

- One-click version: https://melty.gg/m/pale-knight-cataclysm
- Nexus download: built from this repository with `build.ps1`.

## What's in here

| Folder | What it is |
| --- | --- |
| `src/LopForge` | The builder (C#, .NET 10). `Program.cs` `forge` / `install` / `find` are the commands players' installs use. |
| `src/NexusInstaller` | The installer window of the Nexus download (C# 5, .NET Framework 4); it only finds the two game folders and runs `LopArsenal.exe install`. |
| `src/data/LiesofP.usmap` | Lies of P's Unreal type mappings (class layouts, no game content), needed to read its packages. |
| `sheets` | The design sheets the builder works from (items, moves, shots, systems). |
| `third_party/DSLuaDecompiler` | `LuaDecompilerCore` from Katalash's DSLuaDecompiler (MIT), unchanged except its target framework (net9.0 -> net10.0). |
| `licenses` | Licenses of the libraries the release ships. |

## Build

Needs Windows, [git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone <this repository>
cd pale-knight-cataclysm
.\build.ps1
```

`build.ps1`:
1. clones CUE4Parse (FabianFG, commit `c780a457`) and SoulsFormatsNEXT (soulsmods, commit `e25d1a5c`) into `deps\`;
2. `dotnet publish src\LopForge\LopForge.csproj -c Release -r win-x64 --self-contained true` and renames
   `LopForge.exe` to `LopArsenal.exe`;
3. compiles `src\NexusInstaller\InstallerWindow.cs` with Windows' own `csc.exe` into
   `Install Pale Knight Cataclysm.exe`.

The result in `out\Pale Knight Cataclysm\` is the Nexus download's layout (add the readme from the release).

## Credits

CUE4Parse (FabianFG and contributors, Apache-2.0), SoulsFormats / SoulsFormatsNEXT (TKGP and contributors,
GPL-3.0), HKX2 and SoulsAssetPipeline's Havok spline decoder (Meowmaritus, Katalash and contributors; decoder
adapted from HavokLib by Lukas Cone; GPL-3.0; in `src/LopForge/HKX2` and `src/LopForge/Sap`), DSLuaDecompiler
(Katalash and contributors, MIT), MoonSharp (Marco Mastropaolo, MIT), BCnEncoder.NET (Nominom, MIT), DrSwizzler
(Shadowth117, MIT), OutTheShade's UE4SS mappings generator.

## License

GPL-3.0 (see `LICENSE`). Lies of P and Bloodborne belong to their owners; this mod contains none of their files.
