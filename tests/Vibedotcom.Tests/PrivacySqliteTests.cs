using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Providers;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class PrivacySqliteTests : IDisposable
{
    private const string Secret="SECRET-PROMPT-123";
    private readonly string root=Path.Combine(Path.GetTempPath(),"vibedotcom-private-sqlite-"+Guid.NewGuid().ToString("N"));
    public PrivacySqliteTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root,true);
    private sealed class Writer : IDisposable
    {
        private readonly IntPtr handle;
        internal Writer(string path) => Assert.Equal(0,SqliteSnapshot.Native.sqlite3_open_v2(path,out handle,6,IntPtr.Zero));
        internal void Exec(string sql) => Assert.Equal(0,sqlite3_exec(handle,sql,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero));
        public void Dispose() => Assert.Equal(0,SqliteSnapshot.Native.sqlite3_close(handle));
        [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,IntPtr callback,IntPtr argument,IntPtr error);
    }
    private static string Fingerprint(string path) => string.Join("|",new[] { "","-wal","-shm","-journal" }.Select(s=>
    {
        if(!File.Exists(path+s)) return "missing";
        using var file=new FileStream(path+s,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(file))+File.GetLastWriteTimeUtc(path+s).Ticks;
    }));

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void PrivateSqliteUsesMemoryAndLatestCommittedWalWithoutTouchingSources(int pageSize)
    {
        var path=Path.Combine(root,"source.db"); using var writer=new Writer(path);
        writer.Exec($"PRAGMA page_size={pageSize}; PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE state(value TEXT); INSERT INTO state VALUES('old'); PRAGMA wal_checkpoint(TRUNCATE);");
        writer.Exec($"UPDATE state SET value='{Secret}'; CREATE TABLE large(value TEXT); INSERT INTO large VALUES('{new string('x',200000)}');");
        var before=Fingerprint(path);
        using(var snapshot=new SqliteSnapshot(path,memoryOnly:true))
        {
            Assert.Equal(Secret,snapshot.Query("SELECT value FROM state").Single()[0]);
            Assert.Equal("200000",snapshot.Query("SELECT length(value) FROM large").Single()[0]);
            Assert.Null(typeof(SqliteSnapshot).GetField("directory",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot));
        }
        Assert.Equal(before,Fingerprint(path));
        writer.Exec("BEGIN; UPDATE state SET value='uncommitted';");
        using(var snapshot=new SqliteSnapshot(path,memoryOnly:true)) Assert.Equal(Secret,snapshot.Query("SELECT value FROM state").Single()[0]);
        writer.Exec("ROLLBACK;");
    }

    [Theory]
    [InlineData(PrivacyMode.Tasks)]
    [InlineData(PrivacyMode.Full)]
    public void PrivateAntigravityCatalogDoesNotStoreOrRestorePromptDerivedTitle(PrivacyMode mode)
    {
        var directory=Path.Combine(root,".gemini","antigravity"); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"conversation_summaries.db"); var now=DateTimeOffset.UtcNow;
        using var writer=new Writer(path);
        writer.Exec("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; PRAGMA user_version=3; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,parent_conversation_id TEXT,nesting_depth INTEGER,agent_name TEXT,workspace_uris TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_time TEXT,step_count INTEGER);");
        writer.Exec($"INSERT INTO conversation_summaries VALUES('conversation','{Secret}','',0,'{Secret}','[\"file:///C:/Users/private/proj\"]','CASCADE_RUN_STATUS_IDLE',0,0,'{now:O}','{now:O}',1);");
        var before=Fingerprint(path);
        using var tracker=new AgentTracker(new() { Home=root,PrivacyMode=mode,ScanInterval=TimeSpan.FromMilliseconds(1) });
        var agent=Assert.Single(tracker.Poll(now)); Assert.Equal("Antigravity",agent.Name); Assert.Empty(agent.Task);
        Assert.Equal(mode==PrivacyMode.Tasks ? "proj" : null,agent.Cwd); Assert.Empty(tracker.Warnings);
        Assert.Equal(before,Fingerprint(path));
        tracker.SetPrivacy(PrivacyMode.Full); tracker.SetPrivacy(PrivacyMode.Off);
        agent=Assert.Single(tracker.Poll(now.AddMilliseconds(5))); Assert.Equal("Antigravity",agent.Name); Assert.Null(agent.Cwd);
        Assert.DoesNotContain(Secret,JsonSerializer.Serialize(agent,StateJson.Options));
    }

    [Fact]
    public void SwitchingFromOffScrubsCachedAntigravityMetadata()
    {
        var directory=Path.Combine(root,".gemini","antigravity"); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"conversation_summaries.db"); var now=DateTimeOffset.UtcNow;
        using(var writer=new Writer(path))
        {
            writer.Exec("PRAGMA user_version=3; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,parent_conversation_id TEXT,nesting_depth INTEGER,agent_name TEXT,workspace_uris TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_time TEXT,step_count INTEGER);");
            writer.Exec($"INSERT INTO conversation_summaries VALUES('conversation','{Secret}','',0,'','[\"file:///C:/Users/private/proj\"]','CASCADE_RUN_STATUS_IDLE',0,0,'{now:O}','{now:O}',1);");
        }
        using var tracker=new AgentTracker(new() { Home=root,ScanInterval=TimeSpan.FromMilliseconds(1) });
        Assert.Equal(Secret,Assert.Single(tracker.Poll(now)).Name);
        foreach(var mode in new[] { PrivacyMode.Tasks,PrivacyMode.Full,PrivacyMode.Off })
        {
            tracker.SetPrivacy(mode); var agent=Assert.Single(tracker.Poll(now.AddMilliseconds(5)));
            Assert.Equal("Antigravity",agent.Name); Assert.DoesNotContain(Secret,JsonSerializer.Serialize(agent,StateJson.Options));
        }
    }

    [Fact]
    public void ActiveRollbackJournalAndInvalidDatabaseFailWithoutEchoingRawData()
    {
        var path=Path.Combine(root,"source.db"); using(var writer=new Writer(path)) writer.Exec("CREATE TABLE state(value TEXT);");
        File.WriteAllBytes(path+"-journal",[0xd9,0xd5,0x05,0xf9,0x20,0xa1,0x63,0xd7]);
        Assert.Throws<IOException>(()=>new SqliteSnapshot(path,memoryOnly:true));
        var exception=Assert.Throws<IOException>(()=>SqliteMemoryImage.Merge(System.Text.Encoding.UTF8.GetBytes(Secret),[]));
        Assert.DoesNotContain(Secret,exception.Message);
    }
}
