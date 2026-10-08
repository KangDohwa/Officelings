using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Officelings.Core;

namespace Officelings.App;

internal static partial class Program
{
    internal static string ParseChoice(string value, string flag, params string[] values) =>
        values.Contains(value) ? value : throw new ArgumentException(flag + " must be " + string.Join(", ", values) + ".");
    internal static TimeSpan MinimumInterval(TimeSpan a, TimeSpan b) => a == TimeSpan.Zero ? b : b == TimeSpan.Zero ? a : a < b ? a : b;

    private static int AmbientRenders(string output, AssetPack assets, OfficeLayout layout, string home)
    {
        output = StateJson.OutputPath(output, home); Directory.CreateDirectory(output);
        var epoch = DateTimeOffset.Parse("2026-12-12T12:34:56+09:00");
        var office = new OfficeAnimator(42, assets, layout);
        var agents = Enumerable.Range(0, 18).Select(i => new AgentInfo($"ambient:{i:00}", (Provider)(i % 3),
            i >= 3 ? $"ambient:{i % 3:00}" : null, i >= 3, $"Agent {i + 1}", null, "demo", "C:/Demo/Office",
            "Fabricated ambient activity", null, null, AgentState.Idle, epoch, epoch, false)).ToArray();
        IReadOnlyList<AgentView> best = Array.Empty<AgentView>();
        IReadOnlyList<FurnitureSprite> bestFurniture = Array.Empty<FurnitureSprite>();
        OfficeLayout bestLayout = layout; var bestTime = epoch; var score = -1;
        for (var i = 0; i < 3000; i++)
        {
            var time = epoch.AddMilliseconds(i * 100);
            var views = office.Update(agents, time, .1);
            var active = views.Where(a => a.Pose.ActivitySpotId != null && a.Pose.Action != "walking").ToArray();
            var nextScore = active.Select(a => a.Pose.Action).Distinct().Count() * 20 + active.Length;
            if (nextScore <= score) continue;
            score = nextScore; best = views.ToArray(); bestFurniture = office.FurnitureSprites.ToArray(); bestLayout = office.Layout; bestTime = time;
        }
        var local = epoch;
        var view = new OfficeView(assets, Path.Combine(home, "brand"), localClock: () => local)
            { TagMode = "hover", SeasonDecor = "auto" };
        foreach (var (name, hour, weather) in new[] { ("day", 12, "off"), ("night", 22, "off"), ("rain", 12, "rain"), ("snow", 12, "snow") })
        {
            local = new(2026, 12, 12, hour, 34, 56, TimeSpan.FromHours(9));
            view.AmbientSettings = new(weather);
            view.Update(bestLayout, best, bestTime, bestFurniture); view.SetZoom(2);
            Render(view, layout.WidthTiles * 32 + 32, layout.HeightTiles * 32 + 32, Path.Combine(output, name + ".png"));
            StateJson.WriteAtomic(Path.Combine(output, name + "-state.json"), new StateSnapshot(StateJson.SchemaVersion,
                bestTime, 1, SpriteAtlas.Info, bestLayout, best, assets.Warnings, assets.Manifest, view.ResolvedFurnitureSprites, view.AmbientState, view.Pets));
        }
        ProviderTagCrops(output, assets, layout, home, epoch);
        File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new
        {
            offscreen = true, logBytesRead = 0, active = best.Where(a => a.Pose.ActivitySpotId != null)
                .Select(a => new { a.Agent.Id, a.Pose.Action, a.Pose.ActivitySpotId, a.Pose.ActivityPartnerId }),
            pets = view.Pets.Count, warnings = assets.Warnings, images = new[] { "day.png", "night.png", "rain.png", "snow.png", "tags-1x.png", "tags-2x.png", "tags-3x.png" }
        }, StateJson.Options));
        return assets.Warnings.Count == 0 ? 0 : 2;
    }

    private static void ProviderTagCrops(string output, AssetPack assets, OfficeLayout layout, string home, DateTimeOffset now)
    {
        var people = Enumerable.Range(0, 6).Select(i =>
        {
            var agent = new AgentInfo($"icon:{i}", (Provider)(i % 3), i >= 3 ? $"icon:{i % 3}" : null,
                i >= 3, new[] { "Codex", "Claude", "Antigravity" }[i % 3], null, null, null, "", null, null,
                AgentState.Idle, now, now, false);
            var design = assets.Assign(agent, Array.Empty<string>());
            var sprite = assets.CharacterFrame(agent, design, "idle", "down", 0);
            return new AgentView(agent, new Pose(4 + i % 3 * 8, 8 + i / 3 * 6, new(4, 8), "idle", "down", sprite.Row,
                sprite.Column, true, CharacterId: design.Id, Sprite: sprite, DisplayName: agent.Name));
        }).ToArray();
        foreach (var zoom in new[] { 1, 2, 3 })
        {
            var view = new OfficeView(assets, Path.Combine(home, "brand")) { TagMode = "always", PetsEnabled = false };
            view.SetZoom(zoom); view.Update(layout, people, now);
            var bitmap = RenderBitmap(view, layout.WidthTiles * 16 * zoom + 32, layout.HeightTiles * 16 * zoom + 32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(OfficeView.BackgroundColor), null, new(0, 0, 340 * zoom, 54 * zoom));
                for (var i = 0; i < people.Length; i++)
                {
                    var rect = view.AgentTagRect(people[i].Agent.Id); rect.Inflate(2 * zoom, 2 * zoom);
                    var crop = new CroppedBitmap(bitmap, new((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
                    dc.DrawImage(crop, new(6 * zoom + i % 3 * 110 * zoom, 5 * zoom + i / 3 * 24 * zoom, crop.PixelWidth, crop.PixelHeight));
                }
            }
            var image = new RenderTargetBitmap(340 * zoom, 54 * zoom, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, $"tags-{zoom}x.png")); encoder.Save(file);
        }
    }
}
