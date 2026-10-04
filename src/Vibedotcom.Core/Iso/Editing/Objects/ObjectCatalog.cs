using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vibedotcom.Core.Iso.Editing;

internal static class ObjectCatalog
{
    internal sealed record Reference(string AssetId, string ContentHash);
    internal sealed record Document(int Version, ImmutableArray<Reference> Objects);
    internal static string Root(IsoEditorPaths paths) => ObjectFiles.Under(ObjectFiles.Check(paths.RootDirectory), "iso-objects");
    internal static string DirectoryFor(IsoEditorPaths paths, string hash)
    {
        if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new InvalidDataException("Invalid hash.");
        return ObjectFiles.Under(Root(paths), "objects/" + hash);
    }
    internal static ObjectSource? Load(IsoEditorPaths paths, string id, string hash, ObjectIssues issues, string? directory = null)
    {
        try
        {
            directory ??= DirectoryFor(paths, hash);
            var sourceBytes = ObjectFiles.Read(ObjectFiles.Under(directory, "source.iso.json"), ObjectFiles.JsonLimit);
            var installedBytes = ObjectFiles.Read(ObjectFiles.Under(directory, "object.iso.json"), ObjectFiles.JsonLimit);
            using var installed = ObjectSchema.Parse(installedBytes);
            ObjectSchema.Validate(installed.RootElement);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var source = ObjectSource.Read("source.iso.json", issues, s =>
            {
                // The installed manifest, never the source path, identifies hashed sheet files.
                var sheet = installed.RootElement.GetProperty("sheets").EnumerateArray().Single(v => v.GetProperty("id").GetString() == s.Id);
                var file = sheet.GetProperty("file").GetString()!;
                if (!Regex.IsMatch(file, "^sheet-[a-f0-9]{64}\\.png$")) throw new InvalidDataException("Installed sheet name.");
                if (!files.TryGetValue(file, out var png)) files[file] = png = ObjectFiles.Read(ObjectFiles.Under(directory, file), ObjectFiles.PngLimit);
                if (file != ObjectSource.SheetFile(png)) throw new InvalidDataException("Installed sheet hash.");
                return png;
            }, sourceBytes);
            if (source == null || issues.HasErrors) return null;
            if (source.Hash != hash || source.Manifest.Id != id) { issues.Add("O009"); return null; }
            if (!JsonNode.DeepEquals(JsonNode.Parse(installedBytes), JsonNode.Parse(source.InstalledManifest())))
            { issues.Add("O009"); return null; }
            var thumbnail = ObjectFiles.Read(ObjectFiles.Under(directory, "thumbnail.png"), ObjectFiles.PngLimit);
            if (!thumbnail.AsSpan().SequenceEqual(ObjectThumbnail.Create(source))) { issues.Add("O009"); return null; }
            return source;
        }
        catch (ObjectFieldException e) { issues.Add(e.Rule); return null; }
        catch (Exception e) when (ObjectSource.Expected(e) || e is JsonException or InvalidOperationException)
        { issues.Add(e is InvalidDataException && !issues.HasErrors ? "O003" : "O009", error: e); return null; }
    }
    internal static IsoCustomObjectEntry Entry(IsoEditorPaths paths, ObjectSource source) =>
        new(source.Manifest.Id, source.Hash, DirectoryFor(paths, source.Hash), source.Manifest,
            ObjectFiles.Under(DirectoryFor(paths, source.Hash), "thumbnail.png"));
    internal static IsoCustomObjectCatalog Read(IsoEditorPaths paths, ObjectIssues issues)
    {
        var objects = ImmutableArray.CreateBuilder<IsoCustomObjectEntry>();
        long installedBytes = 0;
        try
        {
            var file = ObjectFiles.Under(Root(paths), "catalog.json");
            if (!FileBoundary.Exists(file)) return new([]);
            using var json = ObjectSchema.Parse(ObjectFiles.Read(file, ObjectFiles.JsonLimit));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("version" or "objects")) ||
                root.GetProperty("version").ValueKind != JsonValueKind.Number || !root.GetProperty("version").TryGetInt32(out var version) ||
                version != 1 || root.GetProperty("objects").ValueKind != JsonValueKind.Array)
                throw new JsonException();
            if (root.GetProperty("objects").GetArrayLength() > 64) { issues.Add("O010"); return new([]); }
            var ids = new HashSet<string>(StringComparer.Ordinal); var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("objects").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 ||
                    item.EnumerateObject().Any(p => p.Name is not ("assetId" or "contentHash"))) throw new JsonException();
                var id = item.GetProperty("assetId").GetString()!; var hash = item.GetProperty("contentHash").GetString()!;
                var local = new ObjectIssues();
                if (!ids.Add(id) || !hashes.Add(hash)) local.Add("O002", id);
                var source = local.HasErrors ? null : Load(paths, id, hash, local);
                issues.AddRange(local.All);
                if (source != null && !local.HasErrors)
                {
                    installedBytes += source.InstalledPngBytes;
                    if (installedBytes > 64 * 1024 * 1024) { issues.Add("O010"); break; }
                    objects.Add(Entry(paths, source));
                }
            }
        }
        catch (Exception e) when (ObjectSource.Expected(e) || e is JsonException or InvalidOperationException or KeyNotFoundException)
        { issues.Add("O009", error: e); }
        return new(objects.ToImmutable());
    }
    internal static void Publish(IsoEditorPaths paths, IsoCustomObjectCatalog catalog)
    {
        var document = new Document(1, catalog.Objects.OrderBy(o => o.AssetId, StringComparer.Ordinal).Select(o => new Reference(o.AssetId, o.ContentHash)).ToImmutableArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, IsoPack.CustomJson);
        if (bytes.Length > ObjectFiles.JsonLimit) throw new ObjectSizeException();
        var file = ObjectFiles.Output(ObjectFiles.Under(Root(paths), "catalog.json"));
        var backup = ObjectFiles.Output(file + ".bak");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var parent = FileBoundary.LockWriteParent(file);
        try
        {
            ObjectFiles.Write(temporary, bytes);
            if (FileBoundary.Exists(file)) File.Replace(temporary, file, backup);
            else File.Move(temporary, file);
        }
        finally { if (FileBoundary.Exists(temporary)) File.Delete(ObjectFiles.Output(temporary)); }
    }
    internal static void VerifyCurrent(IsoEditorPaths paths, IsoCustomObjectCatalog expected)
    {
        var local = new ObjectIssues(); var actual = Read(paths, local);
        if (local.HasErrors || !actual.Objects.OrderBy(o => o.AssetId, StringComparer.Ordinal).Select(o => (o.AssetId, o.ContentHash))
            .SequenceEqual(expected.Objects.OrderBy(o => o.AssetId, StringComparer.Ordinal).Select(o => (o.AssetId, o.ContentHash))))
            throw new IOException("Catalog changed since it was loaded.");
    }
}
