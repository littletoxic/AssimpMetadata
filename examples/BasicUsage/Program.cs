using System.Runtime.InteropServices;
using Assimp;
using Windows.Win32.Foundation;

unsafe
{
    if (args.Length == 0)
    {
        Console.WriteLine("Usage: BasicUsage <model-file>");
        Console.WriteLine("Example: BasicUsage model.obj");
        return 1;
    }

    string filePath = args[0];
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"Error: File not found: {filePath}");
        return 1;
    }

    // Import the model with triangulation and generate normals
    uint flags = (uint)(PostProcessSteps.Triangulate | PostProcessSteps.GenNormals);

    // CsWin32 generates a friendly string overload
    Scene* scene = PInvoke.ImportFile(filePath, flags);

    if (scene == null)
    {
        PCSTR error = PInvoke.GetErrorString();
        string errorMsg = error.ToString() ?? "Unknown error";
        Console.WriteLine($"Error loading model: {errorMsg}");
        return 1;
    }

    try
    {
        Console.WriteLine($"Loaded: {filePath}");
        Console.WriteLine($"  Meshes: {scene->mNumMeshes}");
        Console.WriteLine($"  Materials: {scene->mNumMaterials}");
        Console.WriteLine($"  Textures: {scene->mNumTextures}");
        Console.WriteLine($"  Animations: {scene->mNumAnimations}");
        Console.WriteLine($"  Lights: {scene->mNumLights}");
        Console.WriteLine($"  Cameras: {scene->mNumCameras}");

        // Print mesh details
        for (uint i = 0; i < scene->mNumMeshes; i++)
        {
            Mesh* mesh = scene->mMeshes[i];
            string meshName = GetAiString(&mesh->mName);
            Console.WriteLine($"  Mesh[{i}]: \"{meshName}\" - {mesh->mNumVertices} vertices, {mesh->mNumFaces} faces");
        }
    }
    finally
    {
        // Always release the imported scene
        PInvoke.ReleaseImport(scene);
    }

    return 0;
}

static unsafe string GetAiString(AssimpString* str)
{
    if (str == null || str->length == 0)
        return string.Empty;

    // CsWin32 wraps fixed arrays with CHAR type - cast to sbyte* for Marshal
    sbyte* pData = (sbyte*)&str->data._0;
    return Marshal.PtrToStringUTF8((nint)pData, (int)str->length) ?? string.Empty;
}
