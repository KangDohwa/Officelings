using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Officelings.Core.Iso.Editing;

internal sealed class ObjectIssues
{
    private readonly List<IsoValidationIssue> items = [];
    internal bool HasErrors => items.Any(i => i.Severity == IsoIssueSeverity.Error);
    internal ImmutableArray<IsoValidationIssue> All => items.GroupBy(i => (i.RuleId, i.Severity, i.Message, Targets: string.Join("\0", i.TargetIds)))
        .Select(g => g.First()).OrderBy(i => i.RuleId, StringComparer.Ordinal)
        .ThenBy(i => string.Join("\0", i.TargetIds), StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal).ToImmutableArray();
    internal void Add(string rule, string id = "", IsoIssueSeverity severity = IsoIssueSeverity.Error, Exception? error = null)
    {
        // Only bounded schema identifiers enter diagnostics; arbitrary source text and paths stay private.
        id = id.Length <= 160 && Regex.IsMatch(id, "\\Auser/[a-z0-9][a-z0-9-]{0,47}(?:/[a-z0-9/_-]{1,96})?\\z") ? id : "[hidden]";
        var message = rule switch
        {
            "O001" => "Unsupported custom object manifest.",
            "O002" => $"Invalid or conflicting custom object ID '{id}'.",
            "O003" => "Custom object paths must be regular local files within the import directory.",
            "O004" => "Custom object size, dimension or count limits exceeded.",
            "O005" => $"Invalid or unsafe custom PNG sheet '{id}'.",
            "O006" => $"Invalid custom furniture geometry for '{id}'.",
            "O007" => $"Invalid custom part, anchor or depth metadata for '{id}'.",
            "O008" when severity == IsoIssueSeverity.Warning => "Custom colors differ from the built-in frozen palette.",
            "O008" => "Custom pixels are outside the declared palette.",
            "O009" => "Could not install the custom object: " + PrivacyModes.Diagnostic(error ?? new IOException(), PrivacyMode.Full),
            "O010" => "The merged iso pack would exceed its limits.",
            "O011" => $"Custom object '{id}' is still referenced by a layout or editor history.",
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
        if (!items.Any(i => i.RuleId == rule && i.Message == message && i.Severity == severity))
            items.Add(new(rule, severity, message, id == "[hidden]" ? [] : [id]));
    }
    internal void AddRange(IEnumerable<IsoValidationIssue> issues) => items.AddRange(issues);
}
