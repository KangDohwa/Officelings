namespace Vibedotcom.Core.Iso.Editing;

internal static class IsoEditorNavigation
{
    // Pets stay in one door-connected aisle component without routing through the door.
    internal static Tile[] PetComponent(IsoNavigation nav, Tile door, IEnumerable<Tile> reserved)
    {
        var aisle = nav.WithBlocked(reserved);
        var remaining = aisle.Reachable(door); remaining.Remove(door);
        var routes = aisle.WithBlocked([door]);
        var components = new List<Tile[]>();
        while (remaining.Count > 0)
        {
            var start = remaining.OrderBy(t => t.X).ThenBy(t => t.Y).First();
            var component = routes.Reachable(start).OrderBy(t => t.X).ThenBy(t => t.Y).ToArray();
            remaining.ExceptWith(component);
            if (component.Length >= 2) components.Add(component);
        }
        return components.OrderByDescending(c => c.Length).ThenBy(c => c[0].X).ThenBy(c => c[0].Y).FirstOrDefault() ?? [];
    }
}
