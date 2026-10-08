// This executable has no Windhawk linkage and excludes the entire HWND host.
#define OFFICELINGS_TEST
#define GdipAlloc VdcTestGdiAlloc
#include "officelings.wh.cpp"
#undef GdipAlloc
#include <iostream>
#include <functional>
#include <climits>
#include <winioctl.h>

namespace {
bool failGdiWrapperAllocation=false;
size_t gdiWrapperAllocationCalls=0;
}
namespace Gdiplus::DllExports {
extern "C" VOID* WINGDIPAPI GdipAlloc(size_t);
extern "C" VOID* WINGDIPAPI VdcTestGdiAlloc(size_t n){++gdiWrapperAllocationCalls;return failGdiWrapperAllocation?nullptr:GdipAlloc(n);}
}

using namespace vdc;
namespace {
int checks=0;
void Check(bool condition,const char* message){++checks;Require(condition,message);}
void Reject(const std::function<void()>& fn){bool rejected=false;try{fn();}catch(const Error&){rejected=true;}Check(rejected,"Expected rejection");}
std::string Replace(std::string s,const std::string& from,const std::string& to){auto p=s.find(from);Require(p!=s.npos,"Test mutation target missing");s.replace(p,from.size(),to);return s;}
std::string PoseField(std::string bytes,const std::string& name,const std::string& value){auto j=Parser(bytes).Parse();const auto& pose=j.At("agents").array.front().At("pose");auto it=pose.object.find(name);if(it==pose.object.end())bytes.insert(pose.begin+1,"\""+name+"\":"+value+",");else bytes.replace(it->second.begin,it->second.end-it->second.begin,value);return bytes;}
std::string ObjectField(std::string bytes,const Json& object,const std::string& name,const std::string& value){auto it=object.object.find(name);if(it==object.object.end())bytes.insert(object.begin+1,"\""+name+"\":"+value+",");else bytes.replace(it->second.begin,it->second.end-it->second.begin,value);return bytes;}
std::string RootField(std::string bytes,const std::string& name,const std::string& value){auto j=Parser(bytes).Parse();return ObjectField(std::move(bytes),j,name,value);}
#include "../tests/windhawk/hardening_tests.inl"
double CpuSeconds(){FILETIME a{},b{},k{},u{};GetProcessTimes(GetCurrentProcess(),&a,&b,&k,&u);ULARGE_INTEGER x{},y{};x.LowPart=k.dwLowDateTime;x.HighPart=k.dwHighDateTime;y.LowPart=u.dwLowDateTime;y.HighPart=u.dwHighDateTime;return (x.QuadPart+y.QuadPart)/1e7;}
bool Different(Bitmap& a,Bitmap& b,Gdiplus::Rect r){
    Require(a.GetWidth()==b.GetWidth()&&a.GetHeight()==b.GetHeight(),"Compared render dimensions");BitmapData x{},y{};
    Require(a.LockBits(&r,ImageLockModeRead,PixelFormat32bppPARGB,&x)==Ok,"Lock first render");
    if(b.LockBits(&r,ImageLockModeRead,PixelFormat32bppPARGB,&y)!=Ok){a.UnlockBits(&x);throw Error("Lock second render");}
    bool different=false;for(int row=0;row<r.Height;++row)if(memcmp(static_cast<BYTE*>(x.Scan0)+row*x.Stride,static_cast<BYTE*>(y.Scan0)+row*y.Stride,r.Width*4)!=0){different=true;break;}
    a.UnlockBits(&x);b.UnlockBits(&y);return different;
}
bool Different(Bitmap& a,Bitmap& b){return Different(a,b,Gdiplus::Rect(0,0,a.GetWidth(),a.GetHeight()));}
void NativeFaultCleanupTests(){
    struct CountedOwner {int& cleaned;~CountedOwner(){++cleaned;}};
    struct CountedCom {int& released;ULONG Release() noexcept {++released;return 0;}};
    struct NoAccessPage {void* value=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_NOACCESS);~NoAccessPage(){if(value)VirtualFree(value,0,MEM_RELEASE);}};
    NoAccessPage page;Require(page.value!=nullptr,"Allocate owned inaccessible AV regression page");
    Check(Native([]{return 37;})==37,"Native leaf preserves successful API return values");
    for(bool actualAv:{false,true}){
        int cleaned=0,released=0;CountedCom acquired{released};bool reached=false,returned=false,caught=false;
        try{
            auto heapBitmap=MakeGdi<Bitmap>(4,4,PixelFormat32bppPARGB);
            SafeGdi<Bitmap> stackBitmap(4,4,PixelFormat32bppPARGB);
            Check(heapBitmap->GetLastStatus()==Ok&&stackBitmap.get().GetLastStatus()==Ok,"Real bitmap owners exist before native fault");
            CountedOwner owner{cleaned};ComSlot<CountedCom> slot;slot.value=&acquired;
            reached=true;Native([&]{
                if(actualAv)*static_cast<volatile BYTE*>(page.value)=1;
                else RaiseException(EXCEPTION_ACCESS_VIOLATION,0,0,nullptr);
                return 0;
            });returned=true;
        }catch(const Error&){caught=true;}
        Check(reached&&caught&&!returned,actualAv?"Native PAGE_NOACCESS write converts to a caught C++ Error":"Native RaiseException converts to a caught C++ Error");
        Check(cleaned==1,"Native fault unwinds the counted outer RAII owner exactly once");
        Check(released==1,"Native fault releases an already acquired COM out-parameter exactly once");
    }
    auto recovery=MakeGdi<Bitmap>(4,4,PixelFormat32bppPARGB);Color expected(255,17,29,43),actual;
    Check(recovery->SetPixel(1,1,expected)==Ok&&recovery->GetPixel(1,1,&actual)==Ok&&actual.GetValue()==expected.GetValue(),"Real bitmap construction and pixels remain usable after native fault cleanup");
}
void RelativePathGrammarTests(){
    // No configured root or drive lookup participates in these grammar tests.
    for(const std::string& path:std::vector<std::string>{"atlas.png","sheets/atlas.png","sheets\\nested/atlas.png","sheets/pixel office.png","sheets/한글.png","sheets/.hidden.png"}){
        auto expected=Wide(path);std::replace(expected.begin(),expected.end(),L'/',L'\\');Check(ValidateRelativeSheetPath(path)==expected,"F11 valid relative sheet grammar normalizes separators");
    }
    for(const std::string& path:std::vector<std::string>{"","/atlas.png","\\atlas.png","C:/atlas.png","C:atlas.png","//server/share/atlas.png","\\\\server\\share\\atlas.png","https://server/atlas.png","./atlas.png","../atlas.png","sheets/./atlas.png","sheets/../atlas.png","sheets//atlas.png","sheets\\\\atlas.png","sheets/","sheets\\","sheets./atlas.png","sheets /atlas.png","sheets/atlas.png.","sheets/atlas.png ","sheets/atlas.png:stream",std::string("sheets/\0atlas.png",16),std::string("sheets/\xC0\xAF.png")})Reject([&]{ValidateRelativeSheetPath(path);});
}
void HostPolicyTests(){
    Check(HostRetryDelay(0)==1000&&HostRetryDelay(1)==2000&&HostRetryDelay(4)==16000,"F7 host retry exponential backoff");
    Check(HostRetryDelay(5)==30000&&HostRetryDelay(6)==30000&&HostRetryDelay(std::numeric_limits<unsigned>::max())==30000,"F7 host retry caps delay and shift count");
    Check(HoverEligible(true,false,false,true),"F8 visible current-desktop office permits hover");
    Check(!HoverEligible(false,false,false,true)&&!HoverEligible(true,true,false,true)&&!HoverEligible(true,false,true,true)&&!HoverEligible(true,false,false,false),"F8 hidden, minimized, cloaked or other-desktop office suppresses hover");
    auto extent=ClampClientExtent(4096,2048);Check(extent.cx==4096&&extent.cy==2048,"F9 exact maximum client extent");
    extent=ClampClientExtent(0,0);Check(extent.cx==1&&extent.cy==1,"F9 zero client extent clamps to one");
    extent=ClampClientExtent(INT_MIN,INT_MAX);Check(extent.cx==1&&extent.cy==2048,"F9 signed extent extremes clamp safely");
    extent=ClampClientExtent(5000,5000);Check(extent.cx==4096&&extent.cy==2048,"F9 oversized actual client extent clamps to buffer limits");
    auto same=[](const RECT& a,const RECT& b){return a.left==b.left&&a.top==b.top&&a.right==b.right&&a.bottom==b.bottom;};
    RECT negative{-300,40,-100,240},negativeWork{-1920,0,0,1080};Check(same(RecoverOffscreenRect(negative,negativeWork,true),negative),"F9 negative-coordinate monitor preserves a visible office");
    RECT partial{-50,25,150,225},work{0,0,1920,1080};Check(same(RecoverOffscreenRect(partial,work,true),partial),"F9 partially offscreen user placement is preserved");
    RECT lost{-2400,-300,-2200,-100};auto recovered=RecoverOffscreenRect(lost,negativeWork,false);
    Check(!same(recovered,lost)&&recovered.right-recovered.left==200&&recovered.bottom-recovered.top==200&&recovered.left>=negativeWork.left&&recovered.top>=negativeWork.top&&recovered.right<=negativeWork.right&&recovered.bottom<=negativeWork.bottom,"F9 fully offscreen office returns to nearest negative-coordinate monitor without resizing");
    RECT oversized{5000,5000,7200,6200};recovered=RecoverOffscreenRect(oversized,work,false);
    Check(recovered.right-recovered.left==2200&&recovered.bottom-recovered.top==1200&&recovered.left<work.right&&recovered.right>work.left&&recovered.top<work.bottom&&recovered.bottom>work.top,"F9 oversized offscreen recovery preserves size and intersects monitor");
    Check(DpiPixels(12,96)==12&&DpiPixels(12,144)==18&&DpiPixels(12,192)==24,"F10 100/150/200 percent DPI dimensions");
    Check(DpiPixels(13,144)==20&&DpiPixels(-13,144)==-20,"F10 DPI rounding preserves signed metrics");
    Check(DpiPixels(16,0)==8&&DpiPixels(16,std::numeric_limits<UINT>::max())==160,"F10 DPI input clamps at 48 and 960");
}
void AdditiveSchemaTests(const std::string& bytes,const Snapshot& original){
    auto unknown=PoseField(bytes,"futurePose","{\"nested\":[1,null,true],\"state\":false}");
    auto tree=Parser(unknown).Parse();unknown=ObjectField(unknown,tree.At("agents").array.front().At("agent"),"futureAgent","{\"name\":17}");
    unknown=RootField(unknown,"futureField","{\"nested\":[1,null,true]}");
    // A v4 fixture can contain the new -1 desk sentinel. Use assigned desks for
    // the v3 compatibility case, whose older nonnegative bounds still apply.
    auto legacy=unknown;tree=Parser(legacy).Parse();
    for(auto it=tree.At("agents").array.rbegin();it!=tree.At("agents").array.rend();++it){const auto& desk=it->At("pose").At("desk");if(desk.At("x").Int(-1,128)<0||desk.At("y").Int(-1,128)<0)legacy.replace(desk.begin,desk.end-desk.begin,"{\"x\":0,\"y\":0}");}
    for(int version:{3,4,5})for(const Snapshot* previous:{static_cast<const Snapshot*>(nullptr),&original}){
        auto parsed=ParseSnapshot(RootField(version==3?legacy:unknown,"schemaVersion",std::to_string(version)),previous);
        Check(parsed.schema==version&&parsed.staticKey==original.staticKey&&parsed.agents.size()==original.agents.size(),"Supported schemas 3-5, cold and cached");
        const auto& a=parsed.agents.front();const auto& b=original.agents.front();
        Check(a.id==b.id&&a.name==b.name&&a.state==b.state&&a.action==b.action&&a.x==b.x&&a.y==b.y&&a.frame.sheet==b.frame.sheet&&a.frame.x==b.frame.x&&a.frame.y==b.frame.y&&a.frame.w==b.frame.w&&a.frame.h==b.frame.h,"Unknown root, agent and pose fields are ignored");
    }
    for(const std::string& version:std::vector<std::string>{"0","2","6","2147483647","2147483648","3.5","\"3\"","null","true"})for(const Snapshot* previous:{static_cast<const Snapshot*>(nullptr),&original})Reject([&]{ParseSnapshot(RootField(bytes,"schemaVersion",version),previous);});
}
Snapshot RegressionScene(const Snapshot& original){
    auto s=original;s.schema=4;s.w=8;s.h=8;s.doorX=0;s.doorY=7;s.cozy=true;s.zones.clear();s.furniture.clear();s.agents.clear();s.pets.clear();s.hasAmbient=false;
    auto floor=std::find_if(s.types.begin(),s.types.end(),[](const auto& t){return t.second.kind=="floor";});Require(floor!=s.types.end(),"Regression floor tile");s.tiles.assign(64,floor->first);
    s.staticKey=Hash("windhawk-regression-room"+s.rawAssets);return s;
}
Agent RegressionAgent(const Snapshot& s,const std::string& id,int x,int y){
    Agent a;a.id=id;a.ordinal=Wide(id);a.name=id;a.displayName="A";a.provider="codex";a.x=x;a.y=y;a.dx=x;a.dy=y;a.visible=true;a.state="idle";a.action="idle";a.facing="down";
    a.frame=SelectFrame(s.sheets.at("fallback"),"idle",a.facing,0);a.frame.ax=8;a.frame.ay=24;a.frame.scale=1;a.frame.ox=0;a.frame.oy=0;return a;
}
void StateOnlyVisualTests(Renderer& renderer,const Snapshot& original){
    auto working=RegressionScene(original);auto parent=RegressionAgent(working,"regression-parent",2,4),child=RegressionAgent(working,"regression-child",5,4);
    child.parent=parent.id;child.sub=true;child.state="working";working.agents={parent,child};auto idle=working;idle.agents.back().state="idle";
    auto before=renderer.Render(working,160,160,1,false,false,0),after=renderer.Render(idle,160,160,1,false,false,0);
    Check(VisualKey(working,false,0)!=VisualKey(idle,false,0),"F6 state-only change invalidates VisualKey");
    Check(Different(*before,*after),"F6 state-only relationship change alters real pixels");
    auto reporting=idle;reporting.agents.back().state="reporting";auto report=renderer.Render(reporting,160,160,1,false,false,0);
    Check(VisualKey(reporting,false,0)!=VisualKey(idle,false,0)&&Different(*report,*after),"F6 reporting-only change updates key and relationship pixels");
}
void DeskNameplateTests(Renderer& renderer,const Snapshot& original){
    auto s=RegressionScene(original);auto owner=RegressionAgent(s,"desk-owner",3,4);
    Furniture desk;desk.id="regression-desk";desk.ordinal=Wide(desk.id);desk.kind="desk";desk.x=2;desk.y=2;desk.w=3;desk.h=3;desk.owner=owner.id;
    s.furniture={desk};s.agents={owner};renderer.tagMode=TagMode::Always;
    for(const std::string& state:std::vector<std::string>{"working","thinking","waiting","idle","reporting","gone"})for(double distance:{0.0,1.5,1.501}){
        auto& a=s.agents.front();a.state=state;a.x=owner.dx+distance;
        bool away=!(distance<=1.5&&(state=="working"||state=="thinking"||state=="waiting"));
        Check(OwnerAway(a)==away,"Visible away reservation follows work-state/distance boundary");
        renderer.Render(s,160,160,1,false,false,0);
        Check(std::any_of(renderer.tagHits.begin(),renderer.tagHits.end(),[&](const auto& h){return h.key==TagKey(desk.id,true);})==away,"Actual desk tag/hit shares away rule");
        for(const auto& phase:{"turn","lower","seated","raise"}){a.seatPhase=phase;Check(!OwnerAway(a),"Seat phases suppress duplicate desk reservation");}a.seatPhase.clear();
        a.visible=false;Check(!OwnerAway(a),"Departed invisible owner has no reservation");a.visible=true;
    }
    renderer.tagMode=TagMode::Hover;
}
void NameTagModeTests(Renderer& renderer,const Snapshot& original){
    auto s=RegressionScene(original);s.schema=5;auto a=RegressionAgent(s,"tags-a",3,4);auto b=RegressionAgent(s,"tags-b",4,4);a.displayName="Fourteen elements plus more";s.agents={a,b};
    Check(NormalizeTagMode(L"bad")==TagMode::Hover&&NormalizeTagMode(L"")==TagMode::Hover&&NormalizeTagMode(L"icon")==TagMode::Icon&&NormalizeTagMode(L"always")==TagMode::Always,"Tag mode normalization");
    Check(CompactName(L"123456789012345")==L"12345678901234\u2026","Fourteen elements plus ellipsis");
    Check(CompactName(L"1234567890123e\u0301x")==L"1234567890123e\u0301\u2026","Combining mark remains with final element");
    Check(CompactName(L"1234567890123\U0001F600x")==L"1234567890123\U0001F600\u2026","Surrogate pair remains complete");
    Check(CompactName(L"1234567890123\u1100\u1161\u11A8x")==L"1234567890123\u1100\u1161\u11A8\u2026","Decomposed Hangul remains one text element");
    Check(CompactName(L"\u030112345678901234")==L"\u03011234567890123\u2026","Leading combining mark counts as its own element");
    Check(CompactName(L"1234567890123\U0001F469\u200D\U0001F4BBx")==L"1234567890123\U0001F469\u200D\U0001F4BB\u2026","ZWJ emoji remains one text element");
    TagHover fade;auto key=TagKey(a.id);fade.Update(key,0);Check(fade.Opacity(key)==0,"Hover starts transparent");fade.Advance(60);Check(fade.Opacity(key)==.5,"120ms fade in midpoint");fade.Advance(120);Check(fade.Opacity(key)==1,"120ms fade in complete");fade.Update(0,120);fade.Advance(180);Check(fade.Opacity(key)==1,"60ms leave grace");fade.Advance(280);Check(std::abs(fade.Opacity(key)-.5)<1e-6,"200ms fade out midpoint");fade.Advance(380);Check(fade.Opacity(key)==0&&!fade.Animating(),"Fade expires without storage growth");
    renderer.tagMode=TagMode::Hover;renderer.hover.Clear();auto hidden=renderer.Render(s,160,160,1,false,false,0);Check(renderer.tagHits.empty(),"Default hover has no idle plates");
    renderer.tagMode=TagMode::Icon;auto icon=renderer.Render(s,160,160,1,false,false,0);Check(Different(*hidden,*icon)&&renderer.tagHits.size()==2,"Icon mode renders two provider anchors");
    renderer.hover.Update(key,0);renderer.hover.Advance(120);auto full=renderer.Render(s,160,160,1,false,false,0);Check(Different(*icon,*full)&&std::count_if(renderer.tagHits.begin(),renderer.tagHits.end(),[](const auto& h){return h.plate;})==1,"Hover reveals only selected icon plate");
    auto plate=std::find_if(renderer.tagHits.begin(),renderer.tagHits.end(),[](const auto& h){return h.plate;});Check(plate!=renderer.tagHits.end()&&plate->bounds.h==14,"V5 plate height is fourteen world pixels");
    Check(std::any_of(renderer.tagHits.begin(),renderer.tagHits.end(),[&](const auto& h){return !h.plate&&h.key==key&&h.bounds.h>14;}),"Incumbent hit joins body and plate gap");
    renderer.tagMode=TagMode::Always;renderer.Render(s,160,160,1,false,false,0);Check(std::count_if(renderer.tagHits.begin(),renderer.tagHits.end(),[](const auto& h){return h.plate;})==2,"Always reveals both plates");
    std::vector<Bounds> plates;for(const auto& h:renderer.tagHits)if(h.plate)plates.push_back(h.bounds);Check(!Intersects(plates[0],plates[1]),"Only colliding plates stack upward");
    renderer.tagMode=TagMode::Hover;renderer.hover.Clear();
}
void V5ContractTests(const std::string& bytes,const Snapshot& original){
    auto input=PoseField(bytes,"seatPhase","\"raise\"");input=PoseField(input,"seatPhaseFrame","1");input=PoseField(input,"seatAnchor","{\"x\":40,\"y\":80}");input=PoseField(input,"activitySpotId","\"lounge-demo\"");input=PoseField(input,"activityPartnerId","\"partner-demo\"");
    for(const Snapshot* previous:{static_cast<const Snapshot*>(nullptr),&original}){auto s=ParseSnapshot(input,previous);auto& a=s.agents.front();Check(a.seat&&a.seatX==40&&a.seatY==80&&a.seatPhase=="raise"&&a.seatPhaseFrame==1&&a.activitySpot=="lounge-demo"&&a.activityPartner=="partner-demo","V5 optional metadata reads cold and cached");Check(a.frame.ox==original.agents.front().frame.ox&&a.frame.oy==original.agents.front().frame.oy,"Supplied seated offsets are not reapplied");}
    for(const auto& bad:{"2","-1","1.5","\"1\""})Reject([&]{ParseSnapshot(PoseField(bytes,"seatPhaseFrame",bad),&original);});
    Reject([&]{ParseSnapshot(PoseField(bytes,"seatPhase","\"future\""));});Reject([&]{ParseSnapshot(PoseField(bytes,"seatAnchor","{\"x\":1e99,\"y\":0}"));});
}
void AmbientContractTests(const std::string& bytes,const Snapshot& original){
    const std::string ambient="{\"windowView\":\"snow\",\"weather\":\"snow\",\"stringLightsOn\":true,\"winterDecor\":true,\"localTime\":\"2026-12-12T22:34:56+09:00\"}";
    auto root=Parser(bytes).Parse();const auto& frame=root.At("agents").array.front().At("pose").At("sprite");auto sprite=bytes.substr(frame.begin,frame.end-frame.begin);
    auto pet="[{\"id\":\"test-pet\",\"sheetId\":\""+original.agents.front().frame.sheet+"\",\"x\":3,\"y\":4,\"action\":\"idle\",\"facing\":\"down\",\"sprite\":"+sprite+"}]";
    auto input=RootField(RootField(bytes,"ambient",ambient),"pets",pet);
    for(const Snapshot* previous:{static_cast<const Snapshot*>(nullptr),&original}){auto s=ParseSnapshot(input,previous);Check(s.hasAmbient&&s.ambient.view=="snow"&&s.ambient.local.wHour==22&&s.ambient.local.wMinute==34&&s.ambient.lights&&s.ambient.winter,"Ambient uses supplied civil time with UTC offset");Check(s.pets.size()==1&&s.pets[0].id=="test-pet"&&s.pets[0].frame.x==original.agents.front().frame.x,"Supplied pet frame is authoritative");auto absent=ParseSnapshot(RootField(RootField(bytes,"ambient","null"),"pets","null"),&s);Check(!absent.hasAmbient&&absent.pets.empty(),"Omitted optional ambient/pets never leak from cached state");}
    for(const auto& view:{"day","sunset","night","rain","snow"})Check(ParseSnapshot(RootField(bytes,"ambient",Replace(ambient,"snow",view))).ambient.view==view,"Five ambient views accepted");
    Reject([&]{ParseSnapshot(RootField(bytes,"ambient",Replace(ambient,"snow","future")));});
    Reject([&]{ParseSnapshot(RootField(bytes,"ambient",Replace(ambient,"true","3")));});
    Reject([&]{ParseSnapshot(RootField(bytes,"ambient",Replace(ambient,"2026-12-12","2026-02-30")));});
    Reject([&]{ParseSnapshot(RootField(bytes,"pets",Replace(pet,"\"x\":3","\"x\":1e99")));});
    Reject([&]{ParseSnapshot(RootField(bytes,"pets","{}"));});
    Reject([&]{ParseSnapshot(RootField(bytes,"pets",Replace(pet,"\"sheetId\":\""+original.agents.front().frame.sheet+"\"","\"sheetId\":\"missing\"")));});
    for(int hour:{6,7,16,17,18,19,22}){SYSTEMTIME local{2026,12,0,12,static_cast<WORD>(hour),34,56,0},utc{};FILETIME ft{};Require(TzSpecificLocalTimeToSystemTime(nullptr,&local,&utc)&&SystemTimeToFileTime(&utc,&ft),"Synthetic civil clock");ULARGE_INTEGER ticks{};ticks.LowPart=ft.dwLowDateTime;ticks.HighPart=ft.dwHighDateTime;double time=ticks.QuadPart/1e7-11644473600.0;auto sample=SampleAmbient(time);
        Check(sample.view==(hour>=7&&hour<17?"day":hour>=17&&hour<19?"sunset":"night")&&sample.lights==(hour>=17||hour<7),"Local day/sunset/night and light boundaries");sample=SampleAmbient(time,"auto",true);Check(sample.view=="snow"&&sample.winter,"Local December weather/decor without network");}
}
void LifeVisualTests(Renderer& renderer,const Snapshot& original){
    auto s=RegressionScene(original);s.schema=5;auto a=RegressionAgent(s,"life",3,4);s.agents={a};renderer.tagMode=TagMode::Hover;renderer.hover.Clear();
    auto standing=renderer.HitBoxes(s,160,160,1,false,0).front().first;s.agents[0].action="beanbag";auto reclining=renderer.HitBoxes(s,160,160,1,false,0).front().first;
    Check(reclining.w==standing.h&&reclining.h==standing.w,"Reclining opaque hit bounds rotate ninety degrees");Check(std::abs(reclining.x+reclining.w/2-(a.x+.5)*16-16)<.51&&std::abs(reclining.y+reclining.h/2-((a.y+1)*16-4)-16)<.51,"Reclining centre uses supplied contact plus camera");
    auto lying=renderer.Render(s,160,160,1,false,false,0);s.agents[0].action="idle";auto idle=renderer.Render(s,160,160,1,false,false,0);Check(Different(*lying,*idle),"Beanbag body pixels rotate with the hit bounds");
    s.agents[0].action="reading";auto reading=renderer.Render(s,160,160,1,false,false,0);Check(Different(*reading,*idle),"Reading has a visible book prop");
    s.agents[0]=a;s.agents[0].state="gone";s.agents[0].action="walking";s.agents[0].opacity=.5;auto walking=renderer.Render(s,160,160,1,false,false,0);Check(renderer.HitBoxes(s,160,160,1,false,0).size()==1&&Different(*walking,*idle),"Gone still renders the supplied walk/fade");
    s.agents[0].opacity=0;Check(renderer.HitBoxes(s,160,160,1,false,0).empty(),"Completed fade has no hover body");s.agents[0].visible=false;renderer.tagMode=TagMode::Always;renderer.Render(s,160,160,1,false,false,0);Check(renderer.tagHits.empty(),"Invisible departed agent has no plate");renderer.tagMode=TagMode::Hover;
    s.agents.clear();Pet p;p.id="life-pet";p.ordinal=Wide(p.id);p.x=3;p.y=4;p.frame=a.frame;s.pets={p};auto pets=renderer.Render(s,160,160,1,false,false,0);auto key=VisualKey(s,false,0);s.pets.clear();auto empty=renderer.Render(s,160,160,1,false,false,0);Check(Different(*pets,*empty)&&key!=VisualKey(s,false,0),"Pet pixels and repaint key follow supplied frames");Check(renderer.HitBoxes(s,160,160,1,false,0).empty(),"Pets never create agent hover targets");
    auto transparent=renderer.Render(s,160,160,1,true,false,0);Color tile,margin;GdiCheck(transparent->GetPixel(24,100,&tile));GdiCheck(transparent->GetPixel(0,0,&margin));Check(tile.GetA()==255&&margin.GetA()==0,"V5 transparent view retains opaque nonvoid tiles and clears margins");
    s.agents={a};s.agents[0].action="darts";Check(VisualKey(s,false,0)!=VisualKey(s,false,.125),"Flight overlays repaint at their bounded 125ms cadence");
}
void AlphaHitBoxTests(const Snapshot& original,const std::wstring& output){
    auto sprite=MakeGdi<Bitmap>(16,24,PixelFormat32bppPARGB);
    {Graphics g(sprite.get());Require(g.Clear(Color(0,0,0,0))==Ok,"Alpha fixture clear");SolidBrush ink(Color(255,80,160,120));Require(g.FillRectangle(&ink,8,0,8,24)==Ok,"Alpha fixture opaque half");}
    {PixelLock lock(*sprite,16,24);bool half=true;for(int y=0;y<24;++y){auto* row=static_cast<BYTE*>(lock.data.Scan0)+y*lock.data.Stride;for(int x=0;x<16;++x)if(row[x*4+3]!=(x<8?0:255))half=false;}Check(half,"PixelLock captures exact transparent and opaque sprite halves");lock.Close();}
    const auto path=output+L"\\windhawk-hit-alpha.png";SavePng(*sprite,path);
    auto s=RegressionScene(original);Sheet sheet;sheet.id="fallback";sheet.file="windhawk-hit-alpha.png";sheet.sha=Sha256(ReadFileBytes(path,1024*1024));sheet.w=16;sheet.h=24;sheet.cw=16;sheet.ch=24;sheet.ax=8;sheet.ay=24;Clip idle;idle.name="idle";idle.direction="down";sheet.clips={idle};
    s.sheets.clear();s.sheets.emplace(sheet.id,sheet);s.rawAssets="alpha-half-regression";s.agents={RegressionAgent(s,"alpha-agent",3,4)};
    Renderer renderer;renderer.Load(s,output);
    for(int version:{4,5,INT32_MAX})for(int size:{160,80}){
        s.schema=version;int zoom=size==160?1:0;auto camera=ViewCamera(s,size,size,zoom);auto boxes=renderer.HitBoxes(s,size,size,zoom,false,0);
        Check(boxes.size()==1&&boxes.front().second==0,"v4 alpha hit box retains agent index");const auto& b=boxes.front().first;
        Check(std::abs(b.x-(camera.x+56*camera.zoom))<1e-6&&std::abs(b.y-(camera.y+56*camera.zoom))<1e-6&&std::abs(b.w-8*camera.zoom)<1e-6&&std::abs(b.h-24*camera.zoom)<1e-6,"v4 alpha hit box maps actual content to screen coordinates");
        Check(renderer.Hit(s,size,size,zoom,camera.x+52*camera.zoom,camera.y+68*camera.zoom,false,0)==nullptr,"v4 transparent sprite half has no hover target");
        Check(renderer.Hit(s,size,size,zoom,camera.x+60*camera.zoom,camera.y+68*camera.zoom,false,0)==&s.agents.front(),"v4 opaque sprite half retains hover target");
        if(size==80)Check(std::abs(camera.zoom-.3125)<1e-6,"v4 automatic camera fits a small viewport with fractional zoom");
        auto offline=renderer.HitBoxes(s,size,size,zoom,true,1);Check(offline.size()==1&&std::abs(offline.front().first.y-b.y-camera.zoom)<1e-6,"Offline alpha hover bounds follow one-pixel breathing motion");
    }
    s.schema=3;auto legacy=renderer.HitBoxes(s,160,160,1,false,0);Check(legacy.size()==1&&legacy.front().first.w==16,"v3 keeps complete sprite hit geometry");
    s.schema=4;s.agents.front().opacity=0;Check(renderer.HitBoxes(s,160,160,1,false,0).empty()&&renderer.Hit(s,160,160,1,76,84,false,0)==nullptr,"Zero-opacity sprite is excluded from HitBoxes and Hit");
    s.agents.front().opacity=1;s.agents.front().visible=false;Check(renderer.HitBoxes(s,160,160,1,false,0).empty(),"Invisible sprite has no screen hit box");
    s.agents.front().visible=true;auto top=s.agents.front();top.id="top-agent";top.ordinal=Wide(top.id);s.agents.push_back(top);auto overlap=renderer.HitBoxes(s,160,160,1,false,0);
    Check(overlap.size()==2&&overlap.front().second==1&&renderer.Hit(s,160,160,1,76,84,false,0)==&s.agents.back(),"HitBoxes and Hit preserve front-to-back ordinal depth ties");
    // A 32px authored tile occupies 16 world pixels. At zoom 2, its single
    // odd source column must stay one screen pixel wide without a 1x raster.
    Color base(255,57,82,105),stripe(255,227,49,187);auto tileImage=MakeGdi<Bitmap>(32,32,PixelFormat32bppPARGB);
    {Graphics g(tileImage.get());Require(g.Clear(base)==Ok,"Odd-column tile clear");SolidBrush ink(stripe);Require(g.FillRectangle(&ink,7,0,1,32)==Ok,"Odd-column tile stripe");}
    const auto tilePath=output+L"\\windhawk-odd-tile.png";SavePng(*tileImage,tilePath);
    Sheet tile;tile.id="odd-pixel-floor";tile.file="windhawk-odd-tile.png";tile.sha=Sha256(ReadFileBytes(tilePath,1024*1024));tile.w=32;tile.h=32;tile.cw=32;tile.ch=32;Clip floorClip;floorClip.name="floor";floorClip.direction="any";tile.clips={floorClip};
    s.sheets.emplace(tile.id,tile);s.rawAssets="alpha-half-and-odd-tile-regression";s.agents.clear();s.types.clear();TileType floor;floor.id=0;floor.kind="floor";floor.color="#395269";floor.sheet=tile.id;floor.animation="floor";s.types.emplace(0,floor);s.tiles.assign(64,0);renderer.Load(s,output);
    auto image=renderer.Render(s,288,288,2,false,false,0);auto camera=ViewCamera(s,288,288,2);int x=static_cast<int>(camera.x)+3*32+7,y=static_cast<int>(camera.y)+2*32+11;Color left,middle,right;
    Require(image->GetPixel(x-1,y,&left)==Ok&&image->GetPixel(x,y,&middle)==Ok&&image->GetPixel(x+1,y,&right)==Ok,"Read authored tile stripe pixels");
    Check(left.GetValue()==base.GetValue()&&middle.GetValue()==stripe.GetValue()&&right.GetValue()==base.GetValue(),"Zoom 2 retains a single odd authored tile column and its neighboring pixels");
}
void WarmCacheDimensionTests(Renderer& renderer,const Snapshot& original,const std::wstring& root){
    auto before=renderer.Render(original,700,480,0,false,false,original.generated);
    const auto& id=original.agents.front().frame.sheet;const auto& sheet=original.sheets.at(id);
    for(const char* dimension:{"width","height"}){
        auto candidate=original;auto& changed=candidate.sheets.at(id);int old=dimension[0]=='w'?sheet.w:sheet.h,cell=dimension[0]=='w'?sheet.cw:sheet.ch;
        Require(old+cell<=8192,"F5 dimension mutation within schema bounds");int value=old+cell;if(dimension[0]=='w')changed.w=value;else changed.h=value;
        auto manifest=Parser(candidate.rawAssets).Parse();auto item=std::find_if(manifest.At("sheets").array.begin(),manifest.At("sheets").array.end(),[&](const Json& j){return j.At("id").Str()==id;});Require(item!=manifest.At("sheets").array.end(),"F5 manifest sheet");
        candidate.rawAssets=ObjectField(candidate.rawAssets,*item,dimension,std::to_string(value));candidate.staticKey=Hash(candidate.rawOffice+candidate.rawAssets);
        Check(changed.id==sheet.id&&changed.file==sheet.file&&changed.sha==sheet.sha&&candidate.rawAssets!=original.rawAssets,"F5 changed manifest retains ID/path/SHA cache identity");
        Reject([&]{renderer.Load(candidate,root);});Renderer cold;Reject([&]{cold.Load(candidate,root);});
        auto retained=renderer.Render(original,700,480,0,false,false,original.generated);Check(retained->GetLastStatus()==Ok&&!Different(*before,*retained),"F5 dimension rejection retains last-good pixels");
        renderer.Load(original,root);
    }
}
struct AllocationFault {
    explicit AllocationFault(int after){g_failAllocationAfter=after;}
    ~AllocationFault(){g_failAllocationAfter=-1;}
};
struct GdiAllocatorFault {
    GdiAllocatorFault(){failGdiWrapperAllocation=true;}
    ~GdiAllocatorFault(){failGdiWrapperAllocation=false;}
};
void GdiAllocatorBypassTests(const Snapshot& original,const std::wstring& root){
    auto s=RegressionScene(original);auto a=RegressionAgent(s,"gdi-allocation-owner",4,4);a.queued=true;a.action="typing";a.workLocation="overflow";s.agents={a};
    Renderer reference;reference.Load(s,root);auto before=reference.Render(s,160,160,1,false,false,0);Renderer fresh;FontFamily family(L"Segoe UI");decltype(before) rendered;
    {GdiAllocatorFault fault;
        auto calls=gdiWrapperAllocationCalls;Check(Gdiplus::DllExports::VdcTestGdiAlloc(16)==nullptr&&gdiWrapperAllocationCalls==calls+1,"F2 actual GdipAlloc null hook is armed");calls=gdiWrapperAllocationCalls;
        auto bitmap=MakeGdi<Bitmap>(16,24,PixelFormat32bppPARGB);auto font=MakeGdi<Font>(&family,7.0f,FontStyleRegular,UnitPixel);
        Check(bitmap->GetLastStatus()==Ok&&font->GetLastStatus()==Ok,"F2 Bitmap and Font construction bypass null GDI+ wrapper allocator");
        fresh.Load(s,root);rendered=fresh.Render(s,160,160,1,false,false,0);
        Check(rendered->GetLastStatus()==Ok&&!Different(*before,*rendered),"F2 actual GdipAlloc null leaves cold Load/render pixels intact");
        Check(gdiWrapperAllocationCalls==calls,"F2 MakeGdi, decode and crop never call GdipAlloc wrapper allocator");
    }
    auto recovered=fresh.Render(s,160,160,1,false,false,0);Check(!Different(*rendered,*recovered),"F2 actual GDI allocator hook reset retains render");
}
void AllocationFailureTests(const Snapshot& original,const std::wstring& root){
    auto s=RegressionScene(original);auto a=RegressionAgent(s,"allocation-owner",4,4);a.action="typing";a.state="working";a.workLocation="overflow";a.queued=true;a.paper=true;
    auto bubble=RegressionAgent(s,"allocation-bubble",2,5);bubble.action="typing";bubble.state="working";bubble.toolKind="shell";s.agents={a,bubble};
    Furniture desk;desk.id="allocation-desk";desk.ordinal=Wide(desk.id);desk.kind="desk";desk.x=1;desk.y=1;desk.w=3;desk.h=3;desk.owner=a.id;s.furniture={desk};
    std::set<std::string> referenced{"fallback","icons","provider-icons","office-extras"};
    for(int tile:s.tiles){const auto& type=s.types.at(tile);if(!type.sheet.empty()&&!type.animation.empty())referenced.insert(type.sheet);}
    std::erase_if(s.sheets,[&](const auto& sheet){return !referenced.contains(sheet.first);});
    Check(s.sheets.contains("fallback")&&s.sheets.size()<=5,"F2 allocation sweep loads only referenced regression sheets");
    Renderer good;good.Load(s,root);auto retained=good.Render(s,160,160,1,false,false,0);int failures=0;bool completed=false;
    // A new Renderer on every iteration covers cold decoding, output buffers,
    // floor/sprite crops and fonts. The first success ends the complete sweep.
    for(int after=0;after<1024;++after){
        Renderer fresh;bool failed=false;decltype(retained) image;
        {AllocationFault fault(after);try{fresh.Load(s,root);image=fresh.Render(s,160,160,1,false,false,0);}catch(const std::bad_alloc&){failed=true;}}
        if(!failed){Check(image&&image->GetLastStatus()==Ok&&!Different(*retained,*image),"F2 complete allocation sweep reaches normal render");completed=true;break;}
        ++failures;fresh.Load(s,root);auto recovered=fresh.Render(s,160,160,1,false,false,0);
        Check(recovered->GetLastStatus()==Ok&&!Different(*retained,*recovered),"F2 injected allocation failure recovers retained scene after reset");
    }
    Check(completed&&failures>0,"F2 fault injection exercises allocations and terminates");
    bool failed=false;{AllocationFault fault(0);try{good.Render(s,160,160,1,false,false,0);}catch(const std::bad_alloc&){failed=true;}}
    Check(failed,"F2 next warm render allocation throws bad_alloc");auto recovered=good.Render(s,160,160,1,false,false,0);Check(!Different(*retained,*recovered),"F2 warm render failure retains prior pixels");
    std::cout<<"F2: "<<failures<<" cold Load/render allocation failure positions recovered; warm render recovered.\n";
}
void WriteStandardRenders(Renderer& renderer,const Snapshot& s,const std::wstring& output){
    auto render=[&](int w,int h,int z,bool transparent,bool offline,const wchar_t* name){auto image=renderer.Render(s,w,h,z,transparent,offline,offline?0:s.generated);SavePng(*image,output+L"\\"+name);};
    render(s.w*32+32,s.h*32+32,2,false,false,L"windhawk-2x.png");render(700,480,0,false,false,L"windhawk-auto.png");render(400,300,0,false,false,L"windhawk-small.png");render(700,480,0,true,false,L"windhawk-transparent.png");render(700,480,0,false,true,L"windhawk-offline.png");
}
int RenderOnly(const wchar_t* stateFile,const wchar_t* packRoot,const wchar_t* outputDir,TagMode mode){
    ULONG_PTR token=0;GdiplusStartupInput input;if(GdiplusStartup(&token,&input,nullptr)!=Ok)return 2;int result=0;
    try{
        auto root=LocalPath(packRoot),output=LocalPath(outputDir);CreateDirectoryW(output.c_str(),nullptr);auto s=ParseSnapshot(ReadFileBytes(LocalPath(stateFile),4*1024*1024));
        Renderer renderer;renderer.tagMode=mode;renderer.Load(s,root);WriteStandardRenders(renderer,s,output);
        std::cout<<"RENDER ONLY: standard offscreen PNGs use the mod renderer.\n";
    }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";result=1;}
    GdiplusShutdown(token);return result;
}
void Profile(const std::wstring& stateFile,const std::wstring& root,Renderer& renderer,const Snapshot& original){
    auto idle=original;for(auto& a:idle.agents){a.action="idle";a.state="idle";a.carrying=false;a.reaction.clear();a.partner.clear();a.frame=SelectFrame(idle.sheets.at(a.frame.sheet),"idle",a.facing,0);}
    auto image=renderer.Render(idle,720,520,0,false,false,idle.generated);uint64_t key=VisualKey(idle,false,idle.generated);int paints=0;
    LARGE_INTEGER frequency{},start{},end{};QueryPerformanceFrequency(&frequency);QueryPerformanceCounter(&start);double cpu=CpuSeconds();
    for(int i=0;i<40;++i){auto next=ParseSnapshot(ReadFileBytes(stateFile,4*1024*1024),&original); // 5Hz local reads, live parse and cached pack validation.
        renderer.Load(next,root);next=idle;next.generated=Now();next.sequence=i+1;auto nextKey=VisualKey(next,false,next.generated);
        if(nextKey!=key){image=renderer.Render(next,720,520,0,false,false,next.generated);key=nextKey;++paints;}Sleep(200);
    }
    QueryPerformanceCounter(&end);double wall=double(end.QuadPart-start.QuadPart)/frequency.QuadPart,used=CpuSeconds()-cpu;
    std::cout<<"Idle live parse/cache profile: "<<used<<" CPU seconds / "<<wall<<" wall seconds = "<<100*used/wall<<"% of one core; repeated paints="<<paints<<"\n";
    Check(paints==0,"Unchanged live scene must not repaint");
    // Ordinary tracker snapshots omit ambient. Sample the new local clock
    // cadence separately from the fixed, supplied offscreen ambient above.
    auto local=idle;local.hasAmbient=false;key=VisualKey(local,false,Now());paints=0;
    QueryPerformanceCounter(&start);cpu=CpuSeconds();
    for(int i=0;i<40;++i){auto time=Now();auto nextKey=VisualKey(local,false,time);
        if(nextKey!=key){image=renderer.Render(local,720,520,0,false,false,time);key=nextKey;++paints;}Sleep(200);
    }
    QueryPerformanceCounter(&end);wall=double(end.QuadPart-start.QuadPart)/frequency.QuadPart;used=CpuSeconds()-cpu;
    std::cout<<"Local ambient idle rendering: "<<100*used/wall<<"% of one core; paints="<<paints<<" / "<<wall<<" wall seconds\n";
    const bool timed=local.schema>=5&&std::any_of(local.furniture.begin(),local.furniture.end(),[](const Furniture& f){return f.kind=="clock"||f.kind=="window"||f.asset.find("string-lights")!=std::string::npos;});
    Check(timed?paints>0&&paints<=static_cast<int>(std::ceil(wall))+1:paints==0,"Local ambient changes at most once per second");
    // Also exercise the same offline breathing cadence, at one paint/second.
    QueryPerformanceCounter(&start);cpu=CpuSeconds();paints=0;
    for(int i=0;i<8;++i){image=renderer.Render(idle,720,520,0,false,true,i);++paints;Sleep(1000);}
    QueryPerformanceCounter(&end);wall=double(end.QuadPart-start.QuadPart)/frequency.QuadPart;used=CpuSeconds()-cpu;
    std::cout<<"Offline idle rendering: "<<100*used/wall<<"% of one core; paints="<<paints<<"\n";
}
}
int wmain(int argc,wchar_t** argv){
    if(argc<4){std::cerr<<"Usage: renderer_tests STATE_JSON EXPORTED_PACK_ROOT OUTPUT_DIR [--profile|--render-only]\n";return 2;}
    if(argc>4&&wcscmp(argv[4],L"--render-only")==0){TagMode mode=TagMode::Hover;if(argc>6&&wcscmp(argv[5],L"--tags")==0)mode=NormalizeTagMode(argv[6]);return RenderOnly(argv[1],argv[2],argv[3],mode);}
    ULONG_PTR token=0;GdiplusStartupInput input;if(GdiplusStartup(&token,&input,nullptr)!=Ok)return 2;int result=0;
    try {
        auto root=LocalPath(argv[2]),output=LocalPath(argv[3]);CreateDirectoryW(output.c_str(),nullptr);auto bytes=ReadFileBytes(LocalPath(argv[1]),4*1024*1024);auto s=ParseSnapshot(bytes);
        NativeFaultCleanupTests();
        Check(s.w>=8&&s.tiles.size()==size_t(s.w*s.h)&&!s.agents.empty(),"Demo geometry");
        Check(Parser("{\"x\":\"\\uD83D\\uDE00 한글\",\"n\":9223372036854775807,\"b\":true}").Parse().At("n").Int(1,INT64_MAX)==INT64_MAX,"Exact int64");
        Check(Wide(Parser("\"\\uD83D\\uDE00\"").Parse().Str()).size()==2,"Unicode surrogate pair");
        for(const std::string& bad:std::vector<std::string>{"{","[1,]","{\"a\":1,\"a\":2}","01","1e999","1.","true false","\"\\uD800\"","\"\\uDC00\"","\"\\u0000\"","\"\\q\"",std::string("\"\xC0\xAF\"")})Reject([&]{Parser(bad).Parse();});
        Reject([&]{Parser(std::string(70,'[')+std::string(70,']')).Parse();});
        Reject([&]{Parser(std::string(4*1024*1024+1,' ')).Parse();});
        Reject([&]{Parser("9223372036854775808").Parse().Int(1,INT64_MAX);});
        Check(std::abs(Timestamp("2026-10-04T09:00:00+09:00")-Timestamp("2026-10-04T00:00:00Z"))<.001,"UTC offset");
        Check(std::abs(Timestamp("2026-10-04T00:00:00.125Z")-Timestamp("2026-10-04T00:00:00Z")-.125)<.001,"Fractional seconds");
        Reject([&]{Timestamp("2026-02-30T00:00:00Z");});Reject([&]{Timestamp("2026-10-04T00:00:00+15:00");});
        Reject([&]{SheetPath(root,"../escape.png");});Reject([&]{SheetPath(root,"C:/escape.png");});Reject([&]{SheetPath(root,"//server/share.png");});Reject([&]{SheetPath(root,"sheets/file.png:stream");});
        Check(SheetPath(root,"sheets/ok.png").starts_with(root),"Relative sheet path");
        RelativePathGrammarTests();
        HardeningInputTests(bytes,s);
        HostPolicyTests();
        auto schemaToken="\"schemaVersion\": "+std::to_string(s.schema);
        Reject([&]{ParseSnapshot(Replace(bytes,schemaToken,"\"schemaVersion\": 2"));});
        AdditiveSchemaTests(bytes,s);
        Reject([&]{ParseSnapshot(Replace(bytes,"\"tileSize\": 16","\"tileSize\": 17"));});
        Reject([&]{ParseSnapshot(bytes.substr(0,bytes.size()/2));});
        Check(ParseSnapshot(Replace(bytes,schemaToken,"\"futureField\": {\"a\":[1,null,true]}, "+schemaToken)).staticKey==s.staticKey,"Unknown additive fields");
        Check(ParseSnapshot(bytes,&s).staticKey==s.staticKey,"Validated static JSON reuse");
        Reject([&]{ParseSnapshot(Replace(bytes,"\"tileSize\": 16","\"tileSize\": 17"),&s);});
        Reject([&]{ParseSnapshot(bytes.substr(0,bytes.size()/2),&s);});
        Reject([&]{ParseSnapshot(Replace(bytes,"\"cellWidth\": 16","\"cellWidth\": 17"),&s);});
        auto tree=Parser(bytes).Parse();const auto& furnitureFrames=tree.At("furnitureSprites");
        auto primitives=bytes;primitives.replace(furnitureFrames.begin,furnitureFrames.end-furnitureFrames.begin,"[]");
        auto primitiveState=ParseSnapshot(primitives,&s);Check(std::none_of(primitiveState.furniture.begin(),primitiveState.furniture.end(),[](const Furniture& f){return f.hasFrame;}),"Empty furniture frames select primitives");
        auto valid=ParseSnapshot(Replace(bytes,"\"sequence\": 1","\"sequence\": 9223372036854775807"));Check(valid.sequence==INT64_MAX,"Snapshot int64 sequence");
        auto badFrame=Parser("{\"sheetId\":\"fallback\",\"row\":0,\"column\":0,\"x\":719,\"y\":0,\"width\":16,\"height\":24,\"anchorX\":8,\"anchorY\":24,\"scale\":1}").Parse();Reject([&]{ParseFrame(badFrame,s);});
        Agent a;a.x=3;a.y=7;Frame f{"fallback",0,0,0,0,16,24,8,24,1,0,0};auto bounds=AgentBounds(a,f);Check(bounds.x==48&&bounds.y==104,"Foot anchor equation");
        f.oy=-10;Check(AgentBounds(a,f).y==94,"Draw-only pose offset");
        Snapshot order;Furniture fur;fur.id="b";fur.ordinal=L"b";fur.y=6;fur.h=2;order.furniture.push_back(fur);a.id="a";a.ordinal=L"a";a.visible=true;a.frame=f;order.agents.push_back(a);
        auto items=Scene(order);Check(items.size()==2&&items[0].agent&&items[0].y==128,"Depth ties use ordinal ID, not drawn offset");order.furniture[0].variant.layer=0;Check(!Scene(order)[0].agent,"Furniture layers precede world Y");
        Check(!Offline(s,s.generated+9.999)&&Offline(s,s.generated+10.001),"Stale threshold");
        Check(VisualKey(s,false,s.generated)==VisualKey(s,false,s.generated+.1),"No unnecessary idle timer repaint");
        Check(Tooltip(s.agents[0],s.generated).find("Project   ")!=std::string::npos,"Tooltip project metadata");
        Renderer renderer;renderer.Load(s,root);
        {
            Renderer reference;reference.Load(s,root);reference.cacheRoom=false;
            auto local=s;local.hasAmbient=false;
            auto first=renderer.Render(local,720,520,0,false,false,s.generated);
            auto rooms=renderer.roomBuildCount;
            for(int second:{1,2,17}){
                auto retained=renderer.Render(local,720,520,0,false,false,s.generated+second);
                auto fresh=reference.Render(local,720,520,0,false,false,s.generated+second);
                Check(!Different(*retained,*fresh),"Cached room matches a fresh full room with local clock changes");
            }
            Check(renderer.roomBuildCount==rooms,"Local clock retains the room raster");
            auto transparent=renderer.Render(local,720,520,0,true,false,s.generated);
            auto fresh=reference.Render(local,720,520,0,true,false,s.generated);
            Check(!Different(*transparent,*fresh),"Room cache invalidates for transparent mode");
            Check(renderer.roomBuildCount==rooms+1,"Mode change rebuilds the room once");
        }
        auto v4=Replace(bytes,schemaToken,"\"schemaVersion\": 4");v4=PoseField(v4,"displayName","\"Windhawk test\"");v4=PoseField(v4,"workLocation","\"overflow\"");v4=PoseField(v4,"queued","true");v4=PoseField(v4,"action","\"typing\"");
        auto added=ParseSnapshot(v4,&s);Check(added.schema==4&&added.agents.front().queued&&added.agents.front().workLocation=="overflow"&&added.agents.front().displayName=="Windhawk test","v4 optional visual metadata");
        Check(added.agents.front().frame.x==s.agents.front().frame.x&&added.agents.front().frame.sheet==s.agents.front().frame.sheet,"v4 supplied frame remains authoritative");
        auto addedRender=renderer.Render(added,700,480,0,false,false,s.generated);SavePng(*addedRender,output+L"\\windhawk-v4-additions.png");
        auto noExtra=added;noExtra.agents.front().queued=false;noExtra.agents.front().workLocation="desk";auto ordinary=renderer.Render(noExtra,700,480,0,false,false,s.generated);Check(Different(*addedRender,*ordinary),"v4 queue/laptop render branches");
        Check(ParseSnapshot(PoseField(v4,"desk","{\"x\":-1,\"y\":-1}"),&s).agents.front().dx==-1,"v4 unassigned desk sentinel");
        auto unassigned=ParseSnapshot(PoseField(v4,"desk","{\"x\":-1,\"y\":-1}"),&s);auto unassignedRender=renderer.Render(unassigned,700,480,0,false,false,s.generated);
        for(int version:{5}){
            auto future=ParseSnapshot(RootField(PoseField(v4,"desk","{\"x\":-1,\"y\":-1}"),"schemaVersion",std::to_string(version)),&s);
            Check(future.schema==version&&future.agents.front().dx==-1&&future.agents.front().dy==-1&&future.agents.front().queued&&future.agents.front().workLocation=="overflow"&&future.agents.front().displayName=="Windhawk test","v5 schema preserves v4 pose metadata");
            auto expected=unassigned;expected.schema=5;auto expectedRender=renderer.Render(expected,700,480,0,false,false,s.generated);
            auto futureRender=renderer.Render(future,700,480,0,false,false,s.generated);Check(!Different(*expectedRender,*futureRender),"V5 parser/defaults preserve identical v5 render pixels");
        }
        Reject([&]{ParseSnapshot(RootField(PoseField(v4,"desk","{\"x\":-1,\"y\":-1}"),"schemaVersion","3"),&s);});
        Reject([&]{ParseSnapshot(PoseField(v4,"desk","{\"x\":-2,\"y\":-1}"),&s);});
        StateOnlyVisualTests(renderer,s);DeskNameplateTests(renderer,s);NameTagModeTests(renderer,s);V5ContractTests(bytes,s);AmbientContractTests(bytes,s);LifeVisualTests(renderer,s);AlphaHitBoxTests(s,output);
        WriteStandardRenders(renderer,s,output);
        auto first=s.agents.front();auto camera=ViewCamera(s,700,480,0);auto box=AgentBounds(first,first.frame);if(first.visible)Check(renderer.Hit(s,700,480,0,camera.x+(box.x+box.w/2)*camera.zoom,camera.y+(box.y+box.h/2)*camera.zoom,false,0)!=nullptr,"Sprite hover geometry");
        auto wrong=s;wrong.rawAssets.clear();wrong.sheets.begin()->second.sha=std::string(64,'0');Reject([&]{renderer.Load(wrong,root);});
        // A failed candidate must leave the old images renderable.
        auto retained=renderer.Render(s,700,480,0,false,false,s.generated);Check(retained->GetLastStatus()==Ok,"Last-good image transaction");
        WarmCacheDimensionTests(renderer,s,root);GdiAllocatorBypassTests(s,root);AllocationFailureTests(s,root);
        auto idle0=renderer.Render(s,700,480,0,false,true,0),idle1=renderer.Render(s,700,480,0,false,true,1);Check(Different(*idle0,*idle1),"Disconnected idle continues animating");
        if(argc>4&&wcscmp(argv[4],L"--profile")==0)Profile(LocalPath(argv[1]),root,renderer,s);
        std::cout<<"PASS: "<<checks<<" contract/parser/geometry/cache checks; PNGs use the mod renderer.\n";
    }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";result=1;}
    GdiplusShutdown(token);return result;
}
