#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$workspaceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $modRoot))
. (Join-Path $modRoot 'RuntimePayload.ps1')

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside its expected parent: $resolved"
    }
    return $resolved
}

function Assert-NoLink([string]$Path) {
    if ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Linked paths are not packaged: $Path"
    }
}

function Get-SafeFiles([string]$Directory) {
    Assert-NoLink $Directory
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force | Sort-Object Name) {
        if ($item.Name -in @('bin','obj','dist','.work','.git','__pycache__','Saves','Savegames','Logs','Output','Results','.vs','.idea','.secrets')) { continue }
        if ($item.Name -like '.env*' -or $item.Extension -in @('.key','.pem','.rws','.log')) { continue }
        Assert-NoLink $item.FullName
        if ($item.PSIsContainer) { Get-SafeFiles $item.FullName }
        else { $item.FullName }
    }
}

function Copy-ProjectFile([string]$SourcePath, [string]$DestinationRoot, [string]$SourceRoot = $modRoot) {
    $source = Assert-ChildPath $SourcePath $SourceRoot
    Assert-NoLink $source
    $relative = [IO.Path]::GetRelativePath($SourceRoot, $source)
    $destination = Assert-ChildPath (Join-Path $DestinationRoot $relative) $DestinationRoot
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

function Get-ManifestEntries([string]$PackageRoot, [string]$Prefix = 'ModularTurrets') {
    foreach ($file in Get-SafeFiles $PackageRoot) {
        $relative = [IO.Path]::GetRelativePath($PackageRoot, $file).Replace('\','/')
        [pscustomobject]@{
            Path = $Prefix + '/' + $relative
            Bytes = (Get-Item -LiteralPath $file).Length
            SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function Write-NewUtf8File([string]$Path, [string]$Text) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write($Text) }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

$metadataPath = Join-Path $modRoot 'About\About.xml'
[xml]$metadata = Get-Content -LiteralPath $metadataPath -Raw
if ($metadata.ModMetaData.packageId -ne 'local.modularturrets') { throw 'Unexpected package ID.' }
$version = [string]$metadata.ModMetaData.modVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') { throw 'Invalid package version.' }
if (-not (Test-Path -LiteralPath (Join-Path $modRoot 'Assemblies\ModularTurrets.dll') -PathType Leaf)) {
    throw 'Build the mod before packaging.'
}

$runtimeFolders = @('About','Assemblies','Defs','Patches','Textures','Languages')
$runtimeFiles = @('LoadFolders.xml','NOTICE.txt','LICENSE')
if (-not (Test-Path -LiteralPath (Join-Path $modRoot 'Analyzer.xml') -PathType Leaf)) { throw 'Analyzer.xml is missing.' }
foreach ($folder in $runtimeFolders + @('Source')) {
    $path = Join-Path $modRoot $folder
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Required folder is missing: $folder" }
}
foreach ($name in $runtimeFiles + @('Build.ps1','Build-Compat.ps1','RuntimePayload.ps1','Install.ps1','Package.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $modRoot $name) -PathType Leaf)) { throw "Required file is missing: $name" }
}

$workRoot = Assert-ChildPath (Join-Path $workspaceRoot '.work') $workspaceRoot
$outputRoot = Assert-ChildPath (Join-Path $workspaceRoot 'dist') $workspaceRoot
foreach ($directory in @($workRoot, $outputRoot)) {
    if (Test-Path -LiteralPath $directory) { Assert-NoLink $directory }
    else { New-Item -ItemType Directory -Path $directory | Out-Null }
}
$stagingRoot = Assert-ChildPath (Join-Path $workRoot ('modular-turrets-package-' + [Guid]::NewGuid().ToString('N'))) $workRoot
if ((Split-Path -Leaf $stagingRoot) -notmatch '^modular-turrets-package-[a-f0-9]{32}$') { throw 'Unexpected staging path.' }
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
Assert-NoLink $stagingRoot

$stem = 'ModularTurrets-' + $version
while (@('', '-Source' | ForEach-Object {
    Test-Path -LiteralPath (Join-Path $outputRoot ($stem + $_ + '.zip'))
    Test-Path -LiteralPath (Join-Path $outputRoot ($stem + $_ + '.manifest.json'))
}) -contains $true) {
    $stem = 'ModularTurrets-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
}

$runtimePayload = @(
    Get-ModularTurretsRuntimePayload $modRoot
    foreach ($name in $runtimeFiles) { Join-Path $modRoot $name }
)
$sourceCode = @(Get-SafeFiles (Join-Path $modRoot 'Source') |
    Where-Object { [IO.Path]::GetExtension($_) -in @('.cs','.csproj','.props','.md') })
if (-not ($sourceCode | Where-Object { [IO.Path]::GetExtension($_) -eq '.cs' }) -or
    -not ($sourceCode | Where-Object { [IO.Path]::GetExtension($_) -eq '.csproj' })) { throw 'Source files are missing.' }
$sourceCompat = @()
$compatRoot = Join-Path $modRoot 'Compat'
if (Test-Path -LiteralPath $compatRoot -PathType Container) {
    $sourceCompat = @(Get-SafeFiles $compatRoot | Where-Object {
        $relative = [IO.Path]::GetRelativePath($compatRoot, $_).Replace('\','/')
        $relative -notmatch '(^|/)(Assemblies|bin|obj)(/|$)' -and [IO.Path]::GetExtension($_) -in @('.cs','.csproj','.props','.xml','.png','.glsl','.json','.wav','.ogg','.txt')
    })
}
$sourceExtras = @($sourceCode) + $sourceCompat + @(foreach ($name in @('Build.ps1','Build-Compat.ps1','RuntimePayload.ps1','Install.ps1','Package.ps1')) { Join-Path $modRoot $name })
$sourceExtras += @(Get-SafeFiles (Join-Path $modRoot 'Languages') | Where-Object { [IO.Path]::GetExtension($_) -eq '.md' })
Add-Type -AssemblyName System.IO.Compression.FileSystem
$results = @()
foreach ($kind in @('Runtime','Source')) {
    $stageParent = Assert-ChildPath (Join-Path $stagingRoot $kind) $stagingRoot
    $packageRoot = Assert-ChildPath (Join-Path $stageParent 'ModularTurrets') $stageParent
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    $payload = if ($kind -eq 'Source') { $runtimePayload + $sourceExtras } else { $runtimePayload }
    foreach ($file in $payload | Sort-Object -Unique) { Copy-ProjectFile $file $packageRoot }
    # The content manifest lists payload files; it cannot include its own hash.
    $payloadEntries = @(Get-ManifestEntries $packageRoot)
    $manifestText = ($payloadEntries | ForEach-Object { $_.SHA256 + '  ' + $_.Path }) -join "`n"
    Write-NewUtf8File (Join-Path $packageRoot 'MANIFEST.sha256') ($manifestText + "`n")
    $allEntries = @(Get-ManifestEntries $packageRoot)
    $suffix = if ($kind -eq 'Source') { '-Source' } else { '' }
    $archiveName = $stem + $suffix + '.zip'
    $archivePath = Assert-ChildPath (Join-Path $outputRoot $archiveName) $outputRoot
    [IO.Compression.ZipFile]::CreateFromDirectory($stageParent, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPath = Assert-ChildPath (Join-Path $outputRoot ($stem + $suffix + '.manifest.json')) $outputRoot
    $manifest = [ordered]@{
        SchemaVersion = 1
        PackageId = 'local.modularturrets'
        Version = $version
        Kind = $kind
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        Archive = $archiveName
        ArchiveBytes = (Get-Item -LiteralPath $archivePath).Length
        ArchiveSHA256 = $archiveHash
        FileCount = $allEntries.Count
        PayloadFileCount = $payloadEntries.Count
        ContentManifest = 'ModularTurrets/MANIFEST.sha256'
        Files = $allEntries
    }
    Write-NewUtf8File $manifestPath ($manifest | ConvertTo-Json -Depth 6)
    $results += [pscustomobject]@{
        Kind = $kind
        Archive = $archivePath
        FileCount = $allEntries.Count
        SHA256 = $archiveHash
        Manifest = $manifestPath
    }
}
# Both archives and their manifests now exist. Failed packaging retains its
# staging tree for diagnosis; successful packaging must not keep another copy.
[void](Assert-ChildPath $stagingRoot $workRoot)
if ((Split-Path -Parent $stagingRoot) -ne $workRoot -or
    (Split-Path -Leaf $stagingRoot) -notmatch '^modular-turrets-package-[a-f0-9]{32}$') {
    throw 'Unexpected staging cleanup path.'
}
Assert-NoLink $stagingRoot
Remove-Item -LiteralPath $stagingRoot -Recurse -Force
$results | ConvertTo-Json -Depth 4
