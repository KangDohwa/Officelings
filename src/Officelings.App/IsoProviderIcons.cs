using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Officelings.Core;

namespace Officelings.App;

// The same bundled provider/role cells used by OfficeView, decoded on demand.
// BrandIcons remains first choice; this reader opens no personal pack/profile.
internal sealed class IsoProviderIcons
{
    private readonly Dictionary<(Provider,bool,int),BitmapSource?> cache=new();
    private readonly Dictionary<int,BitmapSource> sheets=new();
    private JsonElement[]? definitions;
    internal BitmapSource? Get(Provider provider,bool subagent,int zoom)
    {
        var size=zoom<2 ? 10 : 12; var key=(provider,subagent,size);
        if(cache.TryGetValue(key,out var cached)) return cached;
        try
        {
            if(definitions==null)
            {
                using var input=typeof(AssetPack).Assembly.GetManifestResourceStream("Officelings.Core.Pack.assets.json");
                if(input==null) return cache[key]=null;
                using var json=JsonDocument.Parse(input);
                definitions=json.RootElement.GetProperty("sheets").EnumerateArray()
                    .Where(s=>s.GetProperty("id").GetString() is "provider-icons" or "provider-icons-10").Select(s=>s.Clone()).ToArray();
            }
            var id=size==10 ? "provider-icons-10" : "provider-icons";
            var definition=definitions.SingleOrDefault(s=>s.GetProperty("id").GetString()==id);
            if(definition.ValueKind==JsonValueKind.Undefined) return cache[key]=null;
            if(!sheets.TryGetValue(size,out var image))
            {
                var file=definition.GetProperty("file").GetString()!;
                using var input=typeof(AssetPack).Assembly.GetManifestResourceStream("Officelings.Core.Pack."+Path.GetFileName(file));
                if(input==null) return cache[key]=null;
                var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.StreamSource=input; bitmap.EndInit(); bitmap.Freeze(); sheets[size]=image=bitmap;
            }
            var name=provider.ToString().ToLowerInvariant(); var role=name+(subagent ? "-sub-agent" : "-supervisor");
            var rows=definition.GetProperty("rows");
            var clip=rows.EnumerateArray().FirstOrDefault(r=>r.GetProperty("name").GetString()==role);
            if(clip.ValueKind==JsonValueKind.Undefined) clip=rows.EnumerateArray().First(r=>r.GetProperty("name").GetString()==name);
            var column=clip.TryGetProperty("column",out var c) ? c.GetInt32() : 0;
            var crop=new CroppedBitmap(image,new Int32Rect(column*size,clip.GetProperty("row").GetInt32()*size,size,size));
            crop.Freeze(); return cache[key]=crop;
        }
        catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or ArgumentException)
        { return cache[key]=null; }
    }
}