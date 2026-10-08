using System.Windows.Media;
using System.Windows.Media.Imaging;
using Officelings.App;
using Officelings.Core;
using Officelings.Core.Iso;

namespace Officelings.Tests;

// Deliberately hand-placed render fixture. There is no navigation, assignment,
// lifecycle or simulation here. SetPhase selects authored frames for QA.
internal sealed class FakeIsoOffice : IIsoOffice
{
    private readonly SourceManifestIsoSheets sheets;
    private readonly IReadOnlyList<AgentInfo> people;
    private readonly Dictionary<BitmapSource, byte[]> pixels = new();
    private long revision;
    internal IsoScene Scene { get; private set; }
    internal int UpdateCount { get; private set; }
    internal (int X, int Y)? LastHit { get; private set; }
    public double NextChangeSeconds { get; set; } = double.PositiveInfinity;

    internal FakeIsoOffice(SourceManifestIsoSheets sheets, IReadOnlyList<AgentInfo>? people = null)
    {
        this.sheets = sheets; this.people = people ?? Agents(12); Scene = Build(0);
    }
    internal void SetPhase(int phase) => Scene = Build(phase);
    internal void SetSnapshot(IsoScene next) => Scene = next;
    public IsoScene Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    { UpdateCount++; return Scene; }

    internal static AgentInfo[] Agents(int count)
    {
        var epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        return Enumerable.Range(0, count).Select(i => new AgentInfo($"iso-fake:{i:00}", (Provider)(i % 3), i >= 3 ? $"iso-fake:{i % 3:00}" : null,
            i >= 3, $"Worker {i + 1}", i < 3 ? "Lead" : "Worker", "synthetic-model", "C:/Synthetic/Office", "Synthetic iso render scene",
            "edit_file", "file", AgentState.Working, epoch, epoch, false)).ToArray();
    }
    private static (int X, int Y) Project(double i, double j) => ((int)Math.Floor(336 + 32 * (i - j) + .5), (int)Math.Floor(80 + 16 * (i + j) + .5));
    private static long Depth(double i, double j, int z = 0) => (long)Math.Floor((i + j) * 1024 + .5) * 100 + z;

