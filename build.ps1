# Builds Pale Knight Cataclysm from source: the builder (LopArsenal.exe) and the Nexus installer window,
# laid out exactly as in the released download (out\Pale Knight Cataclysm\).
# Needs: Windows, git, and the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
# Usage (PowerShell, in this folder):  .\build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$deps = Join-Path $root 'deps'

# 1. Libraries from their own repositories, at the exact versions the release was built with.
$repos = @(
    @{ name = 'CUE4Parse';        url = 'https://github.com/FabianFG/CUE4Parse.git';        commit = 'c780a4574079d1bc34cde2a3a9eee225fbfae4ce' },
    @{ name = 'SoulsFormatsNEXT'; url = 'https://github.com/soulsmods/SoulsFormatsNEXT.git'; commit = 'e25d1a5c3cda83b801fdefe00f4d2903892cc526' }
)
New-Item -ItemType Directory -Force $deps | Out-Null
foreach ($r in $repos) {
    $dir = Join-Path $deps $r.name
    if (-not (Test-Path (Join-Path $dir '.git'))) { git clone --quiet $r.url $dir; if ($LASTEXITCODE) { throw "clone $($r.name) failed" } }
    git -C $dir fetch --quiet origin $r.commit
    git -C $dir checkout --quiet --force $r.commit; if ($LASTEXITCODE) { throw "checkout $($r.name) failed" }
}

# 2. The builder: a self-contained .NET 10 program (players need no runtime), renamed LopArsenal.exe.
$out = Join-Path $root 'out\Pale Knight Cataclysm'
$pub = Join-Path $out 'LopArsenal'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src\LopForge\LopForge.csproj') -c Release -r win-x64 --self-contained true -o $pub
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Move-Item (Join-Path $pub 'LopForge.exe') (Join-Path $pub 'LopArsenal.exe') -Force
Copy-Item -Recurse (Join-Path $root 'licenses') (Join-Path $pub 'licenses')

# 3. The installer window, with the C# compiler that comes with Windows (.NET Framework 4).
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$out\Install Pale Knight Cataclysm.exe" (Join-Path $root 'src\NexusInstaller\InstallerWindow.cs')
if ($LASTEXITCODE) { throw 'installer compile failed' }

Write-Host "Built: $out"
