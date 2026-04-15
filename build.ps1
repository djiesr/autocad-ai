param(
  [string]$Configuration = "Release",
  [string]$AcadDir = "",
  [switch]$UniqueOutDir
)

$ErrorActionPreference = "Stop"

function Resolve-AcadDir {
  param([string]$Override)

  if ($Override -and (Test-Path $Override)) {
    return (Resolve-Path $Override).Path
  }

  $candidates = @(
    "C:\Program Files\Autodesk\AutoCAD 2026",
    "C:\Program Files\Autodesk\AutoCAD 2025",
    "C:\Program Files\Autodesk\AutoCAD 2024"
  )

  foreach ($c in $candidates) {
    if (Test-Path $c) { return $c }
  }

  throw "Impossible de trouver AutoCAD. Passe -AcadDir 'C:\Program Files\Autodesk\AutoCAD 2025' (ou 2024)."
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\AutocadAI.Plugin\AutocadAI.Plugin.csproj"

if (!(Test-Path $proj)) {
  throw "Projet introuvable: $proj"
}

$acad = Resolve-AcadDir -Override $AcadDir
Write-Host "AutoCAD DLLs: $acad"
Write-Host "Build: $Configuration"

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outDir = ""
if ($UniqueOutDir) {
  # No trailing backslash: avoids argument parsing edge-cases
  $outDir = Join-Path $root "artifacts\build_$timestamp"
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null
  Write-Host "OutDir: $outDir (mode anti-verrouillage)"
}

function Resolve-VsMsbuild {
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (!(Test-Path $vswhere)) { return $null }

  $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
  if (!$installPath) { return $null }

  $msbuild = Join-Path $installPath "MSBuild\Current\Bin\MSBuild.exe"
  if (Test-Path $msbuild) { return $msbuild }
  return $null
}

$msbuild = Resolve-VsMsbuild
if ($msbuild) {
  Write-Host "MSBuild: $msbuild"

  # Pour les projets WPF .NET Framework, on veut MSBuild (.NET Framework) mais les SDKs du dotnet SDK
  # (sinon VS peut binder sur des tasks WPF trop anciennes / GAC, ou sur des chemins incomplets).
  try {
    $dotnetSdkVersion = (dotnet --version).Trim()
    $dotnetSdksPath = "C:\Program Files\dotnet\sdk\$dotnetSdkVersion\Sdks"
    if (Test-Path $dotnetSdksPath) {
      $env:MSBuildSDKsPath = $dotnetSdksPath
      Write-Host "MSBuildSDKsPath: $env:MSBuildSDKsPath"
    }
  } catch {
    # Fallback silencieux: on garde le MSBuildSDKsPath par défaut
  }

  $commonProps = @(
    "/p:Configuration=$Configuration",
    "/p:ACAD_DLLS=$acad",
    "/v:minimal",
    "/restore"
  )
  if ($UniqueOutDir) {
    & $msbuild $proj @commonProps "/p:OutDir=$outDir"
  } else {
    & $msbuild $proj @commonProps
  }
} else {
  Write-Host "MSBuild Visual Studio introuvable, fallback sur dotnet build"
  if ($UniqueOutDir) {
    dotnet build $proj -c $Configuration -v minimal /p:Platform=x64 /p:ACAD_DLLS="$acad" /p:OutDir="$outDir"
  } else {
    dotnet build $proj -c $Configuration -v minimal /p:Platform=x64 /p:ACAD_DLLS="$acad"
  }
}

Write-Host ""
Write-Host "DLL output:"
if ($UniqueOutDir) {
  Write-Host " - $outDir\\AutocadAI.Plugin.dll"
} else {
  Write-Host " - src\AutocadAI.Plugin\bin\$Configuration\net48\AutocadAI.Plugin.dll"
}
Write-Host ""
Write-Host "Dans AutoCAD: NETLOAD -> cette DLL, puis AICHAT / AIWORKSPACE."

