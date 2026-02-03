using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MessagePack;
using Microsoft.Windows.SDK.Win32Docs;

const string BaseHelpUrl = "https://assimp-docs.readthedocs.io/en/latest/";
const string PrefixToStrip = "ai";

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: ScrapeDocs <xml-directory> <output-msgpack> <rsp-file> [<rsp-file>...]");
    Console.Error.WriteLine("  <xml-directory>  Path to Doxygen XML output directory");
    Console.Error.WriteLine("  <output-msgpack> Path to output msgpack file");
    Console.Error.WriteLine("  <rsp-file>       One or more .rsp files containing --remap rules");
    return 1;
}

string xmlDirectory = args[0];
string outputPath = args[1];
string[] rspFiles = args[2..];

if (!Directory.Exists(xmlDirectory))
{
    Console.Error.WriteLine($"Error: XML directory not found: {xmlDirectory}");
    return 1;
}

// Load remap rules from .rsp files
var remapRules = new Dictionary<string, string>();
foreach (var rspFile in rspFiles)
{
    if (!File.Exists(rspFile))
    {
        Console.Error.WriteLine($"Warning: RSP file not found: {rspFile}");
        continue;
    }

    Console.WriteLine($"Loading remap rules from {Path.GetFileName(rspFile)}...");
    LoadRemapRules(rspFile, remapRules);
}

Console.WriteLine($"Loaded {remapRules.Count} remap rules.");

var results = new SortedDictionary<string, ApiDetails>();

// Process all XML files in the directory
foreach (var xmlFile in Directory.GetFiles(xmlDirectory, "*.xml"))
{
    var fileName = Path.GetFileName(xmlFile);

    // Skip index and internal Doxygen files
    if (fileName is "index.xml" or "Doxyfile.xml" or "combine.xslt")
        continue;

    Console.WriteLine($"Processing {fileName}...");

    try
    {
        ProcessXmlFile(xmlFile, results, remapRules);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Warning: Failed to process {fileName}: {ex.Message}");
    }
}

Console.WriteLine($"\nFound documentation for {results.Count} APIs.");

// Serialize to MessagePack
var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrEmpty(outputDir))
{
    Directory.CreateDirectory(outputDir);
}

using var fs = File.Create(outputPath);
MessagePackSerializer.Serialize(fs, results, MessagePackSerializerOptions.Standard);

Console.WriteLine($"Written to {outputPath}");
return 0;

// ─── RSP parsing ───

void LoadRemapRules(string rspFile, Dictionary<string, string> rules)
{
    bool inRemapSection = false;

    foreach (var rawLine in File.ReadLines(rspFile))
    {
        var line = rawLine.Trim();

        // Skip empty lines and comments
        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            continue;

        // Detect --remap section
        if (line.StartsWith("--"))
        {
            inRemapSection = line == "--remap";
            continue;
        }

        if (!inRemapSection) continue;

        // Parse "oldName=newName"
        var eqIndex = line.IndexOf('=');
        if (eqIndex > 0)
        {
            var oldName = line[..eqIndex].Trim();
            var newName = line[(eqIndex + 1)..].Trim();
            rules[oldName] = newName;
        }
    }
}

// ─── Name resolution ───

string ResolveTypeName(string originalName, Dictionary<string, string> remapRules)
{
    // For structs/enums/typedefs: apply remap rules
    if (remapRules.TryGetValue(originalName, out var remapped))
        return remapped;

    // Fallback: strip "ai" prefix
    if (originalName.StartsWith(PrefixToStrip, StringComparison.Ordinal) &&
        originalName.Length > PrefixToStrip.Length &&
        char.IsUpper(originalName[PrefixToStrip.Length]))
    {
        return originalName[PrefixToStrip.Length..];
    }

    return originalName;
}

string ResolveEnumMemberName(string originalName, Dictionary<string, string> remapRules)
{
    // For enum members: apply remap rules (they use remapped names)
    if (remapRules.TryGetValue(originalName, out var remapped))
        return remapped;

    return originalName;
}

string ResolveFunctionName(string originalName)
{
    // For functions: keep original name (CsWin32 uses entrypoint)
    return originalName;
}

// ─── XML processing ───

