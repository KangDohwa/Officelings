namespace Officelings.Core.Iso.Editing;

internal static class ObjectPublisher
{
    // The caller retains the writer lock through failure cleanup, so another publisher
    // cannot reference a newly installed directory while this transaction removes it.
    internal static IsoCustomObjectResult Install(IsoCustomImportRequest request, ObjectSource source,
        ObjectIssues issues, CancellationToken cancellation)
    {
        string? stage = null, published = null;
        try
        {
            ObjectCatalog.VerifyCurrent(request.Paths, request.Catalog);
            var root = ObjectCatalog.Root(request.Paths);
            FileBoundary.CreateDirectory(ObjectFiles.Under(root, "objects"));
            var destination = ObjectCatalog.DirectoryFor(request.Paths, source.Hash);
            if (FileBoundary.Exists(destination, directory: true))
            {
                var local = new ObjectIssues(); ObjectCatalog.Load(request.Paths, source.Manifest.Id, source.Hash, local);
                issues.AddRange(local.All); if (local.HasErrors) return new(false, request.Catalog, issues.All);
            }
            else
            {
                stage = ObjectFiles.Under(root, "stage-" + Guid.NewGuid().ToString("N"));
                Stage(stage, request.Paths, source, cancellation);
                cancellation.ThrowIfCancellationRequested();
                ObjectFiles.MoveDirectory(stage, destination); stage = null; published = destination;
            }
            var entry = ObjectCatalog.Entry(request.Paths, source);
            var catalog = new IsoCustomObjectCatalog(request.Catalog.Objects.Add(entry));
            using var directoryLock = FileBoundary.LockAncestors(Path.Combine(destination, ".verify"));
            var final = new ObjectIssues(); ObjectCatalog.Load(request.Paths, source.Manifest.Id, source.Hash, final);
            if (final.HasErrors) throw new InvalidDataException("Published directory validation failed.");
            cancellation.ThrowIfCancellationRequested();
            ObjectCatalog.Publish(request.Paths, catalog); published = null;
            return new(true, catalog, issues.All, entry);
        }
        catch (Exception e) when (ObjectSource.Expected(e) || e is OperationCanceledException) { issues.Add("O009", error: e); }
        finally
        {
            foreach (var directory in new[] { stage, published }.OfType<string>())
                try { if (FileBoundary.Exists(directory, directory: true)) ObjectFiles.DeleteDirectory(directory); }
                catch (Exception e) when (ObjectSource.Expected(e)) { issues.Add("O009", severity: IsoIssueSeverity.Warning, error: e); }
        }
        return new(false, request.Catalog, issues.All);
    }
    private static void Stage(string directory, IsoEditorPaths paths, ObjectSource source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); FileBoundary.CreateDirectory(directory);
        ObjectFiles.Write(ObjectFiles.Under(directory, "source.iso.json"), source.Bytes);
        foreach (var png in source.Images.Values.DistinctBy(ObjectSource.SheetFile))
        { cancellation.ThrowIfCancellationRequested(); ObjectFiles.Write(ObjectFiles.Under(directory, ObjectSource.SheetFile(png)), png); }
        ObjectFiles.Write(ObjectFiles.Under(directory, "object.iso.json"), source.InstalledManifest());
        ObjectFiles.Write(ObjectFiles.Under(directory, "thumbnail.png"), ObjectThumbnail.Create(source));
        var checkedStage = new ObjectIssues(); ObjectCatalog.Load(paths, source.Manifest.Id, source.Hash, checkedStage, directory);
        if (checkedStage.HasErrors) throw new InvalidDataException("Stage validation failed.");
    }
}
