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
        IsoCustomObjectImporter importer) => throw new NotImplementedException();
}
