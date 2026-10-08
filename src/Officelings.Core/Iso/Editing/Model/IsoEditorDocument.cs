using System.Collections.Immutable;
using System.Text.Json;

namespace Officelings.Core.Iso.Editing;

internal static class IsoEditorDocument
{
    internal const int MaxBytes = 4_194_304;
    internal static readonly JsonSerializerOptions Json = new(StateJson.Options)
    { WriteIndented = false, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    internal static byte[] Bytes(IsoLayout layout) => JsonSerializer.SerializeToUtf8Bytes(layout, Json);
    internal static bool Equal(IsoLayout a, IsoLayout b) => ReferenceEquals(a, b) || Bytes(a).AsSpan().SequenceEqual(Bytes(b));
    internal static IsoLayout Freeze(IsoLayout layout) => layout with
    {
        Furniture = layout.Furniture == null ? null! : layout.Furniture.ToImmutableArray(),
        Tiles = layout.Tiles?.ToImmutableArray(), Zones = layout.Zones?.Select(z => z with { Points = z.Points?.ToImmutableArray() }).ToImmutableArray(),
        WallSegments = layout.WallSegments?.ToImmutableArray(),
        RequiredCapabilities = layout.RequiredCapabilities?.ToImmutableArray(), WorkSeats = layout.WorkSeats?.ToImmutableArray()
    };
    internal static IEnumerable<IsoSeatDesignation> Seats(IsoLayout layout) => layout.WorkSeats ??
        (layout.Furniture ?? []).Where(f => f.AssetId == "iso/desk").Select(d => new IsoSeatDesignation(d.Id, d.Id,
            (layout.Furniture ?? []).FirstOrDefault(c => c.AssetId == "iso/chair" && c.DeskId == d.Id)?.Id ?? "",
            (layout.Furniture ?? []).FirstOrDefault(c => c.AssetId == "iso/computer" && c.DeskId == d.Id)?.Id));
    internal static IsoLayout Own(IsoLayout layout, IsoPack pack)
    {
        var owned = Freeze(layout) with
        {
            Tile = layout.Tile is {} tile ? tile with { Anchor = tile.Anchor ?? new(32, 16) } : new(64, 32, new(32, 16)),
            WallRise = layout.WallRise ?? 16, WallHeight = layout.WallHeight ?? 48
        };
        if (layout.EditorVersion != null || layout.WorkSeats != null || layout.RequiredCapabilities?.Contains("isoEditorV1") == true) return owned;
        return owned with
        {
            EditorVersion = 1, WorkSeats = Seats(owned).ToImmutableArray(),
            RequiredCapabilities = (owned.RequiredCapabilities ?? []).Concat(["iso2to1", "renderItemsV1", "isoEditorV1"])
                .Distinct(StringComparer.Ordinal).ToImmutableArray(),
            WallSegments = IsoEditorWallCatalog.Materialize(pack, owned.WallSegments ?? [], (owned.Furniture ?? []).Select(f => f.Id))
        };
    }
    internal static IsoFurnitureVariant Freeze(IsoFurnitureVariant v) => v with
    {
        Parts = v.Parts.Select(Freeze).ToImmutableArray(), BlockedCells = v.BlockedCells?.ToImmutableArray(),
        Points = v.Points?.Select(p => p with { HandContacts = p.HandContacts?.ToImmutableArray() }).ToImmutableArray(),
        SeatProfiles = v.SeatProfiles?.Select(s => s with { HandContacts = s.HandContacts?.ToImmutableArray() }).ToImmutableArray(),
        Shadow = v.Shadow == null ? null : Freeze(v.Shadow), Screen = v.Screen == null ? null : Freeze(v.Screen),
        Glow = v.Glow == null ? null : Freeze(v.Glow)
    };
    private static IsoPart Freeze(IsoPart p) => p with { AnchorChecks = p.AnchorChecks?.ToImmutableArray() };
    internal static IsoLayout Default(IsoPack pack) => Own(pack.Layout.Furniture.Count(f => f.AssetId == "iso/desk") == 12 ? pack.Layout : IsoTwelveSeatFixture.OfficeLayout(pack), pack);
    internal static IEnumerable<string> Ids(IsoLayout l) => (l.Furniture ?? []).Select(f => f.Id)
        .Concat((l.WallSegments ?? []).SelectMany(w => w.AssemblyId == null ? new[] { w.Id } : new[] { w.Id, w.AssemblyId }));
    internal static IEnumerable<string> Assets(IsoLayout l) => (l.Furniture ?? []).Select(f => f.AssetId)
        .Concat((l.WallSegments ?? []).Select(w => w.AssetId));
    internal static IsoFurnitureVariant? Variant(IsoPack pack, IsoPlacement f) =>
        pack.Manifest.Furniture?.FirstOrDefault(a => a.Id == f.AssetId)?.Variants.FirstOrDefault(v => v.Rotation == f.Rotation && v.State == f.State);
}
