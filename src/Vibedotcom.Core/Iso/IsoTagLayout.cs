namespace Vibedotcom.Core.Iso;

public readonly record struct IsoTagBox(int X,int Y,int Width,int Height)
{
    public int Right => X+Width;
    public int Bottom => Y+Height;
    public bool Intersects(IsoTagBox other) => Width>0 && other.Width>0 && X<other.Right && Right>other.X && Y<other.Bottom && Bottom>other.Y;
}
public readonly record struct IsoTagRequest(string Id,int X,int Y,int Width,int Height);

// Integer outlines include the one-pixel border. Reuse relative positions first,
// then search upwards and sideways in ordinal ID order. No idle allocations.
public sealed class IsoTagLayout
{
    private readonly record struct Slot(int X,int Y,long Seen);
    private readonly Dictionary<string,Slot> previous=new(StringComparer.Ordinal);
    private readonly List<string> expired=[];
    private int[] order=[];
    private long generation;

    public void Place(ReadOnlySpan<IsoTagRequest> requests,ReadOnlySpan<IsoTagBox> bodies,IsoTagBox view,Span<IsoTagBox> result)
    {
        if(result.Length<requests.Length) throw new ArgumentException("Missing tag output slots.",nameof(result));
        result=result[..requests.Length]; result.Clear(); generation++;
        if(order.Length<requests.Length) Array.Resize(ref order,requests.Length);
        for(var i=0;i<requests.Length;i++)
        {
            var j=i; while(j>0 && string.CompareOrdinal(requests[order[j-1]].Id,requests[i].Id)>0) { order[j]=order[j-1]; j--; }
            order[j]=i;
        }
        // Reserve all still-valid old positions before placing new arrivals.
        for(var k=0;k<requests.Length;k++)
        {
            var i=order[k]; var r=requests[i];
            if(!previous.TryGetValue(r.Id,out var slot)) continue;
            var box=new IsoTagBox(r.X+slot.X,r.Y+slot.Y,r.Width,r.Height);
            if(box.Bottom<=r.Y && Clear(box,bodies,view,result)) result[i]=box;
        }
        for(var k=0;k<requests.Length;k++)
        {
            var i=order[k]; var r=requests[i];
            if(result[i].Width==0) result[i]=Find(r,bodies,view,result);
            var box=result[i];
            if(box.Width>0) previous[r.Id]=new(box.X-r.X,box.Y-r.Y,generation);
        }
        expired.Clear();
        foreach(var (id,slot) in previous) if(slot.Seen!=generation) expired.Add(id);
        foreach(var id in expired) previous.Remove(id);
    }
    private static bool Clear(IsoTagBox box,ReadOnlySpan<IsoTagBox> bodies,IsoTagBox view,ReadOnlySpan<IsoTagBox> tags)
    {
        if(box.Width<=0 || box.Height<=0 || box.X<view.X || box.Y<view.Y || box.Right>view.Right || box.Bottom>view.Bottom) return false;
        foreach(var body in bodies) if(box.Intersects(body)) return false;
        foreach(var tag in tags) if(box.Intersects(tag)) return false;
        return true;
    }
    private static IsoTagBox Find(IsoTagRequest r,ReadOnlySpan<IsoTagBox> bodies,IsoTagBox view,ReadOnlySpan<IsoTagBox> tags)
    {
        if(r.Width>view.Width || r.Height>view.Height) return default;
        var center=Math.Clamp(r.X-r.Width/2,view.X,view.Right-r.Width);
        for(var distance=0;distance<=view.Width;distance++)
        {
            var left=Column(center-distance,r,bodies,view,tags); if(left.Width>0) return left;
            if(distance>0) { var right=Column(center+distance,r,bodies,view,tags); if(right.Width>0) return right; }
        }
        // A saturated viewport cannot display every full plate. Leave this one
        // hidden rather than occluding a person or drawing outside the view.
        return default;
    }
    private static IsoTagBox Column(int x,IsoTagRequest r,ReadOnlySpan<IsoTagBox> bodies,IsoTagBox view,ReadOnlySpan<IsoTagBox> tags)
    {
        if(x<view.X || x+r.Width>view.Right) return default;
        var box=new IsoTagBox(x,Math.Min(r.Y-r.Height,view.Bottom-r.Height),r.Width,r.Height);
        while(box.Y>=view.Y)
        {
            var y=box.Y;
            foreach(var body in bodies) if(box.Intersects(body)) y=Math.Min(y,body.Y-box.Height);
            foreach(var tag in tags) if(box.Intersects(tag)) y=Math.Min(y,tag.Y-box.Height);
            if(y==box.Y) return box;
            box=box with {Y=y};
        }
        return default;
    }
}
