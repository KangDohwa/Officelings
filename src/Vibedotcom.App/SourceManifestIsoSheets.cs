using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Vibedotcom.App;

internal sealed record IsoSourceClip(string SheetId, string Name, string Direction, int Row, int Column,
    int Frames, bool Loop, int Width, int Height, int AnchorX, int AnchorY, JsonElement Metadata)
{
    internal string Key => Direction == "any" ? Name : Name + ":" + Direction;
}

// Temporary source-manifest adapter. The view depends only on IIsoSheetSource;
// the embedded pack adapter can replace this class when the packaging lane lands.
internal sealed class SourceManifestIsoSheets : IIsoSheetSource
{
    private readonly Dictionary<string, string> definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), IsoSourceClip> clips = new();
    private readonly Dictionary<string, BitmapSource> sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, int, int), IsoSheetCell> frames = new();
    internal IReadOnlyList<IsoSourceClip> Clips { get; }
    internal IReadOnlyDictionary<string, BitmapSource> DecodedSheets => sheets;

    internal SourceManifestIsoSheets(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var files = new List<string>
        {
            Path.Combine(root,"env","floors","floors.iso.json"),
            Path.Combine(root,"env","walls","walls.iso.json"),
            Path.Combine(root,"env","polish.iso.json"),
            Path.Combine(root,"characters","dev-hoodie-v2","dev-hoodie.iso.json"),
            Path.Combine(root,"characters","punk-girl","punk-girl.iso.json"),
            Path.Combine(root,"characters","professor","professor.iso.json"),
            Path.Combine(root,"pets","cat.iso.json")
        };
        files.AddRange(Directory.GetFiles(Path.Combine(root,"furniture-v2"),"*.iso.json",SearchOption.AllDirectories));
        var props = Path.Combine(root,"props","carried-props.iso.json");
        if (File.Exists(props)) files.Add(props);
        var all = new List<IsoSourceClip>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var sheet in json.RootElement.GetProperty("sheets").EnumerateArray())
            {
                var id = sheet.GetProperty("id").GetString()!;
                var image = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,sheet.GetProperty("file").GetString()!));
                if (!image.StartsWith(root + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Iso source sheet escapes its source directory.");
                var width = sheet.GetProperty("cellW").GetInt32();
                var height = sheet.GetProperty("cellH").GetInt32();
                definitions.Add(id,image);
                var anchor = sheet.GetProperty("anchor");
                foreach (var row in sheet.GetProperty("rows").EnumerateArray())
                {
                    var a = row.TryGetProperty("anchor",out var own) ? own : anchor;
                    var clip = new IsoSourceClip(id,row.GetProperty("name").GetString()!,
                        row.TryGetProperty("direction",out var d) ? d.GetString()!.ToLowerInvariant() : "any",
                        row.GetProperty("row").GetInt32(),row.TryGetProperty("column",out var c) ? c.GetInt32() : 0,
                        row.GetProperty("frames").GetInt32(),row.TryGetProperty("loop",out var l) && l.GetBoolean(),
                        width,height,a.GetProperty("x").GetInt32(),a.GetProperty("y").GetInt32(),row.Clone());
                    all.Add(clip); clips.Add((id,clip.Key),clip);
                    if (clip.Direction != "any") { clips.TryAdd((id,clip.Name + "-" + clip.Direction),clip); clips.TryAdd((id,clip.Name + "/" + clip.Direction),clip); }
                    if (row.TryGetProperty("partId",out var p)) clips.TryAdd((id,p.GetString()!),clip);
                }
            }
        }
        all.Add(IsoPreviewContactShadow.Clip);
        clips.Add((IsoPreviewContactShadow.SheetId, IsoPreviewContactShadow.Clip.Key), IsoPreviewContactShadow.Clip);
        Clips = all;
    }

    internal static string FindSourceDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(),AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName,"assets","art","iso");
                if (File.Exists(Path.Combine(candidate,"env","floors","floors.iso.json"))) return candidate;
            }
        throw new DirectoryNotFoundException("Iso source manifests are unavailable. Pass --asset-dir assets/art/iso until the embedded iso pack is integrated.");
    }

    internal IsoSourceClip Clip(string sheetId, string key)
    {
        if (clips.TryGetValue((sheetId,key),out var clip)) return clip;
        var matches = Clips.Where(c=>c.SheetId==sheetId && c.Name==key).Take(2).ToArray();
        if (matches.Length==1) { clips[(sheetId,key)]=matches[0]; return matches[0]; }
        throw new InvalidDataException($"Missing or ambiguous iso clip: {sheetId}/{key}.");
    }

    public IsoSheetCell GetCell(string sheetId, string key, int frame)
    {
        if (sheetId == IsoPreviewContactShadow.SheetId && key == IsoPreviewContactShadow.Clip.Key)
            return IsoPreviewContactShadow.Cell;
        var clip = Clip(sheetId,key);
        if (frame<0 || frame>=clip.Frames) throw new InvalidDataException("Iso frame outside declared clip: " + key);
        var column = clip.Column + frame;
        if (frames.TryGetValue((sheetId,clip.Row,column),out var cached)) return cached;
        if (!sheets.TryGetValue(sheetId,out var image))
        {
            using var stream = File.OpenRead(definitions[sheetId]);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); sheets[sheetId]=image=bitmap;
        }
        var cell = new CroppedBitmap(image,new Int32Rect(column*clip.Width,clip.Row*clip.Height,clip.Width,clip.Height));
        cell.Freeze(); var result = new IsoSheetCell(cell,clip.Width,clip.Height,clip.AnchorX,clip.AnchorY,clip.Frames);
        frames[(sheetId,clip.Row,column)]=result; return result;
    }
}
