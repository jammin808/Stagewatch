<#
.SYNOPSIS
  Build a Stagewatch release: bump the version numbers, test, publish, package the Companion module
  and compile the installer - in one go, so the installer and the module always carry new versions.

.EXAMPLE
  pwsh tools\release.ps1                 # patch bump: 2.0.0 -> 2.0.1 (app and module)
  pwsh tools\release.ps1 -Bump minor     # 2.0.1 -> 2.1.0
  pwsh tools\release.ps1 -Version 3.0.0 -ModuleVersion 3.0.0
  pwsh tools\release.ps1 -NoBump         # rebuild the current version (e.g. after a failed run)

.NOTES
  Versions live in:  src\Timer.App\Timer.App.csproj (Version, FileVersion)
                     installer\Stagewatch.iss (AppVersion)
                     companion-module\package.json (+ package-lock.json); the Companion packager
                     stamps it into the module manifest.
                     src\Timer.Core\AppInfo.cs (ReleaseDate, set to today on a version bump).
  Output:            installer\Output\Stagewatch-Setup-<version>.exe
                     companion-module\stagewatch-<moduleVersion>.tgz (also bundled in the installer)
  Code signing is not done here (see docs\BUILD.md).
#>
[CmdletBinding()]
param(
    [ValidateSet('major', 'minor', 'patch')] [string] $Bump = 'patch',
    [string] $Version,
    [string] $ModuleVersion,
    [switch] $NoBump,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Next-Version([string] $current, [string] $kind) {
    $p = $current.Split('.') | ForEach-Object { [int]$_ }
    switch ($kind) {
        'major' { return "$($p[0] + 1).0.0" }
        'minor' { return "$($p[0]).$($p[1] + 1).0" }
        default { return "$($p[0]).$($p[1]).$($p[2] + 1)" }
    }
}

function Assert-Version([string] $v) {
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$v' must look like 1.2.3" }
}

# ------------------------------------------------------------------ versions
$csproj = Join-Path $root 'src\Timer.App\Timer.App.csproj'
$iss = Join-Path $root 'installer\Stagewatch.iss'
$pkg = Join-Path $root 'companion-module\package.json'
$lock = Join-Path $root 'companion-module\package-lock.json'

$appCurrent = ([regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value
$modCurrent = ([regex]::Match((Get-Content $pkg -Raw), '"version":\s*"([^"]+)"')).Groups[1].Value

$appNew = if ($NoBump) { $appCurrent } elseif ($Version) { $Version } else { Next-Version $appCurrent $Bump }
$modNew = if ($NoBump) { $modCurrent } elseif ($ModuleVersion) { $ModuleVersion } else { Next-Version $modCurrent $Bump }
Assert-Version $appNew
Assert-Version $modNew

Step "Versions: app $appCurrent -> $appNew, Companion module $modCurrent -> $modNew"

function Replace-InFile([string] $path, [string] $pattern, [string] $replacement, [int] $count = 1) {
    $text = [IO.File]::ReadAllText($path)
    $rx = [regex]::new($pattern)
    if (-not $rx.IsMatch($text)) { throw "Pattern not found in $path : $pattern" }
    $text = $rx.Replace($text, $replacement, $count)
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

Replace-InFile $csproj '<Version>[^<]+</Version>' "<Version>$appNew</Version>"
Replace-InFile $csproj '<FileVersion>[^<]+</FileVersion>' "<FileVersion>$appNew.0</FileVersion>"
Replace-InFile $iss '#define AppVersion\s+"[^"]+"' "#define AppVersion    `"$appNew`""
Replace-InFile $pkg '"version":\s*"[^"]+"' "`"version`": `"$modNew`""
if (Test-Path $lock) {
    # The first two "version" entries in package-lock are the project itself.
    Replace-InFile $lock '"version":\s*"[^"]+"' "`"version`": `"$modNew`"" 2
}
# Release date: Production licences whose update window ended before this date run this build as Pro.
# A -NoBump rebuild keeps the original date so a rebuild never moves a customer's entitlement.
if (-not $NoBump) {
    $today = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    Replace-InFile (Join-Path $root 'src\Timer.Core\AppInfo.cs') 'ReleaseDate = "[^"]+"' "ReleaseDate = `"$today`""
    Step "Release date: $today"
}

# ------------------------------------------------------------------ tests
if (-not $SkipTests) {
    Step 'Building and testing (C#)'
    dotnet build Timer.slnf -c Debug -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests\Stagewatch.Tests -c Debug --no-build
    if ($LASTEXITCODE -ne 0) { throw 'C# tests failed' }

    Step 'Testing the Companion module'
    Push-Location companion-module
    npm test
    $t = $LASTEXITCODE
    Pop-Location
    if ($t -ne 0) { throw 'Companion module tests failed' }
}

# ------------------------------------------------------------------ holographic renderer (three.js bundle)
Step 'Bundling the holographic display (three.js)'
Push-Location src\Timer.Remote\holo
if (-not (Test-Path node_modules)) { npm ci --no-audit --no-fund }
npm test
if ($LASTEXITCODE -ne 0) { Pop-Location; throw '3D display tests failed' }
npm run build
$h = $LASTEXITCODE
Pop-Location
if ($h -ne 0) { throw 'Holographic bundle failed' }

# ------------------------------------------------------------------ app
Step "Publishing Stagewatch $appNew (self-contained, single file)"
if (Test-Path publish) { Get-ChildItem publish -File | Remove-Item -Force }
dotnet publish src\Timer.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

# ------------------------------------------------------------------ code signing (optional)
# Set STAGEWATCH_SIGN_THUMBPRINT to the SHA-1 thumbprint of a code-signing certificate in the current user's (or
# machine's) certificate store, and the exe and the installer are signed with a timestamp. Unset = unsigned build.
$signThumb = $env:STAGEWATCH_SIGN_THUMBPRINT
$signtool = $null
if ($signThumb) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { throw 'STAGEWATCH_SIGN_THUMBPRINT is set but signtool.exe (Windows SDK) was not found' }
}
function Sign-File([string] $path) {
    if (-not $signtool) { return }
    & $signtool sign /sha1 $signThumb /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d 'Stagewatch' $path
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $path" }
    & $signtool verify /pa /q $path
    if ($LASTEXITCODE -ne 0) { throw "Signature check failed: $path" }
}
if ($signtool) { Step 'Signing Stagewatch.exe'; Sign-File (Join-Path $root 'publish\Stagewatch.exe') }

# ------------------------------------------------------------------ Companion module
Step "Packaging the Companion module $modNew"
Push-Location companion-module
Get-ChildItem -Filter 'stagewatch-*.tgz' | Remove-Item -Force   # the installer bundles stagewatch-*.tgz: keep only this one
npm run package
$p = $LASTEXITCODE
Pop-Location
if ($p -ne 0) { throw 'Companion packaging failed' }

# ------------------------------------------------------------------ installer
Step "Compiling the installer"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found' }
$ok = $false
for ($i = 1; $i -le 3 -and -not $ok; $i++) {
    & $iscc /Q $iss
    # Antivirus sometimes holds the new exe for a moment ("EndUpdateResource failed"): wait and retry.
    if ($LASTEXITCODE -eq 0) { $ok = $true } else { Write-Host "ISCC failed (attempt $i) - retrying" -ForegroundColor Yellow; Start-Sleep -Seconds 3 }
}
if (-not $ok) { throw 'Installer compile failed' }

if ($signtool) { Step 'Signing the installer'; Sign-File (Join-Path $root "installer\Output\Stagewatch-Setup-$appNew.exe") }

$setup = Get-Item (Join-Path $root "installer\Output\Stagewatch-Setup-$appNew.exe")
$tgz = Get-Item (Join-Path $root "companion-module\stagewatch-$modNew.tgz")
Step 'Done'
Write-Host ("  Installer        {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
Write-Host ("  Companion module {0}" -f $tgz.FullName)
if ($signtool) { Write-Host '  Code-signed and timestamped.' }
else { Write-Host '  Not code-signed: set STAGEWATCH_SIGN_THUMBPRINT to sign (see docs\BUILD.md).' }
