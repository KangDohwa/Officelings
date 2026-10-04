using System;
using System.Collections.Immutable;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

// The sole composition point. Fakes are supplied explicitly by UI tests/smoke, never a production fallback.
internal static class IsoEditorComposition
{
    internal static IsoEditorSession? Load(IsoPack bundled,AppStorage storage)
    {
        var type=typeof(IsoPack).Assembly.GetType("Vibedotcom.Core.Iso.Editing.Objects.LocalIsoCustomObjectImporter") ??
            typeof(IsoPack).Assembly.GetType("Vibedotcom.Core.Iso.Editing.LocalIsoCustomObjectImporter");
        if (type==null || !typeof(IIsoEditorRuntime).IsAssignableFrom(typeof(IsoOffice))) return null;
        var importer=(IsoCustomObjectImporter)(Activator.CreateInstance(type) ?? throw new InvalidOperationException("Cannot create local object importer."));
        var paths=new IsoEditorPaths(storage.Home);
        var loaded=IsoEditorStorage.Load(bundled,paths,importer);
        var session=new IsoEditorSession(new IsoEditor(loaded.Pack,loaded.Layout),loaded.Pack,bundled,importer,paths,loaded.Catalog,loaded.Layout);
        session.Report(loaded.Issues); return session;
    }
}