void ProcessXmlFile(string xmlFile, SortedDictionary<string, ApiDetails> results, Dictionary<string, string> remapRules)
{
    var doc = XDocument.Load(xmlFile);
    var root = doc.Root;
    if (root == null) return;

    foreach (var compoundDef in root.Descendants("compounddef"))
    {
        var kind = compoundDef.Attribute("kind")?.Value;
        var compoundName = compoundDef.Element("compoundname")?.Value;

        if (string.IsNullOrEmpty(compoundName)) continue;

        // Process struct/union definitions
        if (kind is "struct" or "union")
        {
            var resolvedName = ResolveTypeName(compoundName, remapRules);
            var structDetails = GetOrCreateApiDetails(results, resolvedName);

            var briefDesc = GetDescriptionText(compoundDef.Element("briefdescription"));
            var detailedDesc = GetDescriptionText(compoundDef.Element("detaileddescription"));

            if (!string.IsNullOrWhiteSpace(briefDesc))
                structDetails.Description = briefDesc;
            if (!string.IsNullOrWhiteSpace(detailedDesc))
                structDetails.Remarks = detailedDesc;

            // Process member fields
            foreach (var memberDef in compoundDef.Descendants("memberdef"))
            {
                var memberKind = memberDef.Attribute("kind")?.Value;
                if (memberKind != "variable") continue;

                var fieldName = memberDef.Element("name")?.Value;
                if (string.IsNullOrEmpty(fieldName)) continue;

                var fieldDesc = GetDescriptionText(memberDef.Element("briefdescription"));
                var fieldDetailedDesc = GetDescriptionText(memberDef.Element("detaileddescription"));
                var fullFieldDesc = CombineDescriptions(fieldDesc, fieldDetailedDesc);

                if (!string.IsNullOrWhiteSpace(fullFieldDesc))
                {
                    structDetails.Fields[fieldName] = fullFieldDesc;
                }
            }

            structDetails.HelpLink = new Uri(BaseHelpUrl);
        }

        // Process all member definitions within file/namespace compounds
        if (kind is "file" or "namespace")
        {
            foreach (var memberDef in compoundDef.Descendants("memberdef"))
            {
                ProcessMemberDef(memberDef, results, remapRules);
            }
        }
    }
}

void ProcessMemberDef(XElement memberDef, SortedDictionary<string, ApiDetails> results, Dictionary<string, string> remapRules)
{
    var kind = memberDef.Attribute("kind")?.Value;
    var name = memberDef.Element("name")?.Value;

    if (string.IsNullOrEmpty(name)) return;

    if (kind == "function")
    {
        var resolvedName = ResolveFunctionName(name);  // Keep original name for functions
        var details = GetOrCreateApiDetails(results, resolvedName);

        var briefDesc = GetDescriptionText(memberDef.Element("briefdescription"));
        var detailedDesc = GetDescriptionText(memberDef.Element("detaileddescription"));

        if (!string.IsNullOrWhiteSpace(briefDesc))
            details.Description = briefDesc;
        if (!string.IsNullOrWhiteSpace(detailedDesc))
            details.Remarks = detailedDesc;

        // Get parameters from detaileddescription
        var parameterList = memberDef.Element("detaileddescription")?
            .Descendants("parameterlist")
            .FirstOrDefault(pl => pl.Attribute("kind")?.Value == "param");

        if (parameterList != null)
        {
            foreach (var paramItem in parameterList.Elements("parameteritem"))
            {
                var paramName = paramItem.Element("parameternamelist")?
                    .Element("parametername")?.Value;
                var paramDesc = GetDescriptionText(paramItem.Element("parameterdescription"));

                if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrWhiteSpace(paramDesc))
                {
                    details.Parameters[paramName] = paramDesc;
                }
            }
        }

        // Get return value
        var returnDesc = memberDef.Element("detaileddescription")?
            .Descendants("simplesect")
            .FirstOrDefault(s => s.Attribute("kind")?.Value == "return");

        if (returnDesc != null)
        {
            var returnText = GetDescriptionText(returnDesc);
            if (!string.IsNullOrWhiteSpace(returnText))
            {
                details.ReturnValue = returnText;
            }
        }

        details.HelpLink = new Uri(BaseHelpUrl);
    }
    else if (kind == "enum")
    {
        var resolvedName = ResolveTypeName(name, remapRules);  // Enums use remapped names
        var details = GetOrCreateApiDetails(results, resolvedName);

        var briefDesc = GetDescriptionText(memberDef.Element("briefdescription"));
        var detailedDesc = GetDescriptionText(memberDef.Element("detaileddescription"));

        if (!string.IsNullOrWhiteSpace(briefDesc))
            details.Description = briefDesc;
        if (!string.IsNullOrWhiteSpace(detailedDesc))
            details.Remarks = detailedDesc;

        // Process enum values as fields
        foreach (var enumValue in memberDef.Elements("enumvalue"))
        {
            var valueName = enumValue.Element("name")?.Value;
            if (string.IsNullOrEmpty(valueName)) continue;

            var resolvedValueName = ResolveEnumMemberName(valueName, remapRules);  // Enum members use remapped names
            var valueDesc = GetDescriptionText(enumValue.Element("briefdescription"));
            var valueDetailedDesc = GetDescriptionText(enumValue.Element("detaileddescription"));
            var fullValueDesc = CombineDescriptions(valueDesc, valueDetailedDesc);

            if (!string.IsNullOrWhiteSpace(fullValueDesc))
            {
                details.Fields[resolvedValueName] = fullValueDesc;
            }
        }

        details.HelpLink = new Uri(BaseHelpUrl);
    }
    else if (kind == "typedef")
    {
        var resolvedName = ResolveTypeName(name, remapRules);  // Typedefs use remapped names
        var details = GetOrCreateApiDetails(results, resolvedName);

        var briefDesc = GetDescriptionText(memberDef.Element("briefdescription"));
        var detailedDesc = GetDescriptionText(memberDef.Element("detaileddescription"));

        if (!string.IsNullOrWhiteSpace(briefDesc))
            details.Description = briefDesc;
        if (!string.IsNullOrWhiteSpace(detailedDesc))
            details.Remarks = detailedDesc;

        details.HelpLink = new Uri(BaseHelpUrl);
    }
}

