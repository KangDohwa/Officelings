namespace Officelings.Core.Iso.Editing;

internal static class ObjectGeometry
{
    internal static void Validate(IsoCustomObjectManifest m, IReadOnlyDictionary<string, ObjectPng> pixels, ObjectIssues issues, CancellationToken cancellation = default)
    {
        var sheets = m.Sheets.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var s in m.Sheets)
            if (pixels.TryGetValue(s.Id, out var png) && s.CellW is >= 1 and <= 512 && s.CellH is >= 1 and <= 512)
                foreach (var c in s.Rows ?? [])
                    if (Bounds(s, c, png))
                    {
                        var nonempty = false;
                        for (var f = 0; f < c.Frames && !nonempty; f++)
                            for (var y = 0; y < s.CellH && !nonempty; y++) for (var x = 0; x < s.CellW; x++)
                                if (png.Alpha((c.Column + f) * s.CellW + x, c.Row * s.CellH + y) > 0) { nonempty = true; break; }
                        if (nonempty && c.AnchorChecks?.Count is not > 0 || !Checks(s, c, png, c.AnchorChecks)) issues.Add("O007", s.Id);
                    }
        foreach (var asset in m.Furniture)
        {
            if (asset.Kind is not ("prop" or "desk" or "chair" or "computer" or "sofa" or "shelf" or "plant" or "lamp" or "rug")) issues.Add("O001");
            if (asset.Variants.Length == 0) issues.Add("O006", asset.Id);
            var variants = new HashSet<(int, string)>();
            foreach (var v in asset.Variants)
            {
                cancellation.ThrowIfCancellationRequested();
                var geometry = v.Rotation is 0 or 90 or 180 or 270 && v.Orientation == IsoGeometry.Orientation(v.Rotation) &&
                    !string.IsNullOrEmpty(v.State) && v.State.Length <= 96 && !v.State.Any(char.IsControl) && variants.Add((v.Rotation, v.State)) &&
                    v.FootprintW is >= 1 and <= 10 && v.FootprintH is >= 1 and <= 10 && double.IsFinite(v.Height) && v.Height is >= 0 and <= 96 &&
                    v.Layer is 0 or 1 && (v.Layer != 0 || v.Height == 0 && v.BlockedCells.Length == 0) &&
                    v.BlockedCells.Distinct().Count() == v.BlockedCells.Length && v.BlockedCells.All(t => t.X >= 0 && t.Y >= 0 && t.X < v.FootprintW && t.Y < v.FootprintH);
                var points = v.Points.IsDefault ? [] : v.Points;
                if (points.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != points.Length || points.Any(p =>
                    string.IsNullOrEmpty(p.Id) || p.Id.Length > 96 || p.X < -1 || p.Y < -1 || p.X > v.FootprintW || p.Y > v.FootprintH ||
                    (p.X < 0 || p.X >= v.FootprintW) && (p.Y < 0 || p.Y >= v.FootprintH) ||
                    p.Facing is not ("right" or "down" or "up" or "left") || p.IsoFacing != null && p.IsoFacing != IsoGeometry.Facing(p.Facing) ||
                    p.Action is not ("idle" or "coffee" or "phone" or "tv" or "water" or "darts" or "wave" or "celebrate" or "talk" or "chatting" or
                        "pickup" or "report-handover" or "reporting" or "surprised" or "sleep" or "napping" or "standing-thinking" or "standing-waiting"))) geometry = false;
                if (!geometry) issues.Add("O006", asset.Id);
                if (v.Parts.Length == 0) issues.Add("O007", asset.Id);
                var occupied = new HashSet<(long X, long Y)>();
                foreach (var p in v.Parts) { cancellation.ThrowIfCancellationRequested(); ValidatePart(p, false, v, sheets, pixels, occupied, issues, asset.Id); }
                if (v.Shadow != null) ValidatePart(v.Shadow, true, v, sheets, pixels, [], issues, asset.Id);
            }
        }
    }
    private static bool Bounds(IsoSheet s, IsoClip c, ObjectPng png) => c.Row >= 0 && c.Column >= 0 && c.Frames is >= 1 and <= 16 &&
        ((long)c.Row + 1) * s.CellH <= png.Height && ((long)c.Column + c.Frames) * s.CellW <= png.Width;
    private static bool Checks(IsoSheet s, IsoClip c, ObjectPng png, IReadOnlyList<IsoAnchorCheck>? checks)
    {
        foreach (var a in checks ?? [])
        {
            if (a.Radius is < 0 or > 1 || a.X < 0 || a.X >= s.CellW || a.Y < 0 || a.Y >= s.CellH) return false;
            for (var frame = 0; frame < c.Frames; frame++)
            {
                var found = false;
                for (var dy = -a.Radius; dy <= a.Radius; dy++) for (var dx = -a.Radius; dx <= a.Radius; dx++)
                    if (a.X + dx >= 0 && a.X + dx < s.CellW && a.Y + dy >= 0 && a.Y + dy < s.CellH &&
                        png.Alpha((c.Column + frame) * s.CellW + a.X + dx, c.Row * s.CellH + a.Y + dy) > 0) found = true;
                if (!found) return false;
            }
        }
        return true;
    }
    private static bool Depth(IsoGridPoint? d, IsoCustomFurnitureVariant v) => d is {} p && double.IsFinite(p.I) && double.IsFinite(p.J) &&
        p.I >= -.5 && p.I <= v.FootprintW - .5 && p.J >= -.5 && p.J <= v.FootprintH - .5;
    private static bool Anchor(PixelPoint? a, IsoSheet s) => ObjectSource.Integer(a) && a!.X is >= int.MinValue and <= int.MaxValue && a.Y is >= int.MinValue and <= int.MaxValue && IsoRenderBudget.AnchorFits(a, s.CellW, s.CellH);
    private static void ValidatePart(IsoPart p, bool shadow, IsoCustomFurnitureVariant v, Dictionary<string, IsoSheet> sheets,
        IReadOnlyDictionary<string, ObjectPng> pixels, HashSet<(long X, long Y)> occupied, ObjectIssues issues, string asset)
    {
        if (!sheets.TryGetValue(p.SheetId, out var s)) { issues.Add("O007", asset); return; }
        var c = s.Rows?.FirstOrDefault(c => c.Name == p.Animation && c.Direction == p.Direction) ??
            s.Rows?.FirstOrDefault(c => c.Name == p.Animation && c.Direction == "any");
        if (c == null) { issues.Add("O007", asset); return; }
        var anchor = p.Anchor ?? c.Anchor ?? s.Anchor;
        if (shadow != (s.Kind == "shadow") || !Depth(p.DepthAnchor, v) || !Depth(c.DepthAnchor, v) || p.DepthAnchor != c.DepthAnchor ||
            p.PartId != c.PartId || p.IsoZOrder != c.IsoZOrder || p.IsoZOrder is < -512 or > 511 || p.SupportRank is < 0 or > 4 ||
            !double.IsFinite(p.Opacity) || p.Opacity is < 0 or > 1 || p.Frame is {} f && (f < 0 || f >= c.Frames) ||
            !Anchor(anchor, s) || p.Anchor != null && c.Anchor != null && p.Anchor != c.Anchor ||
            p.AnchorChecks != null && c.AnchorChecks != null && !p.AnchorChecks.SequenceEqual(c.AnchorChecks)) issues.Add("O007", asset);
        if (!pixels.TryGetValue(s.Id, out var png) || s.CellW is < 1 or > 512 || s.CellH is < 1 or > 512 || !Bounds(s, c, png) || !Anchor(anchor, s) || p.Frame is {} selected && (selected < 0 || selected >= c.Frames)) return;
        if (!Checks(s, c, png, p.AnchorChecks)) issues.Add("O007", asset);
        if (shadow) return;
        var own = new HashSet<(long X, long Y)>();
        // Compare unions over authored frames, in the common ground-relative pixel coordinate system.
        var first = p.Frame ?? 0; var last = p.Frame ?? (c.Frames - 1);
        for (var frame = first; frame <= last; frame++)
            for (var y = 0; y < s.CellH; y++) for (var x = 0; x < s.CellW; x++)
                if (png.Alpha((c.Column + frame) * s.CellW + x, c.Row * s.CellH + y) > 0)
                    own.Add((x - (long)anchor!.X, y - (long)anchor.Y));
        if (own.Overlaps(occupied)) issues.Add("O007", asset);
        occupied.UnionWith(own);
    }
}
