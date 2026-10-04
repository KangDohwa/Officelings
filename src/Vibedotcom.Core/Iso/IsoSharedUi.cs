using System.Text.Json;

namespace Vibedotcom.Core.Iso;

// Shared UI is the sole flat-pack import. No flat body/furniture is eligible for iso scenes.
internal static class IsoSharedUi
{
    public const string SheetId = "iso/shared-bubbles", SpeechSheetId = "iso/shared-speech";
    public static (IsoSheet Sheet, byte[] Image) Load()
    {
        var assembly = typeof(IsoPack).Assembly;
        using var manifest = assembly.GetManifestResourceStream("Vibedotcom.Core.Pack.assets.json")
            ?? throw new InvalidDataException("Shared UI manifest is unavailable.");
        using var json = JsonDocument.Parse(manifest);
        var s = json.RootElement.GetProperty("sheets").EnumerateArray().First(s => s.GetProperty("id").GetString() == "bubble-icons")
            .Deserialize<Sheet>(StateJson.Options)!;
        if (s.CellW != 12 || s.CellH != 12 || s.Scale != 1) throw new InvalidDataException("Shared UI must retain native 12px cells.");
        using var source = assembly.GetManifestResourceStream("Vibedotcom.Core.Pack." + Path.GetFileName(s.File))
            ?? throw new InvalidDataException("Shared UI image is unavailable.");
        using var buffer = new MemoryStream(); source.CopyTo(buffer);
        var image = buffer.ToArray(); var (w, h) = AssetPack.PngSize(image);
        var sheet = new IsoSheet(SheetId, "overlay", s.File, s.CellW, s.CellH, s.Anchor,
            s.Rows.Select(c => new IsoClip(c.Name, "any", c.Row, c.Frames, c.Fps, c.Column, c.Loop)).ToArray(),
            Projection: "any", Width: w, Height: h, Sha256: s.Sha256);
        return (sheet, image);
    }
    public static (IsoSheet Sheet, byte[] Image) LoadSpeech()
    {
        // Keep the exact native v2 UI cell; no fallback body pixels enter the iso pack.
        var cell = IsoPngMask.Read(SpriteAtlas.CreatePng()).CopyCell(7 * 16, SpriteAtlas.BubbleRow * 24, 16, 24);
        var png = IsoActorShadow.EncodeRgba(16, 24, cell);
        return (new(SpeechSheetId, "overlay", "shared-speech.png", 16, 24, new(0, 0),
            [new("speech", "any", 0, 1, 1)], Projection: "any", Width: 16, Height: 24), png);
    }
    public static bool Supports(string? effect) => effect == null || effect == "speech-bubble" || Glyph(effect) != null;
    public static string? Glyph(string? effect) => effect switch
    {
        "waiting-bubble" => "question", "thinking-bubble" => "light-bulb", "sleep-bubble" or "sleep-indicator" => "zzz",
        "surprised-bubble" or "exclamation-bubble" => "exclamation-error",
        "celebration-bubble" => "sparkles", _ => null
    };
}
