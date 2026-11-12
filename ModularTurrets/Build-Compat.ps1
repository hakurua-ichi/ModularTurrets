[CmdletBinding()]
param(
    [ValidateSet('LogicalFloors','CombatExtended','VisibleWealth','Milira','MiliraImperium','Monolyn','Maru','Wolfein','AdaptiveStorage')]
    [string[]]$Modules = @('LogicalFloors','CombatExtended','VisibleWealth','Milira','MiliraImperium','Monolyn','Maru','Wolfein','AdaptiveStorage'),
    [string]$GameRoot = 'F:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath,
    [string]$LogicalFloorsPath,
    [string]$CombatExtendedPath,
    [string]$VisibleWealthPath
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Assemblies\ModularTurrets.dll') -PathType Leaf)) {
    throw 'Build Core with Build.ps1 before building optional compatibility modules.'
}
if (-not $HarmonyPath) { $HarmonyPath = Join-Path $GameRoot '..\..\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll' }
foreach ($module in $Modules | Select-Object -Unique) {
    $project = Join-Path $PSScriptRoot ('Compat\' + $module + '\Source\ModularTurrets.Compat.' + $module + '.csproj')
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Compatibility project is missing: $module" }
    $arguments = @('build', $project, '-c', 'Release', '--nologo', "-p:GameRoot=$GameRoot")
    if ($module -eq 'LogicalFloors') {
        if (-not $LogicalFloorsPath) { $LogicalFloorsPath = Join-Path $GameRoot '..\..\workshop\content\294100\3813239016\1.7\Assemblies\dd.LogicalFloors.dll' }
        if (-not (Test-Path -LiteralPath $LogicalFloorsPath -PathType Leaf)) { throw 'Pass -LogicalFloorsPath pointing to the installed RimWorld 1.7 LogicalFloors DLL.' }
        $arguments += "-p:LogicalFloorsPath=$((Resolve-Path -LiteralPath $LogicalFloorsPath).Path)"
    }
    elseif ($module -eq 'VisibleWealth') {
        if (-not $VisibleWealthPath) { $VisibleWealthPath = Join-Path $GameRoot '..\..\workshop\content\294100\3461137081\1.6\Assemblies\VisibleWealth.dll' }
        if (-not (Test-Path -LiteralPath $VisibleWealthPath -PathType Leaf)) { throw 'Pass -VisibleWealthPath pointing to the installed VisibleWealth.dll.' }
        if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) { throw 'Pass -HarmonyPath pointing to standard Harmony for RimWorld 1.7.' }
        $arguments += "-p:VisibleWealthPath=$((Resolve-Path -LiteralPath $VisibleWealthPath).Path)"
        $arguments += "-p:HarmonyPath=$((Resolve-Path -LiteralPath $HarmonyPath).Path)"
    }
    elseif ($module -in @('Milira','MiliraImperium','Monolyn','Maru','Wolfein','AdaptiveStorage')) {
        if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) { throw 'Pass -HarmonyPath pointing to standard Harmony for RimWorld 1.7.' }
        $arguments += "-p:HarmonyPath=$((Resolve-Path -LiteralPath $HarmonyPath).Path)"
    }
    else {
        if (-not $CombatExtendedPath) { $CombatExtendedPath = Join-Path $GameRoot '..\..\workshop\content\294100\1631756268\1.7\Assemblies\CombatExtended.dll' }
        if (-not (Test-Path -LiteralPath $CombatExtendedPath -PathType Leaf)) { throw 'Pass -CombatExtendedPath pointing to a compatible CE 1.7 DLL. A CE 1.6 installation is not a substitute.' }
        if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) { throw 'Pass -HarmonyPath pointing to standard Harmony for RimWorld 1.7.' }
        $arguments += "-p:CombatExtendedPath=$((Resolve-Path -LiteralPath $CombatExtendedPath).Path)"
        $arguments += "-p:HarmonyPath=$((Resolve-Path -LiteralPath $HarmonyPath).Path)"
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Compatibility build failed: $module" }
    Write-Output "Built optional module: $module"
}
