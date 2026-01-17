# Build script for AssimpMetadata

# Clean
Remove-Item -Recurse -Force generation\Assimp\bin, generation\Assimp\obj, examples\BasicUsage\bin, examples\BasicUsage\obj -ErrorAction SilentlyContinue

# Build
dotnet build generation\Assimp\Assimp.proj

# Pack
dotnet pack nuget\LittleToxic.AssimpMetadata.proj
