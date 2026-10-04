using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

// Consumes the packaging lane's manifest/sheets; it does not create or modify a
// pack. This is the single replacement for SourceManifestIsoSheets at shipping.
internal sealed class PackIsoSheetSource : IIsoSheetSource
{
    private sealed record Clip(string File,int Width,int Height,int Row,int Column,int Frames,int AnchorX,int AnchorY);
    private readonly Dictionary<(string,string),Clip> clips=new();
    private readonly Dictionary<string,BitmapSource> images=new(StringComparer.Ordinal);
    private readonly Dictionary<(string,int,int,int,int),BitmapSource> crops=new();
    private readonly Dictionary<(string,string,int),IsoSheetCell> cells=new();
    private readonly Func<string,Stream> openSheet;
    private readonly IsoPack? pack;
    internal PackIsoSheetSource(IsoPack pack)
    {
        this.pack=pack;
        openSheet=_=>throw new InvalidOperationException("Runtime sheets come from IsoPack.Images.");
    }
    private PackIsoSheetSource(Stream manifest,Func<string,Stream> openSheet)
    {
        this.openSheet=openSheet;
        using var doc=JsonDocument.Parse(manifest);
        if (doc.RootElement.GetProperty("projection").GetString()!="iso2to1") throw new InvalidDataException("Expected an iso2to1 pack.");
        foreach(var sheet in doc.RootElement.GetProperty("sheets").EnumerateArray())
        {
            var id=sheet.GetProperty("id").GetString()!; var file=sheet.GetProperty("file").GetString()!;
            var width=sheet.GetProperty("cellW").GetInt32(); var height=sheet.GetProperty("cellH").GetInt32();
            var anchor=sheet.GetProperty("anchor");
            var rows=sheet.TryGetProperty("rows",out var r) ? r : sheet.GetProperty("clips");
            foreach(var row in rows.EnumerateArray())
            {
                var name=row.GetProperty("name").GetString()!;
                var direction=row.TryGetProperty("direction",out var d) ? d.GetString()!.ToLowerInvariant() : "any";
                var a=row.TryGetProperty("anchor",out var own) ? own : anchor;
                var clip=new Clip(file,width,height,row.GetProperty("row").GetInt32(),row.TryGetProperty("column",out var c) ? c.GetInt32() : 0,
                    row.GetProperty("frames").GetInt32(),a.GetProperty("x").GetInt32(),a.GetProperty("y").GetInt32());
                clips.Add((id,direction=="any" ? name : name+":"+direction),clip);
                if(direction!="any") { clips.TryAdd((id,name+"/"+direction),clip); clips.TryAdd((id,name+"-"+direction),clip); }
                if(row.TryGetProperty("partId",out var part)) clips.TryAdd((id,part.GetString()!),clip);
            }
        }
    }
    internal static PackIsoSheetSource LoadDirectory(string root)
    {
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); using var manifest=File.OpenRead(Path.Combine(root,"assets.json"));
        return new(manifest,file=>
        {
            var path=Path.GetFullPath(Path.Combine(root,file));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Iso sheet escapes pack directory.");
            return File.OpenRead(path);
        });
    }
    internal static PackIsoSheetSource LoadEmbedded(Assembly assembly)
    {
        var names=assembly.GetManifestResourceNames();
        var manifestName=names.SingleOrDefault(n=>n.EndsWith(".assets.json",StringComparison.Ordinal) && n.Contains("iso",StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Embedded ISO manifest is unavailable; integrate the isopack resources.");
        using var manifest=assembly.GetManifestResourceStream(manifestName)!;
        return new(manifest,file=>
        {
            var suffix="."+Path.GetFileName(file);
            var resource=names.SingleOrDefault(n=>n.EndsWith(suffix,StringComparison.Ordinal) && n.Contains("iso",StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Missing embedded ISO sheet: "+file);
            return assembly.GetManifestResourceStream(resource)!;
        });
    }
    public IsoSheetCell GetCell(string sheetId,string key,int frame)
    {
        if(cells.TryGetValue((sheetId,key,frame),out var cached)) return cached;
        if (pack!=null)
        {
            var runtimeClip=pack.Clip(sheetId,key); var resolved=pack.Frame(sheetId,key,frame);
            if (frame<0 || frame>=runtimeClip.Frames) throw new InvalidDataException("ISO frame outside declared clip: "+key);
            if (!images.TryGetValue(sheetId,out var sheet))
            {
                using var stream=new MemoryStream(pack.Images[sheetId],false);
                var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); images[sheetId]=sheet=bitmap;
            }
            var cropKey=(sheetId,resolved.Row,resolved.Column,resolved.Width,resolved.Height);
            if (!crops.TryGetValue(cropKey,out var crop))
            {
                crop=new CroppedBitmap(sheet,new Int32Rect(resolved.X,resolved.Y,resolved.Width,resolved.Height));
                crop.Freeze(); crops[cropKey]=crop;
            }
            var runtimeCell=new IsoSheetCell(crop,resolved.Width,resolved.Height,(int)resolved.AnchorX,(int)resolved.AnchorY,runtimeClip.Frames);
            cells[(sheetId,key,frame)]=runtimeCell; return runtimeCell;
        }
        if(!clips.TryGetValue((sheetId,key),out var clip))
        {
            var canonical=key.Length>3 ? key[..^2]+key[^2..].ToLowerInvariant() : key;
            if(!clips.TryGetValue((sheetId,canonical),out clip))
            {
                var matches=clips.Where(p=>p.Key.Item1==sheetId && p.Key.Item2.StartsWith(key+":",StringComparison.Ordinal)).Select(p=>p.Value).Distinct().Take(2).ToArray();
                if(matches.Length!=1) throw new InvalidDataException("Unknown or ambiguous ISO clip: "+sheetId+"/"+key);
                clip=matches[0];
            }
            clips[(sheetId,key)]=clip;
        }
        if(frame<0 || frame>=clip.Frames) throw new InvalidDataException("ISO frame outside declared clip: "+key);
        if(!images.TryGetValue(clip.File,out var image))
        {
            using var stream=openSheet(clip.File);
            var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); images[clip.File]=image=bitmap;
        }
        var column=clip.Column+frame;
        var cacheKey=(clip.File,clip.Row,column,clip.Width,clip.Height);
        if(!crops.TryGetValue(cacheKey,out var cell))
        {
            cell=new CroppedBitmap(image,new Int32Rect(column*clip.Width,clip.Row*clip.Height,clip.Width,clip.Height)); cell.Freeze(); crops[cacheKey]=cell;
        }
        var result=new IsoSheetCell(cell,clip.Width,clip.Height,clip.AnchorX,clip.AnchorY,clip.Frames);
        cells[(sheetId,key,frame)]=result; return result;
    }
}