    private IsoScene Build(int phase)
    {
        phase = Math.Clamp(phase, 0, 3);
        var floors = new List<IsoDrawItem>(); var walls = new List<IsoDrawItem>(); var shadows = new List<IsoDrawItem>(); var objects = new List<IsoDrawItem>();
        var labels = new List<IsoLabel>();
        void Add(List<IsoDrawItem> list, string id, IsoLayer layer, string sheet, string key, double i, double j, long depth,
            string? hit = null, int frame = 0, int dx = 0, int dy = 0)
        {
            var clip = sheets.Clip(sheet, key); var p = Project(i, j);
            list.Add(new(id, layer, sheet, clip.Key, frame % clip.Frames, p.X - clip.AnchorX + dx, p.Y - clip.AnchorY + dy, depth, hit));
        }
        for (var i = 0; i < 10; i++) for (var j = 0; j < 10; j++)
        {
            var checker = i >= 7 && j <= 2;
            Add(floors, $"floor:{i}:{j}", IsoLayer.Floor, "iso-env-floors", checker ? (i + j) % 2 == 0 ? "checker-dark" : "checker-light" : "wood-i", i, j, Depth(i, j));
        }
        for (var n = 0; n < 10; n++)
        {
            Add(walls, $"wall:l:{n}", IsoLayer.Wall, "iso-env-walls", "plain-l", -.5, n - .5, Depth(-.5, n - .5));
            Add(walls, $"wall:r:{n}", IsoLayer.Wall, "iso-env-walls", "plain-r", n - .5, -.5, Depth(n - .5, -.5));
            if (n is 2 or 3 or 6 or 7)
            {
                Add(walls, $"glass:l:{n}", IsoLayer.Wall, "iso-env-walls", "window-glass-l", -.5, n - .5, 0);
                Add(walls, $"window:l:{n}", IsoLayer.Wall, "iso-env-walls", "window-frame-l", -.5, n - .5, 0);
                Add(walls, $"glass:r:{n}", IsoLayer.Wall, "iso-env-walls", "window-glass-r", n - .5, -.5, 0);
                Add(walls, $"window:r:{n}", IsoLayer.Wall, "iso-env-walls", "window-frame-r", n - .5, -.5, 0);
            }
        }
        Add(walls, "wall:corner", IsoLayer.Wall, "iso-env-wall-corner", "inner-rear-corner", -.5, -.5, 0);
        // All four desk orientations, with space between workstations and aisle
        // walkers. Seat contacts below are the frozen S0 front profile.
        (double I, double J, string Facing, double SeatI, double SeatJ, string Worker)[] desks =
        [ (1, 2, "se", 1, 0, "nw"), (4, 1, "sw", 1, 1, "ne"), (7, 3, "nw", -1, 1, "se"),
          (1, 6, "ne", 0, -1, "sw"), (4, 5, "se", 1, 0, "nw"), (6, 7, "nw", -1, 1, "se") ];
        for (var index = 0; index < desks.Length; index++)
        {
            var d = desks[index]; var si = d.I + d.SeatI; var sj = d.J + d.SeatJ; var seatDepth = Depth(si, sj);
            Add(shadows, $"desk-shadow:{index}", IsoLayer.Shadow, "iso-desk-shadow", "shadow:" + d.Facing, d.I, d.J, 0);
            Add(shadows, $"chair-shadow:{index}", IsoLayer.Shadow, "iso-chair-shadow", "shadow:" + d.Worker, si, sj, 0);
            foreach (var clip in sheets.Clips.Where(c => c.SheetId == "iso-desk-parts" && c.Direction == d.Facing))
            {
                var m = clip.Metadata; var anchor = m.GetProperty("depthAnchor");
                var z = m.GetProperty("isoZOrder").GetInt32();
                var depth = m.TryGetProperty("seatGroupId", out _) ? seatDepth + z : Depth(d.I + anchor.GetProperty("i").GetDouble(), d.J + anchor.GetProperty("j").GetDouble(), z);
                Add(objects, $"desk:{index}:{clip.Name}", IsoLayer.Furniture, clip.SheetId, clip.Key, d.I, d.J, depth, "desk:" + index);
            }
            var screen = sheets.Clip("iso-desk-screen", "screen-working:" + d.Facing);
            var screenAnchor = screen.Metadata.GetProperty("depthAnchor");
            Add(objects, $"screen:{index}", IsoLayer.Furniture, screen.SheetId, screen.Key, d.I, d.J,
                Depth(d.I + screenAnchor.GetProperty("i").GetDouble(), d.J + screenAnchor.GetProperty("j").GetDouble(), 1), frame: phase);
            foreach (var clip in sheets.Clips.Where(c => c.SheetId == "iso-chair-parts" && c.Direction == d.Worker))
                Add(objects, $"chair:{index}:{clip.Name}", IsoLayer.Furniture, clip.SheetId, clip.Key, si, sj,
                    seatDepth + clip.Metadata.GetProperty("isoZOrder").GetInt32(), "desk:" + index);
            if (index < people.Count) AddPerson(people[index], si, sj, "seated-typing-front", d.Worker, seated: true);
        }
        (double I, double J, string Facing)[] walkers = [(2, 8, "se"), (4, 9, "ne"), (8, 6, "sw"), (9, 8, "nw"), (6, 4, "se"), (3, 4, "sw")];
        for (var index = 6; index < Math.Min(12, people.Count); index++)
        { var p = walkers[index - 6]; AddPerson(people[index], p.I, p.J, "walk", p.Facing, seated: false); }
        var catKey = phase == 0 ? "sleep" : "walk:sw";
        Add(shadows, "cat-shadow", IsoLayer.Shadow, "iso-cat-shadow", catKey, 7.5, 8.5, 0, frame: phase);
        Add(objects, "cat", IsoLayer.Pet, "iso-cat-normal", catKey, 7.5, 8.5, Depth(7.5, 8.5), "cat", phase);
        var ordered = floors.Concat(walls).Concat(shadows).Concat(objects.OrderBy(o => o.Depth).ThenBy(o => o.InstanceId, StringComparer.Ordinal)).ToArray();
        return new(672, 416, ordered, labels, ++revision);

        void AddPerson(AgentInfo agent, double i, double j, string action, string facing, bool seated)
        {
            if (!seated) Add(shadows, "contact:" + agent.Id, IsoLayer.Shadow, IsoPreviewContactShadow.SheetId, "contact", i, j, 0);
            string[] designs = ["dev-hoodie-v2-normal", "punk-girl-normal", "professor-normal"];
            var sheet = designs[SpriteAtlas.Hash(agent.Id) % 3]; var clip = sheets.Clip(sheet, action + ":" + facing); var p = Project(i, j);
            // Source foot (16,60), seated pelvis (16,44) at ground y-12.
            var x = p.X - 16; var y = p.Y - (seated ? 56 : 60);
            objects.Add(new("body:" + agent.Id, IsoLayer.Character, sheet, clip.Key, phase, x, y, Depth(i, j), agent.Id));
            labels.Add(new(agent.Id, agent.Name, p.X, y + 17));
        }
    }

    public IsoHit? HitTest(int sceneX, int sceneY)
    {
        LastHit = (sceneX, sceneY);
        for (var i = Scene.Items.Count - 1; i >= 0; i--)
        {
            var item = Scene.Items[i]; var cell = sheets.GetCell(item.SheetId, item.Clip, item.Frame);
            var x = sceneX - item.X; var y = sceneY - item.Y;
            if (x < 0 || y < 0 || x >= cell.Width || y >= cell.Height || item.Alpha < 32) continue;
            if (!pixels.TryGetValue(cell.Image, out var bytes))
            {
                var bitmap = new FormatConvertedBitmap(cell.Image, PixelFormats.Bgra32, null, 0);
                bytes = new byte[cell.Width * cell.Height * 4]; bitmap.CopyPixels(bytes, cell.Width * 4, 0); pixels.Add(cell.Image, bytes);
            }
            if (bytes[(y * cell.Width + x) * 4 + 3] * item.Alpha / 255 < 32) continue;
            return item.HitTargetId == null ? null : new(item.HitTargetId, item.Layer == IsoLayer.Character ? "agent" : item.Layer == IsoLayer.Pet ? "pet" : "furniture");
        }
        return null;
    }
}
