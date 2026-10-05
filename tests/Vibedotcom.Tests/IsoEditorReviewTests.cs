using System.Runtime.ExceptionServices;
using System.Threading;
using Xunit;

namespace Vibedotcom.Tests;

[Collection("Iso assets")]
public sealed class IsoEditorReviewTests
{
    [Theory]
    [InlineData("P0")] [InlineData("P0-cache")] [InlineData("P0-equivalence")]
    [InlineData("R01")] [InlineData("R02")] [InlineData("R03")] [InlineData("R03-count")] [InlineData("R06")]
    [InlineData("R08")] [InlineData("R09")] [InlineData("R10")] [InlineData("R11")]
    [InlineData("R13")] [InlineData("R14")] [InlineData("R16")] [InlineData("R17")]
    [InlineData("R18")] [InlineData("R19")] [InlineData("edwire")]
    public void ReviewRegression(string rule)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { IsoEditorReviewCases.Run(rule); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}