// ─── Helpers ───

ApiDetails GetOrCreateApiDetails(SortedDictionary<string, ApiDetails> results, string name)
{
    if (!results.TryGetValue(name, out var details))
    {
        details = new ApiDetails();
        results[name] = details;
    }
    return details;
}

string GetDescriptionText(XElement? element)
{
    if (element == null) return string.Empty;

    var sb = new StringBuilder();
    ExtractText(element, sb);
    return CleanDescription(sb.ToString());
}

void ExtractText(XElement element, StringBuilder sb)
{
    foreach (var node in element.Nodes())
    {
        if (node is XText text)
        {
            sb.Append(text.Value);
        }
        else if (node is XElement child)
        {
            switch (child.Name.LocalName)
            {
                case "para":
                    ExtractText(child, sb);
                    sb.Append(' ');
                    break;
                case "ref":
                    sb.Append(child.Value);
                    break;
                case "computeroutput":
                case "emphasis":
                case "bold":
                    sb.Append(child.Value);
                    break;
                case "itemizedlist":
                case "orderedlist":
                    foreach (var item in child.Elements("listitem"))
                    {
                        sb.Append("\n- ");
                        ExtractText(item, sb);
                    }
                    break;
                case "simplesect":
                    var kindAttr = child.Attribute("kind")?.Value;
                    if (kindAttr == "note")
                    {
                        sb.Append("\nNote: ");
                        ExtractText(child, sb);
                    }
                    else if (kindAttr == "see")
                    {
                        sb.Append("\nSee: ");
                        ExtractText(child, sb);
                    }
                    else if (kindAttr is not "return" and not "param")
                    {
                        ExtractText(child, sb);
                    }
                    break;
                case "parameterlist":
                    // Skip parameter lists in description text
                    break;
                default:
                    ExtractText(child, sb);
                    break;
            }
        }
    }
}

string CleanDescription(string text)
{
    text = Regex.Replace(text, @"[ \t]+", " ");
    text = Regex.Replace(text, @"\n ", "\n");
    text = text.Trim();
    return text;
}

string CombineDescriptions(string? brief, string? detailed)
{
    if (string.IsNullOrWhiteSpace(brief) && string.IsNullOrWhiteSpace(detailed))
        return string.Empty;
    if (string.IsNullOrWhiteSpace(brief))
        return detailed ?? string.Empty;
    if (string.IsNullOrWhiteSpace(detailed))
        return brief;
    if (detailed.StartsWith(brief, StringComparison.OrdinalIgnoreCase))
        return detailed;

    return $"{brief} {detailed}";
}
