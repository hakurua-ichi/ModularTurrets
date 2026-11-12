param([string]$GameRoot = 'F:\SteamLibrary\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$modsRoot = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
$modTarget = [IO.Path]::GetFullPath((Join-Path $modsRoot 'ModularTurrets'))
if ((Split-Path -Parent $modTarget) -ne $modsRoot) { throw 'Unexpected install target.' }
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Assemblies\ModularTurrets.dll'))) { throw 'Build the mod first.' }
$aboutTarget = Join-Path $modTarget 'About\About.xml'
if (Test-Path -LiteralPath $aboutTarget) {
    [xml]$existingMetadata = Get-Content -LiteralPath $aboutTarget -Raw
    if ($existingMetadata.ModMetaData.packageId -ne 'local.modularturrets') { throw 'The target belongs to another mod.' }
}
. (Join-Path $PSScriptRoot 'RuntimePayload.ps1')
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([char[]]@('\','/'))
$payload = @(Get-ModularTurretsRuntimePayload $sourceRoot)
New-Item -ItemType Directory -Path $modTarget -Force | Out-Null
foreach ($file in $payload) {
    $relative = $file.Substring($sourceRoot.Length).TrimStart([char[]]@('\','/'))
    $destination = [IO.Path]::GetFullPath((Join-Path $modTarget $relative))
    if (-not $destination.StartsWith($modTarget + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected runtime destination.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file -Destination $destination -Force
}
# The replacement preserves DefNames; keep the obsolete file outside Defs to
# avoid loading those names twice when updating an existing installation.
$legacyFan = Join-Path $modTarget 'Defs\FanModule.xml'
if ((Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Defs\ServoModule.xml')) -and
    (Test-Path -LiteralPath $legacyFan)) {
    $legacyDirectory = Join-Path $modTarget 'Legacy'
    New-Item -ItemType Directory -Path $legacyDirectory -Force | Out-Null
    $legacyBackup = Join-Path $legacyDirectory ('FanModule-' + [Guid]::NewGuid().ToString('N') + '.xml')
    Move-Item -LiteralPath $legacyFan -Destination $legacyBackup
}
foreach ($fileName in @('LoadFolders.xml','NOTICE.txt','LICENSE')) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $fileName)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fileName) -Destination $modTarget -Force }
}
# Retired owned shaders stay recoverable outside Mods when updating an old install.
$shaderDirectory = [IO.Path]::GetFullPath((Join-Path $modTarget 'Shaders'))
if ((Split-Path -Parent $shaderDirectory) -ne $modTarget) { throw 'Unexpected shader migration source.' }
if (Test-Path -LiteralPath $shaderDirectory -PathType Container) {
    if ((Get-Item -LiteralPath $modTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked mod migration target.' }
    if ((Get-Item -LiteralPath $shaderDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked shader directory.' }
    $gameDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $modsRoot))
    $shaderBackupRoot = [IO.Path]::GetFullPath((Join-Path $gameDirectory 'ModularTurretsLegacy'))
    if ((Split-Path -Parent $shaderBackupRoot) -ne $gameDirectory -or
        $shaderBackupRoot.StartsWith($modsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected shader backup root.'
    }
    if ((Test-Path -LiteralPath $shaderBackupRoot) -and
        ((Get-Item -LiteralPath $shaderBackupRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked shader backup root.' }
    $shaderBackupDirectory = $null
    foreach ($shaderName in @('MT_InstancedSpriteCutout.frag.glsl','MT_ParagonHousingCutout.frag.glsl')) {
        $oldShader = [IO.Path]::GetFullPath((Join-Path $shaderDirectory $shaderName))
        if ((Split-Path -Parent $oldShader) -ne $shaderDirectory) { throw 'Unexpected shader migration file.' }
        if (-not (Test-Path -LiteralPath $oldShader -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $oldShader).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked legacy shader.' }
        if (-not $shaderBackupDirectory) {
            $backupName = (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N')
            $shaderBackupDirectory = [IO.Path]::GetFullPath((Join-Path $shaderBackupRoot $backupName))
            if ((Split-Path -Parent $shaderBackupDirectory) -ne $shaderBackupRoot) { throw 'Unexpected shader backup directory.' }
            New-Item -ItemType Directory -Path $shaderBackupDirectory -Force | Out-Null
        }
        $shaderBackup = [IO.Path]::GetFullPath((Join-Path $shaderBackupDirectory $shaderName))
        if ((Split-Path -Parent $shaderBackup) -ne $shaderBackupDirectory) { throw 'Unexpected shader backup file.' }
        Move-Item -LiteralPath $oldShader -Destination $shaderBackup
        Write-Output "Preserved obsolete shader: $shaderBackup"
    }
    # Leave foreign files alone; remove only this exact directory when it is empty.
    if (@(Get-ChildItem -LiteralPath $shaderDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $shaderDirectory
    }
}
# Old unqualified custom-type folders are rejected by Core's language loader.
# Preserve local edits outside Languages instead of leaving duplicate folders.
$oldLanguageRoots = @(
    @{ Root = 'Languages'; Types = @('ModLanguageDef','ModuleEffectDef') },
    @{ Root = 'Compat\CombatExtended\Languages'; Types = @('AmmoSetDef') }
)
foreach ($entry in $oldLanguageRoots) {
    $languageRoot = Join-Path $modTarget $entry.Root
    if (-not (Test-Path -LiteralPath $languageRoot -PathType Container)) { continue }
    if ((Get-Item -LiteralPath $languageRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked language root.' }
    foreach ($language in Get-ChildItem -LiteralPath $languageRoot -Directory) {
        if ($language.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked language folder.' }
        foreach ($oldType in $entry.Types) {
            $oldFolder = [IO.Path]::GetFullPath((Join-Path $language.FullName ('DefInjected\' + $oldType)))
            if (-not (Test-Path -LiteralPath $oldFolder -PathType Container)) { continue }
            $backup = [IO.Path]::GetFullPath((Join-Path $modTarget ('Legacy\LanguageFolders\' + [Guid]::NewGuid().ToString('N') + '\' + $entry.Root + '\' + $language.Name + '\DefInjected\' + $oldType)))
            foreach ($path in @($oldFolder, $backup)) {
                if (-not $path.StartsWith($modTarget + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Language migration escaped the install target.' }
            }
            foreach ($path in @($oldFolder, (Split-Path -Parent $oldFolder), (Join-Path $modTarget 'Legacy'))) {
                if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked legacy language path.' }
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Move-Item -LiteralPath $oldFolder -Destination $backup
            Write-Output "Preserved old language folder: $backup"
        }
    }
}
$oldAtlas = [IO.Path]::GetFullPath((Join-Path $modTarget 'Textures\ModularTurrets\ModuleDetailsAtlas.png'))
if (Test-Path -LiteralPath $oldAtlas -PathType Leaf) {
    $backup = [IO.Path]::GetFullPath((Join-Path $modTarget ('Legacy\Resources\ModuleDetailsAtlas-' + [Guid]::NewGuid().ToString('N') + '.png')))
    foreach ($path in @($oldAtlas, $backup)) {
        if (-not $path.StartsWith($modTarget + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Resource migration escaped the install target.' }
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Move-Item -LiteralPath $oldAtlas -Destination $backup
}
Write-Output "Installed: $modTarget"
