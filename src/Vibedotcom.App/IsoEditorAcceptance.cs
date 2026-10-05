using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

// Shared real-service assertions for synthetic offscreen smoke and the integration test suite.
internal static class IsoEditorAcceptance
{
    internal static void Require(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static IsoLayout Empty(IsoEditorSession session) => session.Editor.State.Layout with
    { Furniture=[],WorkSeats=[],WallSegments=(session.Editor.State.Layout.WallSegments ?? []).Where(w=>w.HostId==null).ToImmutableArray() };

    internal static IsoEditorSession Create(IsoPack bundled,string root,bool empty=false)
    {
        var storage=new AppStorage(true,Path.Combine(root,"unread-personal"),root);
        var session=IsoEditorComposition.Load(bundled,storage)!;
        if (empty)
        {
            Require(new IsoEditor(session.Pack,Empty(session)).Save(session.LayoutPath).Succeeded,"Cannot create synthetic empty room.");
            session=IsoEditorComposition.Load(bundled,storage)!;
        }
        session.BindRuntime(new IsoOffice(session.Pack,42,session.LastValidLayout) { PetsEnabled=false });
        return session;
    }

    internal static int VerifyCommands(IsoPack bundled,string root)
    {
        var session=Create(bundled,root,true); var checks=0;
        void Check(bool value,string message) { Require(value,message); checks++; }
        foreach (var asset in session.Editor.Palette)
        {
            var editor=new IsoEditor(session.Pack,Empty(session)); var v=asset.Variants[0];
            var placed=editor.Place(asset.AssetId,new(3,3),v.Rotation,v.State);
            Check(placed.Succeeded,asset.AssetId+" place: "+string.Join(",",placed.Issues.Select(i=>i.RuleId)));
            var id=placed.PrimaryTargetId!;
            Check(editor.State.Layout.WorkSeats!.Count==0,"Placement invented a work position.");
            Check(editor.Move(id,new(1,4)).Succeeded,asset.AssetId+" move");
            foreach (var rotation in asset.Variants.Where(a=>a.State==v.State).Select(a=>a.Rotation).Distinct())
                Check(editor.Rotate(id,rotation).Succeeded,asset.AssetId+" rotation "+rotation);
            var duplicate=editor.Duplicate(id,new(6,6)); Check(duplicate.Succeeded,asset.AssetId+" duplicate");
            Check(duplicate.PrimaryTargetId!=id,"Duplicate reused an ID.");
            Check(editor.Delete(duplicate.PrimaryTargetId!).Succeeded,"Delete duplicate");
            Check(editor.Delete(id).Succeeded && editor.State.Layout.Furniture.Count==0,"Delete original");
            Check(editor.Undo().Succeeded && editor.Redo().Succeeded,"Undo/Redo");
        }
        var desk=session.Execute(new IsoPlaceCommand("iso/desk",new(4,4))).PrimaryTargetId!;
        var contact=IsoContacts.Desk(4,4,0);
        var chair=session.Execute(new IsoPlaceCommand("iso/chair",contact.Approach,IsoGeometry.Rotation(contact.Facing))).PrimaryTargetId!;
        Check(session.Execute(new IsoPlaceCommand("iso/computer",new(4,4),HostDeskId:desk)).Succeeded,"Mount computer");
        var computer=session.Editor.State.Layout.Furniture.Single(f=>f.AssetId=="iso/computer" && f.DeskId==desk).Id;
        var designation=session.Execute(new IsoSetSeatCommand(desk,chair,computer));
        Check(designation.Succeeded,"Designate chair/computer: "+string.Join("; ",designation.Issues.Select(i=>i.RuleId+" "+i.Message)));
        Check(session.Editor.State.Layout.WorkSeats!.Single().ComputerId==computer,"Explicit computer lost");
        Check(!session.Applying,"Immediate copied runtime layout did not acknowledge revision.");
        Check(session.Execute(new IsoClearSeatCommand(desk)).Succeeded,"Clear work position");
        Check(session.Editor.State.Layout.Furniture.Count==3 && session.Editor.State.Layout.WorkSeats!.Count==0,"Clear removed physical rig");
        session.Undo(); Check(session.Editor.State.Layout.WorkSeats!.Count==1,"Undo clear");
        Check(session.Execute(new IsoMoveCommand(chair,new(2,3))).Succeeded,"Move linked child");
        Check(session.Execute(new IsoRotateCommand(computer,90)).Succeeded,"Rotate linked child");
        Check(session.Editor.State.Layout.Furniture.Single(f=>f.Id==desk).Rotation==90,"Child did not resolve to root");
        Check(session.Execute(new IsoDuplicateCommand(desk,new(7,7))).Succeeded,"Duplicate linked assembly");
        Check(session.Editor.State.Layout.WorkSeats!.Count==2 && session.Editor.State.Layout.Furniture.Count==6,"Duplicate links/designations");
        return checks;
    }

    internal static int VerifyPersistence(IsoPack bundled,string root)
    {
        var session=Create(bundled,root); var checks=0;
        void Check(bool value,string message) { Require(value,message); checks++; }
        Directory.CreateDirectory(root);
        var flat=Path.Combine(root,"layout.json"); File.WriteAllText(flat,"synthetic flat sentinel");
        Check(session.Editor.Save(session.LayoutPath).Succeeded,"First save");
        var first=File.ReadAllBytes(session.LayoutPath); var editor=session.Editor;
        var desk=editor.State.Layout.WorkSeats!.First().DeskId;
        Check(session.Execute(new IsoClearSeatCommand(desk)).Succeeded,"Clear before restart");
        Check(editor.Save(session.LayoutPath).Succeeded,"Second save");
        Check(File.ReadAllBytes(session.LayoutPath+".bak").SequenceEqual(first),"Backup does not contain first save");
        var restart=Create(bundled,root);
        Check(JsonSerializer.Serialize(editor.State.Layout,StateJson.Options)==JsonSerializer.Serialize(restart.Editor.State.Layout,StateJson.Options),"Restart roundtrip");
        Check(restart.Editor.State.Layout.WorkSeats!.Count==11,"Reload re-enabled cleared seat");
        var live=session.LastValidLayout;
        var invalid=session.Execute(new IsoPlaceCommand("iso/plant",live.Door));
        Check(invalid.Succeeded && invalid.Issues.Any(i=>i.Severity==IsoIssueSeverity.Error),"Door-blocked draft must be accepted only as invalid");
        var saved=File.ReadAllBytes(session.LayoutPath);
        Check(!editor.Save(session.LayoutPath).Succeeded && File.ReadAllBytes(session.LayoutPath).SequenceEqual(saved),"Invalid Save wrote disk");
        Check(ReferenceEquals(live,session.LastValidLayout) && !session.Applying,"Invalid draft changed safe document");
        session.BindRuntime(IsoWindow.PrivacyOffice(session.Pack,(IsoOffice)session.Runtime!,session));
        Check(ReferenceEquals(editor,session.Editor) && session.Editor.State.CanUndo,"Privacy replacement lost editor/history");
        Check(((IsoOffice)session.Runtime!).Layout.Furniture.SequenceEqual(live.Furniture),"Privacy replacement used invalid draft");
        Check(File.ReadAllText(flat)=="synthetic flat sentinel","ISO touched flat layout");
        return checks;
    }

    internal static int VerifyObjects(IsoPack bundled,string root,string fixture)
    {
        var session=Create(bundled,root,true); var checks=0;
        void Check(bool value,string message) { Require(value,message); checks++; }
        var source=Path.Combine(root,"source"); Directory.CreateDirectory(source);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture)!)) File.Copy(file,Path.Combine(source,Path.GetFileName(file)));
        var manifest=Path.Combine(source,Path.GetFileName(fixture));
        var preview=session.Importer.ValidatePreview(manifest,CancellationToken.None);
        Check(preview.SourceIdentityHash=="88d16cbb12cbb96f98521eba4389d19d66789cca5395002227cb9d0dd91b1034","Source identity");
        Check(preview.VariantCount==1 && preview.DisplayName=="Synthetic test crate" && preview.ThumbnailPng.Length>8,"Verified metadata/thumbnail");
        File.WriteAllText(manifest,"{}"); // Import must continue from the verified capture.
        var request=new IsoCustomImportRequest(manifest,session.Paths,session.Catalog,bundled,preview);
        var plan=session.Importer.PrepareImport(request,CancellationToken.None);
        Check(plan.Pack!=null && !Directory.Exists(Path.Combine(root,"iso-objects")),"Preparation wrote catalog");
        using (var cancel=new CancellationTokenSource())
        {
            cancel.Cancel(); Check(!session.CommitPack!(plan,cancel.Token).Succeeded,"Cancelled commit succeeded");
            Check(!Directory.Exists(Path.Combine(root,"iso-objects")),"Cancellation wrote disk");
        }
        Check(session.CommitPack!(plan,CancellationToken.None).Succeeded,"Import commit");
        Check(session.Editor.Palette.Any(a=>a.AssetId=="user/test-crate"),"Palette did not adopt import");
        var catalog=File.ReadAllBytes(Path.Combine(root,"iso-objects","catalog.json")); var revision=session.Editor.State.Revision; var pack=session.Pack;
        plan=session.Importer.PrepareImport(request with {Catalog=session.Catalog},CancellationToken.None);
        Check(session.CommitPack!(plan,CancellationToken.None).Succeeded && session.Editor.State.Revision==revision && ReferenceEquals(pack,session.Pack),"Same-hash import was not idempotent");
        Check(File.ReadAllBytes(Path.Combine(root,"iso-objects","catalog.json")).SequenceEqual(catalog),"Same-hash changed catalog bytes");
        Check(session.Execute(new IsoPlaceCommand("user/test-crate",new(4,4))).Succeeded,"Place imported object");
        Check(!session.Remove("user/test-crate").Succeeded,"Referenced removal succeeded");
        plan=session.Importer.PrepareRemove(new("user/test-crate",session.Paths,session.Catalog,[],bundled));
        Check(!session.CommitPack!(plan,CancellationToken.None).Succeeded,"Editor preflight ignored references");
        Check(ReferenceEquals(pack,session.Pack) && File.ReadAllBytes(Path.Combine(root,"iso-objects","catalog.json")).SequenceEqual(catalog),"Failed preflight mutated consumers/disk");
        Check(session.Editor.Save(session.LayoutPath).Succeeded,"Save custom reference");
        var original=File.ReadAllBytes(session.LayoutPath); var entry=session.Catalog.Objects.Single();
        Directory.Move(entry.InstalledDirectory,entry.InstalledDirectory+"-missing");
        var loaded=IsoEditorStorage.Load(bundled,session.Paths,new LocalIsoCustomObjectImporter());
        Check(!loaded.UsedUserLayout && loaded.Issues.Any(i=>i.RuleId=="P002") && loaded.Layout.WorkSeats!.Count==12,"Missing custom startup fallback");
        Check(File.ReadAllBytes(session.LayoutPath).SequenceEqual(original),"Fallback modified source layout");
        var unused=Create(bundled,Path.Combine(root,"remove"),true);
        plan=unused.Importer.PrepareImport(new(fixture,unused.Paths,unused.Catalog,bundled),CancellationToken.None);
        var installed=unused.CommitPack!(plan,CancellationToken.None);
        Check(installed.Succeeded,"Unused import: "+string.Join("; ",installed.Issues.Select(i=>i.RuleId+" "+i.Message)));
        var removed=unused.Remove("user/test-crate");
        Check(removed.Succeeded,"Unreferenced removal: "+string.Join("; ",removed.Issues.Select(i=>i.RuleId+" "+i.Message)));
        Check(!unused.Editor.Palette.Any(a=>a.AssetId=="user/test-crate"),"Removal retained palette entry");
        return checks;
    }
    internal static int VerifyRuntime(IsoPack bundled,string root)
    {
        var session=Create(bundled,root); var office=(IsoOffice)session.Runtime!; var checks=0;
        void Check(bool value,string message) { Require(value,message); checks++; }
        var people=DemoScenario.Agents(IsoEditorSmoke.Epoch,0);
        for (var frame=0;frame<300;frame++) office.Update(people,IsoEditorSmoke.Epoch.AddSeconds(frame*.1),.1);
        var assigned=office.GetEditorSeatStatus().Where(s=>s.Status==IsoEditorSeatOccupancy.Assigned).Select(s=>s.SeatId).ToArray();
        Check(assigned.Length>0,"Synthetic runtime has no assigned seats.");
        Check(typeof(IsoEditorSeatStatus).GetProperties().All(p=>p.Name is "SeatId" or "Status"),"Status exposes identity.");
        office.Update(people.Select(p=>p with {State=AgentState.Gone}).ToArray(),IsoEditorSmoke.Epoch.AddSeconds(30),.05);
        Check(office.GetEditorSeatStatus().Any(s=>s.Status==IsoEditorSeatOccupancy.Away),"Gone reservation did not become away.");
        office.Update(people,IsoEditorSmoke.Epoch.AddSeconds(30.05),.05);
        Check(assigned.SequenceEqual(office.GetEditorSeatStatus().Where(s=>s.Status==IsoEditorSeatOccupancy.Assigned).Select(s=>s.SeatId)),"Returning actors lost reservations.");
        for (var frame=0;frame<40;frame++) office.Update(people,IsoEditorSmoke.Epoch.AddSeconds(30.1+frame*.1),.1);
        var live=office.Layout; var cleared=session.Execute(new IsoClearSeatCommand(assigned[0]));
        Check(cleared.Succeeded && session.Applying,"Occupied edit did not wait for safe retreat.");
        var invalid=session.Execute(new IsoPlaceCommand("iso/plant",session.Editor.State.Layout.Door));
        Check(invalid.Succeeded && invalid.Issues.Any(i=>i.Severity==IsoIssueSeverity.Error) && !session.Applying,"Invalid draft did not cancel Applying.");
        for (var frame=0;frame<100;frame++) office.Update(people,IsoEditorSmoke.Epoch.AddSeconds(34.1+frame*.1),.1);
        Check(office.Layout.WorkSeats!.Count==live.WorkSeats!.Count && office.Layout.Furniture.SequenceEqual(live.Furniture),"Cancelled swap later published.");
        Check(office.AppliedEditorRevision<invalid.State.Revision,"Invalid revision was acknowledged.");
        session.Undo();
        for (var frame=0;frame<100;frame++) office.Update(people,IsoEditorSmoke.Epoch.AddSeconds(44.1+frame*.1),.1);
        session.LiveApplied(office.Layout);
        Check(!session.Applying && office.AppliedEditorRevision==session.Editor.State.Revision,"Valid retry did not acknowledge after retreat.");
        return checks;
    }
}
