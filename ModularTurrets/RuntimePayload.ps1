# Read-only runtime selection shared by Install and Package. Foreign reference
# assemblies and development outputs never become mod payloads.
function Get-ModularTurretsRuntimeAssets([string]$Directory) {
    $root = Get-Item -LiteralPath $Directory -Force
    if ($root.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked runtime folder: $Directory" }
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force | Sort-Object Name) {
        if ($item.Name -in @('Source','bin','obj','dist','.work','.git','__pycache__','Saves','Savegames','Logs')) { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked runtime path: $($item.FullName)" }
        if ($item.PSIsContainer) { Get-ModularTurretsRuntimeAssets $item.FullName }
        elseif ($item.Extension -in @('.xml','.png','.glsl','.wav','.ogg')) { $item.FullName }
    }
}

function Get-ModularTurretsRuntimePayload([string]$ModRoot) {
    $core = Join-Path $ModRoot 'Assemblies\ModularTurrets.dll'
    if (-not (Test-Path -LiteralPath $core -PathType Leaf)) { throw 'Build Core before selecting its runtime payload.' }
    if ((Get-Item -LiteralPath $core).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked runtime assembly: $core" }
    $core
    # Keep the translator instructions available in the installed mod as well.
    $translationGuide = Join-Path $ModRoot 'Languages\README.md'
    if (Test-Path -LiteralPath $translationGuide -PathType Leaf) {
        if ((Get-Item -LiteralPath $translationGuide).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked translation guide: $translationGuide" }
        $translationGuide
    }
    # Dubs reads this optional profiling registration from the mod root.
    $analyzer = Join-Path $ModRoot 'Analyzer.xml'
    if (Test-Path -LiteralPath $analyzer -PathType Leaf) {
        if ((Get-Item -LiteralPath $analyzer).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked analyzer registration: $analyzer" }
        $analyzer
    }
    foreach ($folder in @('About','Defs','Patches','Textures','Languages','Shaders','Sounds')) {
        $path = Join-Path $ModRoot $folder
        if (Test-Path -LiteralPath $path -PathType Container) { Get-ModularTurretsRuntimeAssets $path }
    }
    foreach ($module in @('LogicalFloors','CombatExtended','VisibleWealth','Milira','MiliraImperium','Monolyn','Maru','Wolfein','AdaptiveStorage')) {
        $moduleRoot = Join-Path $ModRoot ('Compat\' + $module)
        $assembly = Join-Path $moduleRoot ('Assemblies\ModularTurrets.Compat.' + $module + '.dll')
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
            Write-Verbose "Optional module is not built and is omitted: $module"
            continue
        }
        foreach ($directory in @($moduleRoot, (Join-Path $moduleRoot 'Assemblies'))) {
            if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked compatibility folder: $directory" }
        }
        if ((Get-Item -LiteralPath $assembly).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked compatibility assembly: $assembly" }
        $assembly
        foreach ($folder in @('About','Defs','Patches','Textures','Languages','Shaders','Sounds')) {
            $path = Join-Path $moduleRoot $folder
            if (Test-Path -LiteralPath $path -PathType Container) { Get-ModularTurretsRuntimeAssets $path }
        }
    }
}
