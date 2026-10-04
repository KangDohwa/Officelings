using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>Furniture-v3 authoring shape plus user metadata; spec §7.1. JSON uses StateJson camelCase.</summary>
/// <remarks>Sheet/part anchors and nested arrays are deep-copied by the importer. No characters, pets,
/// walls, aliases, scripts or work-seat rigs are accepted in this version.</remarks>
public sealed record IsoCustomObjectManifest(int Version, string Projection, string Id, string DisplayName,
    ImmutableArray<IsoSheet> Sheets, ImmutableArray<IsoCustomFurnitureAsset> Furniture,
    ImmutableArray<string> Palette, int CustomObjectVersion = 1);

/// <summary>Spec §7.1: exactly one asset whose ID equals manifest Id.</summary>
public sealed record IsoCustomFurnitureAsset(string Id, string Kind, ImmutableArray<IsoCustomFurnitureVariant> Variants);

/// <summary>Spec §7.1: source height maps to runtime HeightPx; rotations are authored, never synthesized.</summary>
public sealed record IsoCustomFurnitureVariant(int Rotation, string Orientation, string State,
    int FootprintW, int FootprintH, ImmutableArray<Tile> BlockedCells, double Height,
    ImmutableArray<IsoPart> Parts, int Layer = 1, IsoPart? Shadow = null,
    ImmutableArray<IsoInteraction> Points = default);

/// <summary>Spec §7.3: validated content-addressed object. InstalledDirectory is trusted catalog output.</summary>
public sealed record IsoCustomObjectEntry(string AssetId, string ContentHash, string InstalledDirectory,
    IsoCustomObjectManifest Manifest, string ThumbnailFile);

/// <summary>Spec §7.4: immutable catalog; no dependency on editor history or implementation internals.</summary>
public sealed record IsoCustomObjectCatalog(ImmutableArray<IsoCustomObjectEntry> Objects, int Version = 1);

/// <summary>Spec §7.3: explicit local input/root/catalog and original built-in pack for merge preflight.</summary>
public sealed record IsoCustomImportRequest(string ManifestPath, IsoEditorPaths Paths,
    IsoCustomObjectCatalog Catalog, IsoPack BuiltInPack);
/// <summary>Spec §7.4: caller supplies union of active, saved, backup, Undo and Redo asset references.</summary>
public sealed record IsoCustomRemoveRequest(string AssetId, IsoEditorPaths Paths, IsoCustomObjectCatalog Catalog,
    ImmutableArray<string> ReferencedAssetIds);
/// <summary>Spec §7: failure retains original catalog; warnings accompany a successful import.</summary>
public sealed record IsoCustomObjectResult(bool Succeeded, IsoCustomObjectCatalog Catalog,
    ImmutableArray<IsoValidationIssue> Issues, IsoCustomObjectEntry? Object = null);
/// <summary>Spec §7.4: merge never drops built-ins or unresolved entries to meet a limit.</summary>
public sealed record IsoPackMergeResult(bool Succeeded, IsoPack? Pack, ImmutableArray<IsoValidationIssue> Issues);

/// <summary>Objects-lane service boundary; spec §7. Every expected input/I/O failure is a listed issue.</summary>
public interface IsoCustomObjectImporter
{
    /// <summary>Spec §§7.1–7.2: pure bounded validation, with no installation or catalog writes.</summary>
    ImmutableArray<IsoValidationIssue> Validate(string manifestPath);
    /// <summary>Spec §7.3: stage PNG bytes unchanged, publish by content hash, then atomically update catalog.</summary>
    IsoCustomObjectResult Import(IsoCustomImportRequest request);
    /// <summary>Spec §7.4: validates installed objects; failed entries are listed and excluded from catalog.</summary>
    IsoCustomObjectResult LoadCatalog(IsoEditorPaths paths);
    /// <summary>Spec §7.4: rejects referenced objects; removes catalog entry atomically before file cleanup.</summary>
    IsoCustomObjectResult Remove(IsoCustomRemoveRequest request);
    /// <summary>Spec §7.4: retain built-in caches/rigs; add validated furniture and the built-in computer adapter.</summary>
    IsoPackMergeResult Merge(IsoPack builtInPack, IsoCustomObjectCatalog catalog);
}
