using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;

namespace Officelings.App;

// The sole composition point for real production, demo and QA services.
internal static class IsoEditorComposition
{
    internal static IsoEditorSession? Load(IsoPack bundled,AppStorage storage)
    {
        var importer=new LocalIsoCustomObjectImporter();
        var paths=new IsoEditorPaths(storage.Home);
        var loaded=IsoEditorStorage.Load(bundled,paths,importer);
        IsoEditorSession session=null!;
        session=new(new IsoEditor(loaded.Pack,loaded.Layout),loaded.Pack,bundled,importer,paths,loaded.Catalog,loaded.Layout)
        { CommitPack=(plan,cancellation)=>CommitPack(session,plan,cancellation) };
        session.Report(loaded.Issues); return session;
    }
    // Synchronous on the owning UI thread: consumer revisions cannot change during publication.
    private static IsoCustomObjectResult CommitPack(IsoEditorSession session,IsoCustomObjectPlan plan,CancellationToken cancellation)
    {
        var issues=plan.Issues;
        if (plan.Pack==null || issues.Any(i=>i.Severity==IsoIssueSeverity.Error)) return new(false,session.Catalog,issues);
        issues=issues.AddRange(session.Editor.PreviewPack(plan.Pack));
        if (session.Runtime!=null) issues=issues.AddRange(session.Runtime.PreviewEditorPack(plan.Pack));
        if (issues.Any(i=>i.Severity==IsoIssueSeverity.Error)) return new(false,session.Catalog,issues.Distinct().ToImmutableArray());
        var sheets=new PackIsoSheetSource(plan.Pack);
        foreach (var sheet in plan.Pack.Manifest.Sheets)
            if (sheet.Rows?.FirstOrDefault() is {} clip) sheets.GetCell(sheet.Id,clip.Key,0);
        var result=session.Importer.Commit(plan,cancellation);
        if (!result.Succeeded) return result;
        if (result.Catalog.Objects.Select(o=>(o.AssetId,o.ContentHash)).SequenceEqual(session.Catalog.Objects.Select(o=>(o.AssetId,o.ContentHash)))) return result;
        var editorResult=session.Editor.UpdatePack(plan.Pack);
        var runtimeIssues=session.Runtime?.UpdateEditorPack(plan.Pack) ?? [];
        if (!editorResult.Succeeded || runtimeIssues.Any(i=>i.Severity==IsoIssueSeverity.Error))
            throw new InvalidOperationException("A consumer changed after pack preflight.");
        session.AdoptPack(plan.Pack,result.Catalog,sheets); return result;
    }
}
