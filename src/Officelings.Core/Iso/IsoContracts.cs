namespace Officelings.Core.Iso;
public enum IsoLayer { Floor, Wall, Shadow, Furniture, Character, Pet, Prop, Overlay }
/// X,Y = top-left of the sheet cell in scene pixel space (origin = scene top-left, y down). Items are sorted back-to-front by the engine; draw in list order.
public sealed record IsoDrawItem(string InstanceId, IsoLayer Layer, string SheetId, string Clip, int Frame, int X, int Y, long Depth, string? HitTargetId = null, byte Alpha = 255);
public sealed record IsoLabel(string AgentId, string Text, int X, int Y);
public sealed record IsoHit(string TargetId, string Kind);
public sealed record IsoScene(int Width, int Height, IReadOnlyList<IsoDrawItem> Items, IReadOnlyList<IsoLabel> Labels, long Revision);
public interface IIsoOffice
{
    /// Same inputs as the flat OfficeAnimator.Update. Revision increases only when something visible changed (renderer may skip redraw when unchanged).
    IsoScene Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds);
    IsoHit? HitTest(int sceneX, int sceneY);
    /// Seconds until the next visible change if nothing external happens (0 = now, double.PositiveInfinity = static).
    double NextChangeSeconds { get; }
}
