using System.Text.Json;

namespace Vibedotcom.Core.Iso.Editing;

internal sealed class ObjectFieldException(string rule) : Exception
{
    internal string Rule { get; } = rule;
}

internal static class ObjectSchema
{
    internal static JsonDocument Parse(byte[] bytes)
    {
        // UTF-8 only, no BOM; duplicate keys are checked even in descriptive provenance.
        if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) throw new JsonException();
        var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        try { Unique(json.RootElement); return json; }
        catch { json.Dispose(); throw; }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) { if (!keys.Add(p.Name)) throw new JsonException(); Unique(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static void Shape(JsonElement e, string required, string optional = "")
    {
        if (e.ValueKind != JsonValueKind.Object) throw new JsonException();
        var allowed = (required + " " + optional + " name provenance").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            if (!allowed.Contains(p.Name) || p.Value.ValueKind == JsonValueKind.Null) throw new JsonException();
            if (p.Name == "name" && p.Value.ValueKind != JsonValueKind.String) throw new JsonException();
            if (p.Name == "provenance") Description(p.Value);
        }
        foreach (var key in required.Split(' ', StringSplitOptions.RemoveEmptyEntries)) if (!e.TryGetProperty(key, out _)) throw new JsonException();
    }
    private static void Description(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return;
        if (e.ValueKind != JsonValueKind.Object) throw new JsonException();
        foreach (var p in e.EnumerateObject()) if (p.Value.ValueKind != JsonValueKind.String) throw new JsonException();
    }
    private static IEnumerable<JsonElement> Array(JsonElement e, string key, int maximum, bool optional = false)
    {
        if (optional && !e.TryGetProperty(key, out _)) return [];
        var a = e.GetProperty(key);
        if (a.ValueKind != JsonValueKind.Array) throw new JsonException();
        if (a.GetArrayLength() > maximum) throw new ObjectSizeException();
        return a.EnumerateArray();
    }
    private static void Point(JsonElement e, string key, string coordinates)
    {
        if (!e.TryGetProperty(key, out var p)) return;
        Shape(p, coordinates);
        foreach (var c in coordinates.Split(' ')) Number(p, c, "O007", coordinates == "x y");
    }
    private static void Number(JsonElement e, string key, string rule, bool integer = false)
    {
        if (!e.TryGetProperty(key, out var n)) return;
        if (n.ValueKind != JsonValueKind.Number) throw new JsonException();
        if (integer ? !n.TryGetInt32(out _) : !n.TryGetDouble(out var d) || !double.IsFinite(d)) throw new ObjectFieldException(rule);
    }
    private static void Checks(JsonElement e)
    {
        foreach (var c in Array(e, "anchorChecks", int.MaxValue, true))
        { Shape(c, "x y", "radius"); foreach (var k in new[] { "x", "y", "radius" }) Number(c, k, "O007", true); }
    }
    private static void Part(JsonElement e)
    {
        Shape(e, "sheetId animation direction partId depthAnchor isoZOrder", "anchor anchorChecks supportRank opacity frame");
        Point(e, "anchor", "x y"); Point(e, "depthAnchor", "i j"); Checks(e);
        foreach (var k in new[] { "isoZOrder", "supportRank", "frame" }) Number(e, k, "O007", true);
        Number(e, "opacity", "O007");
    }
    internal static void Validate(JsonElement e)
    {
        Shape(e, "version customObjectVersion projection id displayName palette sheets furniture");
        foreach (var p in Array(e, "palette", int.MaxValue)) if (p.ValueKind != JsonValueKind.String) throw new JsonException();
        foreach (var s in Array(e, "sheets", 8))
        {
            Shape(s, "id kind file cellW cellH anchor scale projection rows"); Point(s, "anchor", "x y");
            Number(s, "cellW", "O004", true); Number(s, "cellH", "O004", true);
            foreach (var c in Array(s, "rows", 64))
            {
                Shape(c, "name direction row frames fps loop partId depthAnchor isoZOrder", "column anchor anchorChecks");
                Point(c, "anchor", "x y"); Point(c, "depthAnchor", "i j"); Checks(c);
                foreach (var k in new[] { "row", "column", "isoZOrder" }) Number(c, k, "O007", true);
                Number(c, "frames", "O004", true); Number(c, "fps", "O004");
            }
        }
        var families = Array(e, "furniture", int.MaxValue).ToArray();
        if (families.Length != 1) throw new JsonException();
        foreach (var f in families)
        {
            Shape(f, "id kind variants");
            foreach (var v in Array(f, "variants", 16))
            {
                Shape(v, "rotation orientation state footprintW footprintH blockedCells height parts", "id layer shadow points");
                foreach (var k in new[] { "rotation", "footprintW", "footprintH", "layer" }) Number(v, k, "O006", true);
                Number(v, "height", "O006");
                if (v.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.String) throw new JsonException();
                foreach (var b in Array(v, "blockedCells", int.MaxValue)) { Shape(b, "x y"); Number(b, "x", "O006", true); Number(b, "y", "O006", true); }
                foreach (var p in Array(v, "parts", 32)) Part(p);
                if (v.TryGetProperty("shadow", out var shadow)) Part(shadow);
                foreach (var p in Array(v, "points", int.MaxValue, true)) { Shape(p, "id x y action", "facing isoFacing"); Number(p, "x", "O006", true); Number(p, "y", "O006", true); }
            }
        }
    }
}
