namespace Vibedotcom.Core.Iso;

public sealed record IsoAnchorCheck(int X, int Y, int Radius = 0);
public sealed record IsoPhaseContact(PixelPoint SeatAnchor, PixelPoint ContactOffset, int FootY);
public sealed record IsoClipTarget(string Name, string Direction);
public sealed record IsoAlias(string Name, string Direction, IsoClipTarget Target, int? Frame = null,
    string? Effect = null, string? Prop = null, string? SeatProfile = null);
public sealed record IsoBlink(string SheetId, IReadOnlyList<int> NormalRows, int DurationMs = 100);
public sealed record IsoEyeMask(string EyeMask);
public sealed record IsoChairFrame(IReadOnlyList<string> Clips, string Hidden, string Overlap);
public sealed record IsoClip(string Name, string Direction, int Row, int Frames, double Fps,
    int Column = 0, bool Loop = true, PixelPoint? Anchor = null, PixelPoint? FootOffset = null,
    PixelPoint? SeatAnchor = null, IReadOnlyList<PixelPoint>? HandAnchors = null,
    IReadOnlyList<IsoPhaseContact>? PhaseContacts = null, IReadOnlyList<IReadOnlyList<PixelPoint>>? HandAnchorsByPhase = null,
    string? DeskOrientation = null, string? SeatProfile = null, string? PartId = null,
    IsoGridPoint? DepthAnchor = null, int IsoZOrder = 0, string? SeatGroupId = null,
    string? HitTargetId = null, IReadOnlyList<IsoAnchorCheck>? AnchorChecks = null,
    PixelPoint? EdgeAnchor = null, string? Effect = null, string? Prop = null, bool Transition = false,
    string? Family = null, string? Axis = null, int? Phase = null,
    IReadOnlyList<IsoChairFrame>? ChairFrames = null)
{
    // The scene API has no direction member: Clip carries this unique name/direction key.
    public string Key => Name + "/" + Direction;
}
public sealed record IsoSheet(string Id, string Kind, string File, int CellW, int CellH,
    PixelPoint Anchor, IReadOnlyList<IsoClip>? Rows = null, double Scale = 1, string Projection = "iso2to1",
    PixelPoint? FootAnchor = null, IReadOnlyList<string>? IdentityPalette = null,
    IReadOnlyList<IsoAlias>? Aliases = null, IsoBlink? Blink = null,
    IReadOnlyDictionary<string, IsoEyeMask>? FaceLock = null, int Width = 0, int Height = 0,
    string? Sha256 = null, IReadOnlyList<IsoClip>? Clips = null, IReadOnlyList<IsoPart>? Parts = null);
public sealed record IsoPart(string SheetId, string? Animation = null, string Direction = "any", string? PartId = null,
    IsoGridPoint? DepthAnchor = null, int IsoZOrder = 0, string? SeatGroupId = null, string? HitTargetId = null,
    PixelPoint? Anchor = null, IReadOnlyList<IsoAnchorCheck>? AnchorChecks = null, string? Clip = null,
    bool HoverPassthrough = false, double Opacity = 1, int? Frame = null);
public sealed record IsoInteraction(string Id, int X, int Y, string Action, string Facing = "up",
    string? IsoFacing = null, string? SeatProfile = null, IsoGridPoint? SeatGround = null,
    PixelPoint? ContactOffset = null, IReadOnlyList<PixelPoint>? HandContacts = null, string? SeatedFacing = null);
public sealed record IsoSeatProfile(string Id, Tile Approach, string IsoFacing, IsoGridPoint SeatGround,
    PixelPoint ContactOffset, IReadOnlyList<PixelPoint>? HandContacts = null, string? ApproachFacing = null);
public sealed record IsoFurnitureVariant(int Rotation, string Orientation, string State, int FootprintW, int FootprintH,
    IReadOnlyList<IsoPart> Parts, IReadOnlyList<IsoInteraction>? Points = null, IReadOnlyList<Tile>? BlockedCells = null,
    IReadOnlyList<IsoSeatProfile>? SeatProfiles = null, int Layer = 1, IsoPart? Shadow = null, IsoPart? Screen = null,
    PixelPoint? ChairSeatAnchor = null, IsoGridPoint? SeatGround = null, string? SeatFacing = null, IsoPart? Glow = null);
public sealed record IsoFurnitureAsset(string Id, IReadOnlyList<IsoFurnitureVariant> Variants, string? Kind = null);
public sealed record IsoWallModule(string Id, string Face, PixelPoint EdgeAnchor, IReadOnlyList<IsoPart> Parts);
public sealed record IsoPet(string Id, string SheetId, IsoPart? Shadow = null);
public sealed record IsoCarriedProp(string Id, string SheetId, string Animation, PixelPoint SourceGrip);
public sealed record IsoTileSpec(int Width, int Height, PixelPoint? Anchor = null);
public sealed record IsoCellSize(int Width, int Height);
public sealed record IsoManifest(int Version, string Projection, IReadOnlyList<IsoSheet> Sheets,
    IReadOnlyList<IsoFurnitureAsset>? Furniture = null, IReadOnlyList<CharacterDesign>? Characters = null,
    IReadOnlyList<TileType>? TileTypes = null, IReadOnlyList<IsoWallModule>? WallModules = null,
    IReadOnlyList<IsoPet>? Pets = null, IReadOnlyList<IsoCarriedProp>? CarriedProps = null,
    IReadOnlyList<string>? RequiredCapabilities = null, IsoTileSpec? Tile = null, int? WallRise = null, int? WallHeight = null,
    IsoCellSize? CharacterCell = null, PixelPoint? FootAnchor = null, PixelPoint? SeatAnchor = null,
    int? StandingHeight = null, int? DeskHeight = null, int? SeatHeight = null, IReadOnlyList<string>? Facings = null);
