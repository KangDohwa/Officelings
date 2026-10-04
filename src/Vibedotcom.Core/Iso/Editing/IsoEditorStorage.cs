using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>Explicit storage root; spec §2.2. Tests inject a synthetic worktree directory.</summary>
public sealed record IsoEditorPaths(string RootDirectory);
/// <summary>Spec §2.2: complete active pack/layout or bundled fallback, with all load diagnostics.</summary>
public sealed record IsoEditorLoadResult(IsoPack Pack, IsoLayout Layout, IsoCustomObjectCatalog Catalog,
    ImmutableArray<IsoValidationIssue> Issues, bool UsedUserLayout);
/// <summary>Local-only startup resolver; spec §§2.2, 7.4. Never writes or starts a watcher on Load.</summary>
public static class IsoEditorStorage
{
    /// <summary>Spec §2.2: load catalog, merge, then validate iso-layout.json; keep invalid source untouched.</summary>
    public static IsoEditorLoadResult Load(IsoPack bundledPack, IsoEditorPaths paths,
        IsoCustomObjectImporter importer)
    {
        var loaded = importer.LoadCatalog(paths);
        var issues = loaded.Issues.ToList();
        var merged = importer.Merge(bundledPack, loaded.Catalog); issues.AddRange(merged.Issues);
        var pack = merged.Succeeded && merged.Pack != null ? merged.Pack : bundledPack;
        var fallback = IsoEditorDocument.Default(bundledPack);
        if (!merged.Succeeded) return new(pack, fallback, loaded.Catalog, IsoEditorValidation.Sort(issues), false);
        try
        {
            var file = FileBoundary.UnderRoot(paths.RootDirectory, "iso-layout.json");
            if (!FileBoundary.Exists(file)) return new(pack, fallback, loaded.Catalog, IsoEditorValidation.Sort(issues), false);
            FileBoundary.Check(file);
            if (new FileInfo(file).Length > IsoEditorDocument.MaxBytes)
                issues.Add(IsoEditorValidation.Issue("L017", "Iso editor layout limits exceeded."));
            else
            {
                var bytes = FileBoundary.ReadBytes(file, IsoEditorDocument.MaxBytes);
                using var stream = new MemoryStream(bytes, false);
                var candidate = IsoEditorDocument.Own(IsoLayoutLoader.ReadDocument(stream), pack);
                var validation = IsoEditorValidation.Validate(candidate, pack); issues.AddRange(validation);
                if (!validation.Any(i => i.Severity == IsoIssueSeverity.Error))
                    return new(pack, candidate, loaded.Catalog, IsoEditorValidation.Sort(issues), true);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { issues.Add(IsoEditorValidation.Issue("L001", "Unsupported iso editor schema or capability.")); }
        issues.Add(IsoEditorValidation.Issue("P002", "Could not activate the saved iso layout; using the bundled office."));
        return new(pack, fallback, loaded.Catalog, IsoEditorValidation.Sort(issues), false);
    }
}
