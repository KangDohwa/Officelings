using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed partial class OfficeView
{
    private readonly PetAnimator pets;
    private readonly AmbientEnvironment environment;
    private readonly Func<DateTimeOffset>? localClock;
    private IReadOnlyList<PetView> petViews = Array.Empty<PetView>();
    private AmbientState? ambientState;
    private DateTimeOffset? previousLocalTime;
    private long activityStamp;
    private readonly Dictionary<string, FurnitureSprite> ambientFrames = new(StringComparer.Ordinal);
    private IReadOnlyList<FurnitureSprite> resolvedFrames = Array.Empty<FurnitureSprite>();
    private bool hasClock;
    private OfficeLayout? ambientLayout;
    private IReadOnlyList<FurnitureSprite>? suppliedFrames;
    private readonly List<(Furniture Furniture, Sheet Sheet, bool Strings)> ambientSources = new();
    internal IReadOnlyList<PetView> Pets => petViews;
    internal AmbientState? AmbientState => ambientState;
    internal IReadOnlyList<FurnitureSprite> ResolvedFurnitureSprites => furnitureSprites;
    internal TimeSpan AnimationInterval => Program.MinimumInterval(pets.AnimationInterval, ambientInterval);
    internal DateTimeOffset NextUpdateAt => pets.NextUpdateAt;
    private TimeSpan ambientInterval;
    public bool PetsEnabled { get => pets.Enabled; set { pets.Enabled = value; } }
    public AmbientSettings AmbientSettings { get => environment.Settings; set => environment.Settings = value; }
    public string SeasonDecor { get => environment.SeasonDecor; set => environment.SeasonDecor = value; }

    private IReadOnlyList<FurnitureSprite> AmbientFrames(OfficeLayout layout, IReadOnlyList<AgentView> views,
        DateTimeOffset now, IReadOnlyList<FurnitureSprite> supplied)
    {
        var localTime = localClock?.Invoke() ?? now.ToLocalTime();
        ambientState = environment.Sample(localTime);
        var frames = ambientFrames;
        if (!ReferenceEquals(ambientLayout, layout))
        {
            ambientLayout = layout; ambientSources.Clear(); suppliedFrames = null;
            foreach (var furniture in layout.Furniture)
            {
                var strings = furniture.AssetId?.Contains("string-lights", StringComparison.Ordinal) == true;
                if (furniture.Kind is not ("window" or "clock") && !strings) continue;
                var variant = assets.Variant(furniture);
                if (variant.SheetId == null || variant.Animation == null) continue;
                ambientSources.Add((furniture, assets.Manifest.Sheets.First(s => s.Id == variant.SheetId), strings));
            }
        }
        if (!ReferenceEquals(suppliedFrames, supplied))
        {
            frames.Clear(); foreach (var frame in supplied) frames[frame.Id] = frame;
            suppliedFrames = supplied;
        }
        hasClock = false; ambientInterval = TimeSpan.Zero;
        foreach (var (furniture, sheet, strings) in ambientSources)
        {
            var clip = furniture.Kind == "window" ? ambientState.WindowView : furniture.Kind == "clock" ? "tick" :
                strings ? ambientState.StringLightsOn ? "twinkle" : "off" : null;
            AnimationRow? row = null;
            if (clip != null) foreach (var candidate in sheet.Rows) if (candidate.Name == clip) { row = candidate; break; }
            if (row == null)
            {
                frames.Remove(furniture.Id);
                foreach (var suppliedFrame in supplied) if (suppliedFrame.Id == furniture.Id) { frames[furniture.Id] = suppliedFrame; break; }
                continue;
            }
            hasClock |= furniture.Kind == "clock";
            var sprite = assets.Frame(sheet.Id, clip!, "any", furniture.Kind == "clock" ? 0 : localTime.ToUnixTimeMilliseconds());
            if (!frames.TryGetValue(furniture.Id, out var old) || old.Sprite != sprite) frames[furniture.Id] = new(furniture.Id, sprite);
            var interval = furniture.Kind == "clock" ? TimeSpan.FromSeconds(1) :
                row.Frames > 1 && row.Fps > 0 ? TimeSpan.FromSeconds(1 / row.Fps) : TimeSpan.Zero;
            ambientInterval = Program.MinimumInterval(ambientInterval, interval);
        }
        var equal = resolvedFrames.Count == frames.Count; var index = 0;
        foreach (var frame in frames.Values) if (!equal || resolvedFrames[index++] != frame) { equal = false; break; }
        if (!equal) resolvedFrames = frames.Values.ToArray();
        return resolvedFrames;
    }

    private void DrawPet(DrawingContext dc, PetView pet)
    {
        var s = pet.Sprite;
        dc.DrawImage(Image(s), new(Math.Round((pet.X + .5) * 16 - s.AnchorX * s.Scale + s.OffsetX),
            Math.Round((pet.Y + 1) * 16 - s.AnchorY * s.Scale + s.OffsetY), s.Width * s.Scale, s.Height * s.Scale));
    }

    private void DrawClock(DrawingContext dc, Furniture furniture, SpriteFrame sprite)
    {
        if (ambientState == null || sprite.Width != 64 || sprite.Height != 64) return;
        var x = furniture.X * 16 - sprite.AnchorX * sprite.Scale + sprite.OffsetX;
        var y = furniture.Y * 16 - sprite.AnchorY * sprite.Scale + sprite.OffsetY;
        var center = new Point(x + 32 * sprite.Scale, y + 50 * sprite.Scale);
        // Replace only the inner dial; retain the authored rim and wall contact.
        dc.DrawEllipse(Color("#f2ebd3"), null, center, 8 * sprite.Scale, 8 * sprite.Scale);
        void Hand(double angle, double length, string color)
        {
            for (var step = 0; step <= length; step++)
                R(dc, color, Math.Round(center.X + Math.Sin(angle) * step * sprite.Scale),
                    Math.Round(center.Y - Math.Cos(angle) * step * sprite.Scale), sprite.Scale, sprite.Scale);
        }
        var time = ambientState.LocalTime;
        Hand((time.Hour % 12 + time.Minute / 60d) * Math.PI / 6, 4, "#263d40");
        Hand((time.Minute + time.Second / 60d) * Math.PI / 30, 6, "#263d40");
        Hand(time.Second * Math.PI / 30, 7, "#b45e4d");
        R(dc, "#263d40", center.X, center.Y, 1, 1);
    }

    private void DrawActivity(DrawingContext dc, AgentView agent, bool flight = false)
    {
        var p = agent.Pose;
        var foot = new Point((p.X + .5) * 16, (p.Y + 1) * 16);
        var body = ContentBounds(p);
        if (p.Action == "darts")
        {
            var phase = activityStamp % 16 / 16d;
            if (!flight) { R(dc, "#e6c59e", foot.X + (phase < .25 ? 3 : 5), body.Top + 10, 3, 2); return; }
            var prop = office.Furniture.FirstOrDefault(f => f.Id == p.ActivitySpotId);
            if (prop == null || phase < .25 || phase >= .8) return;
            var variant = assets.Variant(prop);
            if (variant.SheetId == null || variant.Animation == null) return;
            var frame = assets.Frame(variant.SheetId, variant.Animation, "any", 0);
            var target = new Point(prop.X * 16 + (32 - frame.AnchorX) * frame.Scale + frame.OffsetX,
                prop.Y * 16 + (48 - frame.AnchorY) * frame.Scale + frame.OffsetY);
            var progress = (phase - .25) / .55;
            var dart = foot + new Vector(4, -20) + (target - (foot + new Vector(4, -20))) * progress;
            R(dc, "#f2ebd3", Math.Round(dart.X), Math.Round(dart.Y), 1, 3);
            R(dc, "#b45e4d", Math.Round(dart.X) - 1, Math.Round(dart.Y) + 2, 3, 1);
        }
        else if (p.Action == "pingpong" && p.ActivityPartnerId != null &&
            byId.TryGetValue(p.ActivityPartnerId, out var partner) && partner.Pose.Action == "pingpong")
        {
            var sign = p.Facing == "down" ? 1 : -1;
            if (!flight) { R(dc, "#b45e4d", foot.X + 4, foot.Y - 15 + sign * 3, 3, 4); return; }
            if (StringComparer.Ordinal.Compare(agent.Agent.Id, partner.Agent.Id) >= 0) return;
            var other = new Point((partner.Pose.X + .5) * 16, (partner.Pose.Y + 1) * 16);
            var phase = activityStamp % 16 / 8d;
            var t = phase <= 1 ? phase : 2 - phase;
            var ball = foot + (other - foot) * t + new Vector(0, -17 - Math.Sin(t * Math.PI) * 6);
            R(dc, "#fff4df", Math.Round(ball.X), Math.Round(ball.Y), 2, 2);
        }
        else if (p.Action == "reading" && !flight)
        {
            var x = Math.Round(body.X + body.Width / 2);
            var y = Math.Round(body.Bottom - 8);
            R(dc, "#e8dfbe", x - 5, y, 10, 5);
            R(dc, "#ae9470", x, y, 1, 5);
        }
    }
}
