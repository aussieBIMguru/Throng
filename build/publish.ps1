<#
.SYNOPSIS
    Builds a Throng release: tests, the self-contained app and the installer artifacts\Throng-Setup-<version>.exe.

.DESCRIPTION
    Run from anywhere (paths are relative to the repo root), or double-click publish.bat at the root:
        powershell -ExecutionPolicy Bypass -File build\publish.ps1
    Steps (stops at the first failure):
      1. Reads the version from Directory.Build.props (<Version>, D-414).
      2. Runs the quick tests in Throng.sln: not the stadium bench (bench.bat) and not the long runs tagged
         TestCategory "Slow" (D-430; -AllTests includes them, Visual Studio's Test Explorer always does).
      3. Publishes Throng.App self-contained for win-x64 (a folder, not single-file, no trimming) to artifacts\app\
         and checks the licence files came with it (BRIEF section 20.3).
      4. Optionally signs Throng.exe (-Sign; off for 1.0, D-413).
      5. Compiles installer\Throng.iss with Inno Setup 6 into artifacts\, then signs the setup (if -Sign) and writes
         its SHA-256 next to it.
    Needs: the .NET 10 SDK and Inno Setup 6 (https://jrsoftware.org/isinfo.php; a build tool, not shipped).

.PARAMETER SkipTests
    Skip the tests (not for real releases).

.PARAMETER AllTests
    Also run the long tests (TestCategory "Slow"). The bench still needs bench.bat.

.PARAMETER Sign
    Sign Throng.exe and the setup with signtool (needs -CertThumbprint and signtool on PATH). Off for 1.0.

.PARAMETER InnoCompiler
    Full path to ISCC.exe if Inno Setup 6 isn't in one of its usual folders.
#>
param(
    [switch] $SkipTests,
    [switch] $AllTests,
    [switch] $Sign,
    [string] $CertThumbprint = '',
    [string] $InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

function Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check-Exit([string] $what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

# ---- 1. Version
[xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "The version '$version' isn't major.minor.patch." }
Step "Throng $version"

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

# ---- 2. Tests
if (-not $SkipTests) {
    $filter = if ($AllTests) { 'TestCategory!=Bench' } else { 'TestCategory!=Bench&TestCategory!=Slow' }
    Step "Tests ($filter)"
    dotnet test (Join-Path $root 'Throng.sln') -c Release --nologo --filter $filter
    Check-Exit 'The tests'
}

# ---- 3. The app (self-contained, so users need no .NET runtime: D-407)
Step 'Throng.App (self-contained win-x64)'
$appOut = Join-Path $artifacts 'app'
dotnet publish (Join-Path $root 'src\Throng.App\Throng.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishTrimmed=false -p:PublishSingleFile=false -o $appOut --nologo
Check-Exit 'Publishing Throng.App'
Get-ChildItem $appOut -Filter *.pdb -Recurse | Remove-Item -Force
foreach ($needed in 'Throng.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Assets\Fonts\OFL.txt') {
    if (-not (Test-Path (Join-Path $appOut $needed))) { throw "The app publish lacks $needed." }
}

# ---- 4. Signing (off for 1.0)
function Sign-Files([string[]] $files) {
    if (-not $Sign) { return }
    if (-not $CertThumbprint) { throw '-Sign needs -CertThumbprint.' }
    & signtool sign /sha1 $CertThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $files
    Check-Exit 'signtool'
}
Sign-Files @(Join-Path $appOut 'Throng.exe')

# ---- 5. The installer
Step 'Installer'
if (-not $InnoCompiler) {
    $InnoCompiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                      "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $InnoCompiler) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $InnoCompiler = $onPath.Source }
}
if (-not $InnoCompiler) { throw 'Inno Setup 6 (ISCC.exe) was not found: install it or pass -InnoCompiler.' }
& $InnoCompiler "/DAppVersion=$version" "/O$artifacts" (Join-Path $root 'installer\Throng.iss')
Check-Exit 'Inno Setup'

$setup = Join-Path $artifacts "Throng-Setup-$version.exe"
Sign-Files @($setup)
Step "Done: $setup"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "SHA-256 $hash"
"$hash  Throng-Setup-$version.exe" | Set-Content (Join-Path $artifacts "Throng-Setup-$version.sha256.txt")
