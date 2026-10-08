using System.Runtime.CompilerServices;

namespace Officelings.App;

// Keyed by the app storage lifetime, so flat native/privacy replacements cannot drop the iso history.
internal static class IsoEditorSessions
{
    private static readonly ConditionalWeakTable<AppStorage,IsoEditorSession> sessions=new();
    internal static IsoEditorSession? Get(AppStorage storage) => sessions.TryGetValue(storage,out var session) ? session : null;
    internal static void Remember(AppStorage storage,IsoEditorSession? session)
    { sessions.Remove(storage); if (session!=null) sessions.Add(storage,session); }
}
