# Build script for AssimpMetadata

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'

function Get-AssimpVersionInfo {
    $document = [xml][IO.File]::ReadAllText($versionPropsPath)
    $versionNode = $document.SelectSingleNode('/Project/PropertyGroup/AssimpUpstreamVersion')
    $commitNode = $document.SelectSingleNode('/Project/PropertyGroup/AssimpUpstreamCommit')

    if ($null -eq $versionNode -or $null -eq $commitNode) {
        throw "Missing Assimp version properties in '$versionPropsPath'."
    }

    $version = $versionNode.InnerText.Trim()
    $commit = $commitNode.InnerText.Trim()

    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid Assimp version '$version' in '$versionPropsPath'."
    }

    if ($commit -notmatch '^[0-9a-f]{40}$') {
        throw "Invalid Assimp commit '$commit' in '$versionPropsPath'."
    }

    return [PSCustomObject]@{
        Version = $version
        Commit = $commit
    }
}

$assimpVersionInfo = Get-AssimpVersionInfo
$assimpVersion = $assimpVersionInfo.Version
$assimpCommit = $assimpVersionInfo.Commit
$assimpTag = "v$assimpVersion"
$assimpRepository = 'https://github.com/assimp/assimp.git'
$assetCache = Join-Path $repoRoot ".asset-cache\assimp-$assimpVersion"
$sourceCache = Join-Path $repoRoot ".asset-cache\assimp-source-$assimpVersion-$assimpCommit"
$assimpIncludeDestination = Join-Path $repoRoot 'generation\Assimp\include\assimp'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    & git @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "git $($ArgumentList -join ' ') failed with exit code $exitCode."
    }
}

function Get-AssimpSource {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sourceCache) | Out-Null

    if (-not (Test-Path -LiteralPath $sourceCache)) {
        Write-Host "Fetching Assimp $assimpVersion headers..."
        try {
            Invoke-Git -ArgumentList @(
                'clone'
                '--filter=blob:none'
                '--no-checkout'
                '--depth'
                '1'
                '--branch'
                $assimpTag
                $assimpRepository
                $sourceCache
            )
            Invoke-Git -ArgumentList @('-C', $sourceCache, 'config', 'core.autocrlf', 'false')
            Invoke-Git -ArgumentList @('-C', $sourceCache, 'sparse-checkout', 'init', '--cone')
            Invoke-Git -ArgumentList @('-C', $sourceCache, 'sparse-checkout', 'set', 'include/assimp')
            Invoke-Git -ArgumentList @('-C', $sourceCache, 'checkout', '--detach', '--quiet', 'HEAD')
        }
        catch {
            if (Test-Path -LiteralPath $sourceCache) {
                Remove-Item -LiteralPath $sourceCache -Recurse -Force
            }
            throw
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $sourceCache '.git'))) {
        throw "Invalid Assimp source cache '$sourceCache'."
    }

    $commitOutput = @(Invoke-Git -ArgumentList @('-C', $sourceCache, 'rev-parse', 'HEAD'))
    $actualCommit = ($commitOutput | Select-Object -Last 1).Trim()
    if ($actualCommit -cne $assimpCommit) {
        throw "Assimp tag '$assimpTag' resolved to '$actualCommit' instead of '$assimpCommit'."
    }

    $includeDirectory = Join-Path $sourceCache 'include\assimp'
    if (-not (Test-Path -LiteralPath (Join-Path $includeDirectory 'config.h.in'))) {
        throw "Missing Assimp config template in '$includeDirectory'."
    }

    return $includeDirectory
}

function New-AssimpConfigHeader {
    param(
        [Parameter(Mandatory)]
        [string]$IncludeDirectory
    )

    $templatePath = Join-Path $IncludeDirectory 'config.h.in'
    $outputPath = Join-Path $IncludeDirectory 'config.h'
    $marker = '#cmakedefine ASSIMP_DOUBLE_PRECISION 1'
    $replacement = '/* #undef ASSIMP_DOUBLE_PRECISION */'

    $content = [IO.File]::ReadAllText($templatePath)
    $matchCount = [regex]::Matches($content, [regex]::Escape($marker)).Count
    if ($matchCount -ne 1) {
        throw "Expected exactly one '$marker' in '$templatePath' but found $matchCount."
    }

    $content = $content.Replace($marker, $replacement)
    if ($content -match '(?m)^\s*#cmakedefine(?:01)?\b|@[A-Za-z_][A-Za-z0-9_]*@') {
        throw "Unsupported CMake placeholders remain in '$templatePath'."
    }

    $content = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText($outputPath, $content, [Text.UTF8Encoding]::new($false))
}

