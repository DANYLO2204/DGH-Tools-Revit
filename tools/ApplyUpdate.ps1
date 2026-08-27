param(
    [Parameter(Mandatory=$true)][int]$RevitPid,
    [Parameter(Mandatory=$true)][string]$StateDir,
    [Parameter(Mandatory=$true)][string]$PluginDir,
    [Parameter(Mandatory=$true)][string]$Version
)

$ErrorActionPreference = "Stop"
$LogPath = Join-Path $StateDir "update.log"

function Log([string]$Text) {
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Add-Content -LiteralPath $LogPath -Value "[$stamp] $Text" -Encoding UTF8
}
function Fail([string]$Text) { Log "ERROR: $Text"; exit 1 }

Log "Updater started for $Version. Waiting for Revit PID $RevitPid..."
for ($i = 0; $i -lt 600; $i++) {
    if (-not (Get-Process -Id $RevitPid -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}
if (Get-Process -Id $RevitPid -ErrorAction SilentlyContinue) { Fail "Timed out waiting for Revit to close." }
Start-Sleep -Milliseconds 800

$sourceFiles = @(
    (Join-Path $StateDir "pending-App.cs"),
    (Join-Path $StateDir "pending-AlignGridEndsCommand.cs"),
    (Join-Path $StateDir "pending-UpdateManager.cs")
)
foreach ($f in $sourceFiles) { if (-not (Test-Path $f)) { Fail "Missing pending source: $f" } }

$revitDir = Join-Path $env:ProgramFiles "Autodesk\Revit 2023"
$revitApi = Join-Path $revitDir "RevitAPI.dll"
$revitApiUI = Join-Path $revitDir "RevitAPIUI.dll"
if (-not (Test-Path $revitApi)) { Fail "Revit 2023 API was not found." }
if (-not (Test-Path $revitApiUI)) { Fail "RevitAPIUI.dll was not found." }

$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { Fail ".NET Framework C# compiler was not found." }

$frameworkRoots = @(
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
    "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2",
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319"
)
function Find-Dll([string]$Name) {
    foreach ($r in $frameworkRoots) {
        if (-not $r) { continue }
        $p = Join-Path $r $Name; if (Test-Path $p) { return $p }
        $p = Join-Path (Join-Path $r "WPF") $Name; if (Test-Path $p) { return $p }
    }
    return $null
}
$presentationCore = Find-Dll "PresentationCore.dll"
$windowsBase = Find-Dll "WindowsBase.dll"
$webExtensions = Find-Dll "System.Web.Extensions.dll"
if (-not $presentationCore) { Fail "PresentationCore.dll not found." }
if (-not $windowsBase) { Fail "WindowsBase.dll not found." }
if (-not $webExtensions) { Fail "System.Web.Extensions.dll not found." }

$temp = Join-Path $env:TEMP ("DGH_Update_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$newDll = Join-Path $temp "GridEndAligner.dll"

$args = @(
    "/nologo", "/target:library", "/platform:x64", "/optimize+", "/out:$newDll",
    "/reference:$revitApi", "/reference:$revitApiUI",
    "/reference:$presentationCore", "/reference:$windowsBase", "/reference:$webExtensions"
) + $sourceFiles

Log "Compiling downloaded source..."
$output = & $csc @args 2>&1
$exit = $LASTEXITCODE
if ($output) { Add-Content -LiteralPath $LogPath -Value ($output | Out-String) -Encoding UTF8 }
if ($exit -ne 0 -or -not (Test-Path $newDll)) { Fail "Compilation failed with exit code $exit." }
try { [Reflection.AssemblyName]::GetAssemblyName($newDll) | Out-Null } catch { Fail "Compiled DLL validation failed." }

$installedDll = Join-Path $PluginDir "GridEndAligner.dll"
$backupDll = Join-Path $PluginDir "GridEndAligner.dll.bak"
$installedUpdater = Join-Path $PluginDir "ApplyUpdate.ps1"
$pendingUpdater = Join-Path $StateDir "pending-ApplyUpdate.ps1"

try {
    if (Test-Path $backupDll) { Remove-Item $backupDll -Force -ErrorAction SilentlyContinue }
    if (Test-Path $installedDll) { Copy-Item $installedDll $backupDll -Force }
    Copy-Item $newDll $installedDll -Force
    Set-Content -LiteralPath (Join-Path $PluginDir "version.txt") -Value $Version -Encoding ASCII
    Unblock-File $installedDll -ErrorAction SilentlyContinue

    if (Test-Path $pendingUpdater) {
        Copy-Item $pendingUpdater $installedUpdater -Force
        Unblock-File $installedUpdater -ErrorAction SilentlyContinue
    }

    Get-ChildItem $StateDir -Filter "pending-*" -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Log "Update to $Version completed successfully."
}
catch {
    if (Test-Path $backupDll) { Copy-Item $backupDll $installedDll -Force -ErrorAction SilentlyContinue }
    Fail "Could not replace installed files: $($_.Exception.Message)"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

exit 0
