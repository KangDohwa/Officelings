namespace Vibedotcom.Core;

public sealed partial class OfficeAnimator
{
    // Drop every reference to the prior agent metadata, including hidden and named poses.
    public void ClearAgentCaches()
    {
        characters.Clear(); newcomers.Clear(); leisureDue.Clear(); furnitureOwners.Clear(); activityFurnitureOwners.Clear();
        views = []; ids.Clear(); removedIds.Clear(); deskHistory.Clear(); workReservations.Clear();
        Layout = Layout with { Furniture = Layout.Furniture.Select(f => f with { AgentId = null }).ToArray() };
        workplacesDirty = true;
    }
}
