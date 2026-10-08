using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Media;
using Officelings.App;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoPackReaderTests
{
    [Fact]
    public void PackAdapterReadsFacingCellsFreezesCachesAndRejectsInvalidFrames()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"officelings-iso-reader-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.Copy(Path.Combine(SourceManifestIsoSheets.FindSourceDirectory(),"env","floors","floors.png"),Path.Combine(root,"floor.png"));
                File.WriteAllText(Path.Combine(root,"assets.json"),JsonSerializer.Serialize(new {version=1,projection="iso2to1",sheets=new[]
                {
                    new {id="fixture-floor",file="floor.png",cellW=64,cellH=32,anchor=new {x=32,y=16},clips=new[]
                    {
                        new {name="floor",direction="SE",row=0,column=0,frames=1},
                        new {name="floor",direction="SW",row=1,column=0,frames=1}
                    }}
                }}));
                var source=PackIsoSheetSource.LoadDirectory(root);
                var cell=source.GetCell("fixture-floor","floor:SE",0);
                Assert.True(cell.Image.IsFrozen); Assert.Equal(64,cell.Width); Assert.Equal(32,cell.Height);
                Assert.Equal(32,cell.AnchorX); Assert.Equal(16,cell.AnchorY);
                Assert.Same(cell,source.GetCell("fixture-floor","floor:SE",0));
                Assert.Equal(cell.Image,source.GetCell("fixture-floor","floor/se",0).Image);
                Assert.Throws<InvalidDataException>(()=>source.GetCell("fixture-floor","floor:se",1));
                Assert.Throws<InvalidDataException>(()=>source.GetCell("fixture-floor","floor",0));
            }
            catch(Exception e) { error=e; }
            finally { Directory.Delete(root,true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}