function Update-AssimpHeaders {
    $sourceDirectory = Get-AssimpSource
    $destinationParent = Split-Path -Parent $assimpIncludeDestination

    if (Test-Path -LiteralPath $assimpIncludeDestination) {
        Remove-Item -LiteralPath $assimpIncludeDestination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    Copy-Item -LiteralPath $sourceDirectory -Destination $destinationParent -Recurse -Force
    New-AssimpConfigHeader -IncludeDirectory $assimpIncludeDestination

    Write-Host "Prepared Assimp $assimpVersion headers from $assimpCommit."
}

function Get-AssimpAsset {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    New-Item -ItemType Directory -Force -Path $assetCache | Out-Null
    $path = Join-Path $assetCache $Name
    if (-not (Test-Path -LiteralPath $path)) {
        $url = "https://github.com/assimp/assimp/releases/download/v$assimpVersion/$Name"
        Invoke-WebRequest -Uri $url -OutFile $path
    }

    return $path
}

function Copy-ZipEntry {
    param(
        [Parameter(Mandatory)]
        [string]$ZipPath,
        [Parameter(Mandatory)]
        [string]$EntryName,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entry = $zip.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Missing entry '$EntryName' in '$ZipPath'."
        }

        $destinationDirectory = Split-Path -Parent $Destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        $entryStream = $entry.Open()
        try {
            $fileStream = [IO.File]::Create($Destination)
            try {
                $entryStream.CopyTo($fileStream)
            }
            finally {
                $fileStream.Dispose()
            }
        }
        finally {
            $entryStream.Dispose()
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Update-NativeRuntimes {
    $runtimes = Join-Path $repoRoot 'runtimes'
    Remove-Item -LiteralPath $runtimes -Recurse -Force -ErrorAction SilentlyContinue

    Copy-ZipEntry (Get-AssimpAsset "windows-x64-v$assimpVersion.zip") 'Release/assimp-vc143-mt.dll' (Join-Path $runtimes 'win-x64\native\assimp.dll')
    Copy-ZipEntry (Get-AssimpAsset "windows-x86-v$assimpVersion.zip") 'Release/assimp-vc143-mt.dll' (Join-Path $runtimes 'win-x86\native\assimp.dll')
    Copy-ZipEntry (Get-AssimpAsset "linux-x64-v$assimpVersion.zip") 'libassimp.so' (Join-Path $runtimes 'linux-x64\native\libassimp.so')
    Copy-ZipEntry (Get-AssimpAsset "macos-arm64-v$assimpVersion.zip") 'libassimp.dylib' (Join-Path $runtimes 'osx-arm64\native\libassimp.dylib')
}

# Clean
$pathsToClean = @(
    'generation\Assimp\bin'
    'generation\Assimp\obj'
    'examples\BasicUsage\bin'
    'examples\BasicUsage\obj'
    'apidocs\ScrapeDocs\bin'
    'apidocs\ScrapeDocs\obj'
    'apidocs\xml'
)
foreach ($relativePath in $pathsToClean) {
    Remove-Item -LiteralPath (Join-Path $repoRoot $relativePath) -Recurse -Force -ErrorAction SilentlyContinue
}

# Update Assimp inputs
Update-AssimpHeaders
Update-NativeRuntimes

# Build WinMD
dotnet build 'generation\Assimp\Assimp.proj'
if ($LASTEXITCODE -ne 0) {
    throw "WinMD build failed with exit code $LASTEXITCODE."
}

# Generate Doxygen XML
Write-Host 'Generating Doxygen XML...'
Push-Location (Join-Path $repoRoot 'apidocs')
try {
    doxygen 'Doxyfile'
    if ($LASTEXITCODE -ne 0) {
        throw "Doxygen failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

# Generate API documentation
Write-Host 'Generating API documentation...'
dotnet run --project 'apidocs\ScrapeDocs\ScrapeDocs.csproj' -- 'apidocs\xml' 'bin\apidocs.msgpack' 'generation\Assimp\scraper.settings.rsp' 'generation\Assimp\enum.remap.rsp'
if ($LASTEXITCODE -ne 0) {
    throw "API documentation generation failed with exit code $LASTEXITCODE."
}

# Pack
dotnet pack 'nuget\LittleToxic.AssimpMetadata.proj'
if ($LASTEXITCODE -ne 0) {
    throw "NuGet pack failed with exit code $LASTEXITCODE."
}
