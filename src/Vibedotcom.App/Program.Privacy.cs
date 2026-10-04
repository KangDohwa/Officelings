using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Vibedotcom.Core;
using Forms = System.Windows.Forms;

namespace Vibedotcom.App;

internal sealed partial record Settings
{
    public string? PrivacyMode { get; init; }
}

internal static partial class Program
{
    private static PrivacyMode StartupPrivacy(string[] args)
    {
        var index = Array.LastIndexOf(args, "--privacy");
        var option = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        // Invalid privacy input must not expose another option's value in an error.
        if (index >= 0 && option is not ("off" or "tasks" or "full")) return PrivacyMode.Full;
        var isolated = args.Any(a => a is "--demo" or "--qa" or "--smoke-test" or "--profile-idle" or "--verify-opacity");
        return PrivacyModes.Resolve(option, isolated ? null : PrivacyModes.ReadSetting(Path.Combine(TrackerOptions.OutputDirectory,"settings.json")),
            Environment.GetEnvironmentVariable("VIBEDOTCOM_PRIVACY"));
    }

    internal static StateSnapshot PrivateSnapshot(StateSnapshot snapshot, PrivacyMode requested)
    {
        var mode = (PrivacyMode)Math.Max((int)requested,(int)snapshot.Privacy);
        if (mode == PrivacyMode.Off) return snapshot;
        var redactor = new PrivacyRedactor(mode);
        return snapshot with { Privacy=mode, Agents=snapshot.Agents.Select(redactor.Redact).ToArray(),
            Office=snapshot.Office with { Furniture=snapshot.Office.Furniture.Select(f=>f with { AgentId=redactor.Id(f.AgentId) }).ToArray() },
            Pets=snapshot.Pets?.Select(p=>p with { FollowingId=redactor.Id(p.FollowingId) }).ToArray(),
            Warnings=PrivacyModes.Diagnostics(snapshot.Warnings,mode) };
    }
}

internal sealed partial class OfficeWindow
{
    private int privacyRevision;
    internal PrivacyMode PrivacyLevel => view.PrivacyMode;
    internal OfficeView PrivacyView => view;
    internal void SetPrivacy(PrivacyMode mode)
    {
        settings = settings with { PrivacyMode=PrivacyModes.Value(mode) };
        if (!demo && mode != view.PrivacyMode)
        {
            privacyRevision++;
            tracker.SetPrivacy(mode);
            agents=tracker.Redact(agents);
            view.PrivacyMode=mode; view.ClearPrivacyCaches(); office.ClearAgentCaches();
            warning=""; status.Text="";
            editor?.ClearPrivacyMessage();
            var now=DateTimeOffset.UtcNow;
            view.Update(editor?.Editor.Layout ?? office.Layout,office.Update(agents,now,0),now,office.FurnitureSprites);
            nextPoll=now; nextHostCheck=now;
            ScheduleAnimation();
        }
        Save();
    }

    private void AddPrivacyMenu(ContextMenu menu)
    {
        var privacy = new MenuItem { Header="Privacy" }; menu.Items.Add(privacy);
        foreach (var mode in Enum.GetValues<PrivacyMode>())
        {
            var item=new MenuItem { Header=PrivacyTitle(mode) }; privacy.Items.Add(item);
            item.Click+=(_,_)=>SetPrivacy(mode);
            menu.Opened+=(_,_)=>item.IsChecked=settings.PrivacyMode==PrivacyModes.Value(mode);
        }
    }

    private void AddPrivacyMenu(Forms.ContextMenuStrip menu)
    {
        var privacy=new Forms.ToolStripMenuItem("Privacy"); menu.Items.Add(privacy);
        foreach (var mode in Enum.GetValues<PrivacyMode>())
        {
            var item=new Forms.ToolStripMenuItem(PrivacyTitle(mode)); privacy.DropDownItems.Add(item);
            item.Click+=(_,_)=>Dispatcher.Invoke(()=>SetPrivacy(mode));
            menu.Opening+=(_,_)=>item.Checked=settings.PrivacyMode==PrivacyModes.Value(mode);
        }
    }

    private static string PrivacyTitle(PrivacyMode mode) => mode switch
    { PrivacyMode.Off=>"Off", PrivacyMode.Tasks=>"Hide tasks", _=>"Hide all details" };
}
