# LittleToxic.AssimpMetadata

Windows Metadata (.winmd) for the [Assimp](https://github.com/assimp/assimp) (Open Asset Import Library) C API, enabling use with [CsWin32](https://github.com/microsoft/CsWin32) source generator.

## Features

- Complete metadata for Assimp 6.0.2 C API
- Includes native assimp.dll for Windows x64
- Seamless integration with CsWin32

## Installation

1. Install this package:
   ```
   dotnet add package LittleToxic.AssimpMetadata --prerelease
   ```

2. Install CsWin32 (required separately):
   ```
   dotnet add package Microsoft.Windows.CsWin32 --prerelease
   ```

## Usage

1. Create a `NativeMethods.txt` file in your project root:
   ```
   ImportFile
   ReleaseImport
   GetErrorString
   Scene
   Mesh
   ```

2. Build your project. CsWin32 automatically generates P/Invoke bindings.

3. Use the generated APIs:
   ```csharp
   using Windows.Win32;

   unsafe
   {
       var scene = PInvoke.ImportFile("model.obj", 0);
       // ... work with the scene
       PInvoke.ReleaseImport(scene);
   }
   ```

## Requirements

- Windows x64
- Microsoft.Windows.CsWin32 (installed separately)

## Supported APIs & Naming Conventions

All Assimp C API functions are available. The metadata applies the following naming conventions to provide a cleaner .NET experience:

- **Prefix Removal**: The `ai` prefix has been removed from unambiguous types.
  - `aiScene` → `Scene`
  - `aiMesh` → `Mesh`
  - `aiNode` → `Node`
- **Disambiguation**: Types that conflict with .NET standard types are prefixed with `Assimp`.
  - `aiString` → `AssimpString`
- **Simplified Enums**: Redundant prefixes in enumeration members have been stripped.
  - `aiPostProcessSteps.aiProcess_Triangulate` → `PostProcessSteps.Triangulate`
  - `aiTextureType_DIFFUSE` → `TextureType.DIFFUSE`
- **Functions**: Standard C functions are available directly (e.g., `ImportFile`, `ReleaseImport`, `GetErrorString`).


## License

MIT License. Assimp library is under BSD-3-Clause license.
