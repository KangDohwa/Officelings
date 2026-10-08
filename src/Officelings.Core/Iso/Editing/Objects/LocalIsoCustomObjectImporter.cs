using System.Collections.Immutable;

namespace Officelings.Core.Iso.Editing;

/// <summary>Bounded local furniture importer and catalog service; spec §§7.1–7.4.</summary>
public sealed class LocalIsoCustomObjectImporter : IsoCustomObjectImporter
{
    private static readonly object transactions = new();
    private readonly CancellationToken cancellation;
    /// <summary>Spec §§7.3, 9: callers may bind cancellation to one off-thread import/validation service.</summary>
    public LocalIsoCustomObjectImporter(CancellationToken cancellation = default) => this.cancellation = cancellation;
    /// <summary>Spec §7.2: validates captured bytes without writing.</summary>
    public ImmutableArray<IsoValidationIssue> Validate(string manifestPath)
    {
        var issues = new ObjectIssues(); ObjectSource.Read(manifestPath, issues, cancellation: cancellation); return issues.All;
    }
    /// <summary>Spec §7.2: metadata, identity and thumbnail all come from one validated capture.</summary>
    public IsoCustomValidationPreview ValidatePreview(string manifestPath, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, cancellationToken);
        var issues = new ObjectIssues(); var source = ObjectSource.Read(manifestPath, issues, cancellation: linked.Token);
        return new(source, issues.All);
    }
    /// <summary>Spec §7.3: capture and merge without publishing files or catalog.</summary>
    public IsoCustomObjectPlan PrepareImport(IsoCustomImportRequest request, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, cancellationToken);
        var issues = new ObjectIssues();
        var preview = request.VerifiedPreview ?? ValidatePreview(request.ManifestPath, linked.Token);
        issues.AddRange(preview.Issues); var source = preview.Source;
        if (linked.IsCancellationRequested) issues.Add("O009", error: new OperationCanceledException());
        if (source == null || issues.HasErrors) return new(null, request.Catalog, issues.All, request.Catalog);
        try { ObjectFiles.Output(ObjectCatalog.Root(request.Paths)); }
        catch (Exception e) when (ObjectSource.Expected(e)) { issues.Add("O003"); }
        var existing = request.Catalog.Objects.IsDefault ? null : request.Catalog.Objects.FirstOrDefault(o => o.AssetId == source.Manifest.Id);
        if (existing != null && existing.ContentHash != source.Hash) issues.Add("O002", source.Manifest.Id);
        if (request.BuiltInPack.Manifest.Furniture?.Any(f => f.Id.StartsWith("user/", StringComparison.Ordinal)) == true) issues.Add("O010");
        var sources = Capture(request.Paths, request.Catalog, issues);
        if (existing == null) sources.Add(source);
        var merged = Preflight(request.BuiltInPack, sources, issues);
        var catalog = issues.HasErrors || existing != null ? request.Catalog :
            new IsoCustomObjectCatalog(request.Catalog.Objects.Add(ObjectCatalog.Entry(request.Paths, source)));
        return new(merged, catalog, issues.All, request.Catalog, import: request, source: source);
    }
    /// <summary>Spec §7.4: reference guard and complete replacement-pack preflight without mutation.</summary>
    public IsoCustomObjectPlan PrepareRemove(IsoCustomRemoveRequest request)
    {
        var issues = new ObjectIssues();
        if (request.Catalog.Version != 1 || request.Catalog.Objects.IsDefault || request.Catalog.Objects.Length > 64) issues.Add("O010");
        if (request.ReferencedAssetIds.IsDefault || request.ReferencedAssetIds.Contains(request.AssetId, StringComparer.Ordinal) ||
            !request.AssetId.StartsWith("user/", StringComparison.Ordinal)) issues.Add("O011", request.AssetId);
        var entry = request.Catalog.Objects.IsDefault ? null : request.Catalog.Objects.FirstOrDefault(o => o.AssetId == request.AssetId);
        if (entry == null) issues.Add("O002", request.AssetId);
        if (issues.HasErrors) return new(null, request.Catalog, issues.All, request.Catalog);
        try
        {
            if (!ObjectFiles.CleanupSafe(ObjectCatalog.DirectoryFor(request.Paths, entry!.ContentHash))) issues.Add("O011", request.AssetId);
        }
        catch (Exception e) when (ObjectSource.Expected(e)) { issues.Add("O011", request.AssetId); }
        var catalog = new IsoCustomObjectCatalog(request.Catalog.Objects.Remove(entry!));
        var pack = request.BuiltInPack == null ? null : Preflight(request.BuiltInPack, Capture(request.Paths, catalog, issues), issues);
        return new(pack, catalog, issues.All, request.Catalog, remove: request);
    }
    /// <summary>Spec §§7.3–7.4: catalog publication after the caller's synchronous consumer preflight.</summary>
    public IsoCustomObjectResult Commit(IsoCustomObjectPlan plan, CancellationToken cancellationToken = default)
    {
        lock (transactions)
        {
            var issues = new ObjectIssues(); issues.AddRange(plan.Issues);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, cancellationToken);
            if (linked.IsCancellationRequested) issues.Add("O009", error: new OperationCanceledException());
            if (issues.HasErrors) return new(false, plan.Original, issues.All);
            try
            {
                var paths = plan.Import?.Paths ?? plan.Remove?.Paths ?? throw new InvalidDataException();
                var root = ObjectFiles.Output(ObjectCatalog.Root(paths));
                FileBoundary.CreateDirectory(root);
                using var boundary = FileBoundary.LockWriteParent(Path.Combine(root, ".transaction.lock"));
                using var writer = ObjectFiles.LockWriter(root);
                ObjectCatalog.VerifyCurrent(paths, plan.Original);
                linked.Token.ThrowIfCancellationRequested();
                if (plan.Import is {} import && plan.Source is {} source)
                {
                    var existing = plan.Original.Objects.FirstOrDefault(o => o.AssetId == source.Manifest.Id);
                    if (existing != null) return new(true, plan.Original, issues.All, existing);
                    return ObjectPublisher.Install(import, source, issues, linked.Token);
                }
                var remove = plan.Remove!;
                var entry = plan.Original.Objects.Single(o => o.AssetId == remove.AssetId);
                var directory = ObjectCatalog.DirectoryFor(paths, entry.ContentHash);
                if (!ObjectFiles.CleanupSafe(directory)) { issues.Add("O011", remove.AssetId); return new(false, plan.Original, issues.All); }
                linked.Token.ThrowIfCancellationRequested();
                ObjectCatalog.Publish(paths, plan.Catalog);
                try { if (FileBoundary.Exists(directory, directory: true)) ObjectFiles.DeleteDirectory(directory); }
                catch (Exception e) when (ObjectSource.Expected(e)) { issues.Add("O009", severity: IsoIssueSeverity.Warning, error: e); }
                return new(true, plan.Catalog, issues.All);
            }
            catch (Exception e) when (ObjectSource.Expected(e) || e is OperationCanceledException) { issues.Add("O009", error: e); }
            return new(false, plan.Original, issues.All);
        }
    }
    /// <summary>Spec §7.3: content-addressed atomic install after complete pack preflight.</summary>
    public IsoCustomObjectResult Import(IsoCustomImportRequest request) => Commit(PrepareImport(request, cancellation), cancellation);
    /// <summary>Spec §7.4: startup is read-only and excludes each complete invalid entry.</summary>
    public IsoCustomObjectResult LoadCatalog(IsoEditorPaths paths)
    {
        var issues = new ObjectIssues(); var catalog = ObjectCatalog.Read(paths, issues);
        return new(!issues.HasErrors, catalog, issues.All);
    }
    /// <summary>Spec §7.4: drop an unreferenced catalog entry before safe flat-directory cleanup.</summary>
    public IsoCustomObjectResult Remove(IsoCustomRemoveRequest request) => Commit(PrepareRemove(request), cancellation);
    /// <summary>Spec §§3.1, 7.4: clone resolved built-in data and add the virtual computer and complete validated objects.</summary>
    public IsoPackMergeResult Merge(IsoPack builtInPack, IsoCustomObjectCatalog catalog)
    {
        var issues = new ObjectIssues();
        if (builtInPack.Manifest.Furniture?.Any(f => f.Id.StartsWith("user/", StringComparison.Ordinal)) == true) issues.Add("O010");
        var sources = new List<ObjectSource>();
        if (catalog.Version != 1 || catalog.Objects.IsDefault || catalog.Objects.Length > 64) issues.Add("O010");
        else foreach (var entry in catalog.Objects)
        {
            // Catalog entries from outside LoadCatalog are still untrusted: rederive the hash folder.
            try
            {
                var directory = ObjectFiles.Check(entry.InstalledDirectory);
                if (Path.GetFileName(directory) != entry.ContentHash || Path.GetFileName(Path.GetDirectoryName(directory)) != "objects" ||
                    Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(directory))) != "iso-objects") throw new InvalidDataException();
                var paths = new IsoEditorPaths(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(directory)))!);
                var local = new ObjectIssues(); var source = ObjectCatalog.Load(paths, entry.AssetId, entry.ContentHash, local);
                issues.AddRange(local.All);
                if (source != null) sources.Add(source);
                if (!CaptureFits(sources)) { issues.Add("O010"); break; }
            }
            catch (Exception e) when (ObjectSource.Expected(e)) { issues.Add("O003"); }
        }
        var pack = Preflight(builtInPack, sources, issues);
        return new(!issues.HasErrors && pack != null, issues.HasErrors ? null : pack, issues.All);
    }
    private static List<ObjectSource> Capture(IsoEditorPaths paths, IsoCustomObjectCatalog catalog, ObjectIssues issues)
    {
        var result = new List<ObjectSource>();
        if (catalog.Version != 1 || catalog.Objects.IsDefault || catalog.Objects.Length > 64) { issues.Add("O010"); return result; }
        foreach (var entry in catalog.Objects)
        {
            var local = new ObjectIssues(); var source = ObjectCatalog.Load(paths, entry.AssetId, entry.ContentHash, local);
            issues.AddRange(local.All); if (source != null) result.Add(source);
            if (!CaptureFits(result)) { issues.Add("O010"); break; }
        }
        return result;
    }
    private static bool CaptureFits(List<ObjectSource> sources) =>
        sources.Sum(s => s.InstalledPngBytes) <= 64 * 1024 * 1024 &&
        sources.Sum(s => s.Pixels.Values.Sum(p => (long)p.Rgba.Length)) <= 128 * 1024 * 1024;
    private static IsoPack? Preflight(IsoPack builtIn, List<ObjectSource> sources, ObjectIssues issues)
    {
        if (issues.HasErrors) return null;
        try
        {
            if (!IsoPack.CustomLimits(builtIn, sources)) { issues.Add("O010"); return null; }
            if (sources.Any(s => s.InstalledManifest().Length > ObjectFiles.JsonLimit)) { issues.Add("O010"); return null; }
            var pack = builtIn.WithCustomObjects(sources);
            if (!pack.CustomJsonFits()) { issues.Add("O010"); return null; }
            return pack;
        }
        catch (Exception e) when (ObjectSource.Expected(e) || e is InvalidOperationException) { issues.Add("O010"); return null; }
    }
}
