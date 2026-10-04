using System.Collections.Immutable;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.Core.Iso;

internal static class IsoEditorWallCatalog
{
    internal static (string Asset, int Index) Key(string id)
    {
        if (string.IsNullOrEmpty(id)) return ("", 0);
        var split = id.LastIndexOf("-edge-", StringComparison.Ordinal);
        return split >= 0 && int.TryParse(id.AsSpan(split + 6), out var index) ? (id[..split], index) : (id, 0);
    }
    internal static ImmutableArray<IsoWallDecorDefinition> Build(IsoPack pack) => (pack.Manifest.WallModules ?? [])
        .Where(m => m.Slot != null).GroupBy(m => Key(m.Id).Asset, StringComparer.Ordinal)
        .Where(g => g.Select(m => Key(m.Id).Index).Order().SequenceEqual(Enumerable.Range(0, g.Count())) && g.Select(m => m.Face).Distinct().Count() == 1)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => new IsoWallDecorDefinition(g.Key, g.Key, g.First().Face,
            g.OrderBy(m => Key(m.Id).Index).Select(m => m.Id).ToImmutableArray())).ToImmutableArray();
    internal static IsoGridPoint Edge(IsoGridPoint edge, string face, int index) =>
        face == "left" ? new(edge.I, edge.J + index) : new(edge.I + index, edge.J);
    internal static ImmutableArray<IsoWallSegment> Materialize(IsoPack pack, IReadOnlyList<IsoWallSegment> walls, IEnumerable<string>? reservedIds = null)
    {
        var result = walls.ToArray(); var used = walls.Select(w => w.Id).Concat(walls.Select(w => w.AssemblyId ?? "")).ToHashSet(StringComparer.Ordinal);
        used.UnionWith(reservedIds ?? []);
        var number = 0;
        foreach (var definition in Build(pack))
            foreach (var first in result.Where(w => w.AssetId == definition.ModuleIds[0] && w.HostId != null && w.AssemblyId == null).ToArray())
            {
                var run = definition.ModuleIds.Select((m, k) => Array.FindIndex(result, w => w.AssetId == m && w.Face == first.Face &&
                    w.Edge == Edge(first.Edge, first.Face, k) && w.HostId != null && w.AssemblyId == null)).ToArray();
                if (run.Any(k => k < 0)) continue;
                string id; do { id = "decor-" + ++number; } while (!used.Add(id));
                foreach (var k in run) result[k] = result[k] with { AssemblyId = id };
            }
        return result.ToImmutableArray();
    }
}
