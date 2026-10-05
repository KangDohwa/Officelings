using System.Collections.Immutable;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("IsoCustomObject requires Windows: local file boundary protection is Windows-only.");
    return 2;
}
// Local-only CLI; no profile lookup, implicit catalog root, pack builder or network input.
if (args.Length is < 2 or > 3 || args[0] is not ("validate" or "import" or "catalog") ||
    args[0] == "validate" && args.Length != 2 || args[0] == "import" && args.Length != 3 || args[0] == "catalog" && args.Length != 2)
{
    Console.Error.WriteLine("Usage: validate <local.iso.json> | import <local.iso.json> <synthetic-or-app-root> | catalog <root>");
    return 2;
}
var importer = new LocalIsoCustomObjectImporter();
ImmutableArray<IsoValidationIssue> issues;
if (args[0] == "validate") issues = importer.Validate(args[1]);
else
{
    var paths = new IsoEditorPaths(args[^1]); var loaded = importer.LoadCatalog(paths); issues = loaded.Issues;
    if (loaded.Succeeded && args[0] == "import")
    {
        var result = importer.Import(new(args[1], paths, loaded.Catalog, IsoPack.LoadDefault())); issues = result.Issues;
        if (result.Succeeded) Console.WriteLine(result.Object!.AssetId + " " + result.Object.ContentHash);
    }
    else if (loaded.Succeeded) foreach (var item in loaded.Catalog.Objects) Console.WriteLine(item.AssetId + " " + item.ContentHash);
}
foreach (var issue in issues) Console.WriteLine($"{issue.RuleId} {issue.Severity}: {issue.Message}");
return issues.Any(i => i.Severity == IsoIssueSeverity.Error) ? 1 : 0;
