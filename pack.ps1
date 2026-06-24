# Build script for AssimpMetadata

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$assimpVersion = "6.0.5"
$assetCache = Join-Path $repoRoot ".asset-cache\assimp-$assimpVersion"

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

    Add-Type -AssemblyName System.IO.Compression.FileSystem
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
    $runtimes = Join-Path $repoRoot "runtimes"
    Remove-Item -Recurse -Force $runtimes -ErrorAction SilentlyContinue

    Copy-ZipEntry (Get-AssimpAsset "windows-x64-v$assimpVersion.zip") "Release/assimp-vc143-mt.dll" (Join-Path $runtimes "win-x64\native\assimp.dll")
    Copy-ZipEntry (Get-AssimpAsset "windows-x86-v$assimpVersion.zip") "Release/assimp-vc143-mt.dll" (Join-Path $runtimes "win-x86\native\assimp.dll")
    Copy-ZipEntry (Get-AssimpAsset "linux-x64-v$assimpVersion.zip") "libassimp.so" (Join-Path $runtimes "linux-x64\native\libassimp.so")
    Copy-ZipEntry (Get-AssimpAsset "macos-arm64-v$assimpVersion.zip") "libassimp.dylib" (Join-Path $runtimes "osx-arm64\native\libassimp.dylib")
}

# Clean
Remove-Item -Recurse -Force generation\Assimp\bin, generation\Assimp\obj, examples\BasicUsage\bin, examples\BasicUsage\obj, apidocs\ScrapeDocs\bin, apidocs\ScrapeDocs\obj, apidocs\xml -ErrorAction SilentlyContinue

# Update native runtime assets
Update-NativeRuntimes

# Build WinMD
dotnet build generation\Assimp\Assimp.proj

# Generate Doxygen XML
Write-Host "Generating Doxygen XML..."
Push-Location apidocs
doxygen Doxyfile
Pop-Location

# Generate API documentation
Write-Host "Generating API documentation..."
dotnet run --project apidocs\ScrapeDocs\ScrapeDocs.csproj -- "apidocs\xml" "bin\apidocs.msgpack" "generation\Assimp\scraper.settings.rsp" "generation\Assimp\enum.remap.rsp"

# Pack
dotnet pack nuget\LittleToxic.AssimpMetadata.proj
