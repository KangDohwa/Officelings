using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoTagLayoutTests
{
    private static readonly IsoTagBox View=new(0,0,720,448);
    private static IsoTagRequest[] Crowd() => Enumerable.Range(0,12).Select(i=>new IsoTagRequest($"agent-{i:00}",340,180,64,16)).ToArray();
    private static void Safe(IsoTagRequest[] requests,IsoTagBox[] bodies,IsoTagBox[] tags,IsoTagBox view)
    {
        for(var i=0;i<tags.Length;i++)
        {
            var tag=tags[i]; Assert.True(tag.Width>0);
            Assert.True(tag.X>=view.X && tag.Y>=view.Y && tag.Right<=view.Right && tag.Bottom<=view.Bottom);
            Assert.True(tag.Bottom<=requests[i].Y);
            Assert.All(bodies,body=>Assert.False(tag.Intersects(body)));
            for(var j=0;j<i;j++) Assert.False(tag.Intersects(tags[j]));
        }
    }
    [Fact]
    public void TwelveArrivalsAvoidEveryBodyAndBorderAndOtherTags()
    {
        var requests=Crowd(); IsoTagBox[] bodies=[new(280,95,40,160),new(326,182,34,66),new(360,100,40,150)];
        var output=new IsoTagBox[requests.Length]; new IsoTagLayout().Place(requests,bodies,View,output); Safe(requests,bodies,output,View);
    }
    [Fact]
    public void FirstPlacementIsIndependentOfIncomingOrder()
    {
        var requests=Crowd(); var reversed=requests.Reverse().ToArray(); var first=new IsoTagBox[12]; var second=new IsoTagBox[12];
        new IsoTagLayout().Place(requests,[],View,first); new IsoTagLayout().Place(reversed,[],View,second);
        Assert.Equal(first,second.Reverse());
    }
    [Fact]
    public void NewLowerIdReservesExistingSlotsAndMovementKeepsOffsets()
    {
        var layout=new IsoTagLayout(); IsoTagRequest[] initial=[new("b",300,180,60,16),new("c",300,180,60,16)];
        var before=new IsoTagBox[2]; layout.Place(initial,[],View,before);
        IsoTagRequest[] arrivals=[new("a",301,181,60,16),new("b",301,181,60,16),new("c",301,181,60,16)];
        var after=new IsoTagBox[3]; layout.Place(arrivals,[],View,after);
        Assert.Equal(before[0] with {X=before[0].X+1,Y=before[0].Y+1},after[1]);
        Assert.Equal(before[1] with {X=before[1].X+1,Y=before[1].Y+1},after[2]); Safe(arrivals,[],after,View);
    }
    [Fact]
    public void BodyEnteringAnOldSlotForcesASafePlacement()
    {
        var layout=new IsoTagLayout(); IsoTagRequest[] requests=[new("a",340,180,64,16)]; var output=new IsoTagBox[1];
        layout.Place(requests,[],View,output); var old=output[0];
        IsoTagBox[] bodies=[old]; layout.Place(requests,bodies,View,output); Safe(requests,bodies,output,View);
    }
    [Theory]
    [InlineData(0)] [InlineData(719)]
    public void EdgeTagsRemainFullyInsideTheView(int x)
    {
        IsoTagRequest[] requests=[new("a",x,16,64,16)]; var output=new IsoTagBox[1];
        new IsoTagLayout().Place(requests,[],View,output); Safe(requests,[],output,View);
    }
    [Fact]
    public void WarmedIdlePlacementDoesNotAllocateOrJitter()
    {
        var layout=new IsoTagLayout(); var requests=Crowd(); var output=new IsoTagBox[12];
        for(var i=0;i<30;i++) layout.Place(requests,[],View,output);
        var expected=output.ToArray(); var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++) layout.Place(requests,[],View,output);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before); Assert.Equal(expected,output);
    }
    [Fact]
    public void ImpossibleViewportDoesNotCoverPeopleOrEscapeBounds()
    {
        IsoTagRequest[] requests=[new("a",10,10,64,16)]; var output=new IsoTagBox[1];
        new IsoTagLayout().Place(requests,[],new(0,0,20,20),output); Assert.Equal(default,output[0]);
    }
}
