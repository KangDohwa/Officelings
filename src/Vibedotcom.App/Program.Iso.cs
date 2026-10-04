using System;
using System.Windows;
using System.Windows.Controls;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Forms=System.Windows.Forms;

namespace Vibedotcom.App;

internal sealed partial class OfficeWindow
{
    internal void SwitchIso()
    {
        var runtime=IsoApplication.Prepare(null,false);
        var bounds=modes.ScreenBounds;
        settings=settings with { View="iso",Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom };
        var replacement=new IsoWindow(runtime.Create(0),runtime.Sheets,settings,demo,qa,0,storage,trackerHome,false,agents,epoch);
        Application.Current.MainWindow=replacement; replacement.Show(); Close();
    }
}

internal sealed partial class IsoWindow
{
    internal void SwitchFlat()
    {
        var assets=AssetPack.LoadDefault(useUserPack:!storage.Isolated);
        var layout=LayoutLoader.Load(assets,storage.LayoutPath,assets.Warnings);
        var bounds=modes.ScreenBounds;
        settings=settings with { View="flat",Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom };
        var replacement=new OfficeWindow(demo,settings,0,assets,layout,qa,liveAgents:agents,demoEpoch:epoch,storage:storage,trackerHome:trackerHome);
        Application.Current.MainWindow=replacement; replacement.Show(); Close();
    }

    internal void SetPrivacy(PrivacyMode mode)
    {
        settings=settings with { PrivacyMode=PrivacyModes.Value(mode) };
        if (!demo && mode!=tracker.PrivacyMode)
        {
            tracker.SetPrivacy(mode); agents=tracker.Redact(agents);
            var bounds=modes.ScreenBounds;
            settings=settings with {Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom};
            // Replace the scene and text caches together so old details never
            // remain visible during an exit animation after privacy changes.
            var pack=sheets is PackIsoSheetSource source ? source.RuntimePack : null;
            var nextOffice=pack!=null && office is IsoOffice current ? new IsoOffice(pack,42,current.Layout) : office;
            var replacement=new IsoWindow(nextOffice,sheets,settings,demo,qa,0,storage,trackerHome,false,agents,epoch);
            Application.Current.MainWindow=replacement; replacement.Show(); Close();
        }
        else Save();
    }
    private void AddPrivacyMenu(ContextMenu menu)
    {
        var privacy=new MenuItem { Header="Privacy" }; menu.Items.Add(privacy);
        foreach(var mode in Enum.GetValues<PrivacyMode>())
        {
            var item=new MenuItem { Header=PrivacyTitle(mode) }; privacy.Items.Add(item);
            item.Click+=(_,_)=>SetPrivacy(mode); menu.Opened+=(_,_)=>item.IsChecked=settings.PrivacyMode==PrivacyModes.Value(mode);
        }
    }
    private void AddPrivacyMenu(Forms.ContextMenuStrip menu)
    {
        var privacy=new Forms.ToolStripMenuItem("Privacy"); menu.Items.Add(privacy);
        foreach(var mode in Enum.GetValues<PrivacyMode>())
        {
            var item=new Forms.ToolStripMenuItem(PrivacyTitle(mode)); privacy.DropDownItems.Add(item);
            item.Click+=(_,_)=>Dispatcher.Invoke(()=>SetPrivacy(mode)); menu.Opening+=(_,_)=>item.Checked=settings.PrivacyMode==PrivacyModes.Value(mode);
        }
    }
    private static string PrivacyTitle(PrivacyMode mode) => mode switch
    { PrivacyMode.Off=>"Off",PrivacyMode.Tasks=>"Hide tasks",_=>"Hide all details" };
}
