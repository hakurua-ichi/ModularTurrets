param(
    [string]$GameRoot = 'F:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath,
    [switch]$WithLogicalFloors,
    [switch]$WithCombatExtended,
    [switch]$WithVisibleWealth,
    [switch]$WithMilira,
    [switch]$WithMiliraImperium,
    [switch]$WithMonolyn,
    [switch]$WithMaru,
    [switch]$WithWolfein,
    [switch]$WithAdaptiveStorage,
    [string]$LogicalFloorsPath,
    [string]$CombatExtendedPath,
    [string]$VisibleWealthPath,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
if (-not $HarmonyPath) {
    $HarmonyPath = Join-Path $GameRoot '..\..\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'
}
if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) {
    throw 'Install the standard Harmony mod (brrainz.harmony), or pass -HarmonyPath pointing to its RimWorld 1.7 0Harmony.dll. This DLL is referenced but not bundled.'
}
$HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path
& dotnet build (Join-Path $PSScriptRoot 'Source\ModularTurrets.csproj') -c Release --nologo "-p:GameRoot=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'ModularTurrets build failed.' }
$compatibility = @()
if ($WithLogicalFloors) { $compatibility += 'LogicalFloors' }
if ($WithCombatExtended) { $compatibility += 'CombatExtended' }
if ($WithVisibleWealth) { $compatibility += 'VisibleWealth' }
if ($WithMilira) { $compatibility += 'Milira' }
if ($WithMiliraImperium) { $compatibility += 'MiliraImperium' }
if ($WithMonolyn) { $compatibility += 'Monolyn' }
if ($WithMaru) { $compatibility += 'Maru' }
if ($WithWolfein) { $compatibility += 'Wolfein' }
if ($WithAdaptiveStorage) { $compatibility += 'AdaptiveStorage' }
if ($compatibility.Count -gt 0) {
    & (Join-Path $PSScriptRoot 'Build-Compat.ps1') -Modules $compatibility -GameRoot $GameRoot -HarmonyPath $HarmonyPath -LogicalFloorsPath $LogicalFloorsPath -CombatExtendedPath $CombatExtendedPath -VisibleWealthPath $VisibleWealthPath
}
if ($Install) { & (Join-Path $PSScriptRoot 'Install.ps1') -GameRoot $GameRoot }
