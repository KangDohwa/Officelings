using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>Spec §§7.2–7.3: immutable verified preview retaining the exact validated source internally.</summary>
public sealed class IsoCustomValidationPreview
{
    /// <summary>Spec §7.2: validation diagnostics; an error makes this preview unusable for import.</summary>
    public ImmutableArray<IsoValidationIssue> Issues { get; }
    /// <summary>Spec §7.2: verified user-facing display name.</summary>
    public string? DisplayName => Source?.Manifest.DisplayName;
    /// <summary>Spec §7.2: number of authored variants.</summary>
    public int VariantCount => Source?.Manifest.Furniture[0].Variants.Length ?? 0;
    /// <summary>Spec §7.3: identity of captured manifest and PNG bytes.</summary>
    public string? SourceIdentityHash => Source?.Hash;
    /// <summary>Spec §7.2: immutable PNG thumbnail generated from validated captured pixels.</summary>
    public ImmutableArray<byte> ThumbnailPng { get; }
    internal ObjectSource? Source { get; }
    internal IsoCustomValidationPreview(ObjectSource? source, ImmutableArray<IsoValidationIssue> issues)
    {
        Issues = issues;
        Source = issues.Any(i => i.Severity == IsoIssueSeverity.Error) ? null : source;
        ThumbnailPng = Source == null ? [] : ObjectThumbnail.Create(Source).ToImmutableArray();
    }
}

/// <summary>Spec §§7.3–7.4: opaque staged transaction; preparation performs no writes.</summary>
public sealed class IsoCustomObjectPlan
{
    /// <summary>Spec §7.4: complete proposed pack for every consumer's preflight.</summary>
    public IsoPack? Pack { get; }
    /// <summary>Spec §7.4: immutable proposed catalog.</summary>
    public IsoCustomObjectCatalog Catalog { get; }
    /// <summary>Spec §7: preparation diagnostics; errors block Commit.</summary>
    public ImmutableArray<IsoValidationIssue> Issues { get; }
    internal IsoCustomObjectCatalog Original { get; }
    internal IsoCustomImportRequest? Import { get; }
    internal IsoCustomRemoveRequest? Remove { get; }
    internal ObjectSource? Source { get; }
    internal IsoCustomObjectPlan(IsoPack? pack, IsoCustomObjectCatalog catalog, ImmutableArray<IsoValidationIssue> issues,
        IsoCustomObjectCatalog original, IsoCustomImportRequest? import = null, IsoCustomRemoveRequest? remove = null, ObjectSource? source = null)
    { Pack = pack; Catalog = catalog; Issues = issues; Original = original; Import = import; Remove = remove; Source = source; }
}
