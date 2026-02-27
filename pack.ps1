# Build script for AssimpMetadata

# Clean
Remove-Item -Recurse -Force generation\Assimp\bin, generation\Assimp\obj, examples\BasicUsage\bin, examples\BasicUsage\obj, apidocs\ScrapeDocs\bin, apidocs\ScrapeDocs\obj, apidocs\xml -ErrorAction SilentlyContinue

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
