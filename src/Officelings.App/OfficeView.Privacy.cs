using System;
using System.Windows.Media;
using Officelings.Core;

namespace Officelings.App;

internal sealed partial class OfficeView
{
    internal PrivacyMode PrivacyMode { get; set; }
    internal string TagText(AgentView agent) => agent.Pose.DisplayName ?? agent.Agent.Name;

    private void DrawPrivacyBadge(DrawingContext dc)
    {
        if (PrivacyMode == PrivacyMode.Off) return;
        dc.DrawRoundedRectangle(Color("#303d40"),null,new(8,8,92,22),3,3);
        Text(dc,"privacy · "+PrivacyModes.Value(PrivacyMode),14,11,10,"#bcf0df");
    }

    internal void ClearPrivacyCaches()
    {
        // Clear the popup text and retained drawings before the next redacted update.
        agents = Array.Empty<AgentView>(); byId.Clear(); names.Clear(); textCache.Clear();
        retained.Clear(); drawOrder.Clear(); overlays.Clear(); overlayOrder.Clear(); overlayVisualOrder.Clear();
        sceneVisual.Children.Clear(); overlayVisual.Children.Clear();
        using (linksVisual.RenderOpen()) { }
        using (editorVisual.RenderOpen()) { }
        layersDrawn = false; sceneDirty = true;
        HideTooltip();
    }
}
