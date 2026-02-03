#:property PublishAot=false

#:package MessagePack@2.5.187
#:package Microsoft.Windows.SDK.Win32Docs@0.1.42-alpha

using MessagePack;
using Microsoft.Windows.SDK.Win32Docs;
using System.IO;

var data = MessagePackSerializer.Deserialize<Dictionary<string, ApiDetails>>(File.ReadAllBytes("bin/apidocs.msgpack"));
Console.WriteLine($"Total APIs: {data.Count}");
Console.WriteLine("\nFirst 20 keys:");
foreach (var key in data.Keys.Take(20))
{
    var api = data[key];
    Console.WriteLine($"  {key}: {api.Description?.Substring(0, Math.Min(60, api.Description?.Length ?? 0))}...");
}
Console.WriteLine("\nSample - ImportFile:");
if (data.TryGetValue("ImportFile", out var importFile))
{
    Console.WriteLine($"  Description: {importFile.Description}");
    Console.WriteLine($"  Parameters: {string.Join(", ", importFile.Parameters.Keys)}");
    Console.WriteLine($"  ReturnValue: {importFile.ReturnValue}");
}
