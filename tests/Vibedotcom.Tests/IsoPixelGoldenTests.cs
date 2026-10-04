using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Vibedotcom.App;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoPixelGoldenTests
{
    // Reviewed source-art snapshots after contact shadows and native cache
    // sampling were fixed. Goldens use raw Pbgra32 bytes, never PNG encoders.
    [Theory]
    [InlineData(0, 0, 1, "703B3A28259D1EEB0A8113971F739F0B0D8BEF6749FBA55AAC1672042B4EA16E")]
    [InlineData(0, 0, 2, "00B5B2A9CC07BC64AA7C9C35386FAA6E0D85604952FC982FF9D824AA074BBEB9")]
    [InlineData(0, 0, 3, "A6E2A8E7ED196E22F8EA7DCF95300BF12B941F07836AF214908942E08323B96F")]
    [InlineData(0, 1, 1, "3B753D4A88F163C3AE8E454CB6253249027668563FA94F829973379BFEA76CB7")]
    [InlineData(0, 1, 2, "C35721FA0374CF5B9679BA69409272BF2E56FFCB81DBFC78EAA250E109BFACAC")]
    [InlineData(0, 1, 3, "BDDB052309D8EE071B8ED70A993EDE6934965B7AD478ED4ED0BC143E9AEAC4FE")]
    [InlineData(0, 2, 1, "A3D4EAF77971543A0B2B603F34537D8E96F737A9F2923CFB72205DE4D34F9E50")]
    [InlineData(0, 2, 2, "4FDF61AE2B447A06CD8C56A89A08F83D1E27CA82384C5C42B76735BEC9F5D23A")]
    [InlineData(0, 2, 3, "F9ABAB51EA0A3D30FF4DFB1A59A1935EF73E4298E311B17207D26D64B620C1C9")]
    [InlineData(0, 3, 1, "8396EFA889C56CE0190155F0501C3059A1BBEF048EBA5C8A982DB2A5BAF234C3")]
    [InlineData(0, 3, 2, "FB77E011274799BFFECCE236DB470570C78606B71B1BD257138EAA5274E95B07")]
    [InlineData(0, 3, 3, "7553F205FB5396AAC71C0DF928465AB38A2CF5B9DC285F014CE2A8BB56EED29C")]
    [InlineData(12, 0, 1, "306C28BAB3582B4DA5650CDBCE277782F2F36392691A2274CB03526DEF70FD79")]
    [InlineData(12, 0, 2, "1482C9AB83DFAF427AE90B0B87DB6F860B68BBF35A55A6A96F7B0C2AD74D2C1A")]
    [InlineData(12, 0, 3, "672ADD0A243B9D20541C2F75016554A4A0111AE4240FDC2E1EBB3AEF0A48B7EA")]
    [InlineData(12, 1, 1, "9345B8103EE98F8F3888D677690687BF711AEE3728713A7A40EF01EA84896538")]
    [InlineData(12, 1, 2, "7D74D540A38D56B1591979636CA09F88CD1019D2DA4AF11E428438E929B341DB")]
    [InlineData(12, 1, 3, "91923D7D3DC56A1B93A5AA25C2AC8889DFDC2EEA03423AB3181663D3E8D189D8")]
    [InlineData(12, 2, 1, "2F1E615F4C3A2E370EBC076A01569F5AAAEC367B57199F10405BABA8FD9725FD")]
    [InlineData(12, 2, 2, "B63129245177113DD53219D958248C2C7871E06EA4E1FC1E657937C266A13817")]
    [InlineData(12, 2, 3, "5BFFFB76C8D9806C6E4862B6DC2225F9F93C3AB9278B4D89C3ADD01EF20E9B18")]
    [InlineData(12, 3, 1, "31A65EECF664BA8312AB8AA104AE15402709B6F82AD65D24B0B09D6329332523")]
    [InlineData(12, 3, 2, "BBB254EA674DD13951D620A12B328601BE5DEA8D68E01937E243AFB4AA67F696")]
    [InlineData(12, 3, 3, "3A9E68E95ECF443C4C2A1543A9899D81FE1A856DBBE0AC3D425142D5C8621484")]
    public void ReviewedEmptyAndTwelveAgentFramesStayPixelStable(int count, int phase, int zoom, string expected)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
                var people = FakeIsoOffice.Agents(count); var office = new FakeIsoOffice(source, people);
                office.SetPhase(phase);
                var view = new IsoView(office, source, Path.Combine(Path.GetTempPath(), "vibedotcom-iso-golden-empty-brand"));
                view.SetZoom(zoom); view.Update(people, DateTimeOffset.Parse("2026-10-04T00:00:00Z"), 0);
                var bitmap = Vibedotcom.App.Program.RenderBitmap(view, 672 * zoom, 416 * zoom);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(pixels)));
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

