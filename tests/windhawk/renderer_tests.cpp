// This executable has no Windhawk linkage and excludes the entire HWND host.
#define VIBEDOTCOM_TEST
#include "../../windhawk/vibedotcom.wh.cpp"
#include <iostream>
#include <functional>

using namespace vdc;
namespace {
int checks=0;
void Check(bool condition,const char* message){++checks;Require(condition,message);}
void Reject(const std::function<void()>& fn){bool rejected=false;try{fn();}catch(const Error&){rejected=true;}Check(rejected,"Expected rejection");}
std::string Replace(std::string s,const std::string& from,const std::string& to){auto p=s.find(from);Require(p!=s.npos,"Test mutation target missing");s.replace(p,from.size(),to);return s;}
std::string PoseField(std::string bytes,const std::string& name,const std::string& value){auto j=Parser(bytes).Parse();const auto& pose=j.At("agents").array.front().At("pose");auto it=pose.object.find(name);if(it==pose.object.end())bytes.insert(pose.begin+1,"\""+name+"\":"+value+",");else bytes.replace(it->second.begin,it->second.end-it->second.begin,value);return bytes;}
double CpuSeconds(){FILETIME a{},b{},k{},u{};GetProcessTimes(GetCurrentProcess(),&a,&b,&k,&u);ULARGE_INTEGER x{},y{};x.LowPart=k.dwLowDateTime;x.HighPart=k.dwHighDateTime;y.LowPart=u.dwLowDateTime;y.HighPart=u.dwHighDateTime;return (x.QuadPart+y.QuadPart)/1e7;}
bool Different(Bitmap& a,Bitmap& b){
    Gdiplus::Rect r(0,0,a.GetWidth(),a.GetHeight());BitmapData x{},y{};
    Require(a.LockBits(&r,ImageLockModeRead,PixelFormat32bppPARGB,&x)==Ok,"Lock first render");
    if(b.LockBits(&r,ImageLockModeRead,PixelFormat32bppPARGB,&y)!=Ok){a.UnlockBits(&x);throw Error("Lock second render");}
    bool different=false;for(UINT row=0;row<a.GetHeight();++row)if(memcmp(static_cast<BYTE*>(x.Scan0)+row*x.Stride,static_cast<BYTE*>(y.Scan0)+row*y.Stride,a.GetWidth()*4)!=0){different=true;break;}
    a.UnlockBits(&x);b.UnlockBits(&y);return different;
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
    // Also exercise the same offline breathing cadence, at one paint/second.
    QueryPerformanceCounter(&start);cpu=CpuSeconds();paints=0;
    for(int i=0;i<8;++i){image=renderer.Render(idle,720,520,0,false,true,i);++paints;Sleep(1000);}
    QueryPerformanceCounter(&end);wall=double(end.QuadPart-start.QuadPart)/frequency.QuadPart;used=CpuSeconds()-cpu;
    std::cout<<"Offline idle rendering: "<<100*used/wall<<"% of one core; paints="<<paints<<"\n";
}
}
int wmain(int argc,wchar_t** argv){
    if(argc<4){std::cerr<<"Usage: renderer_tests STATE_JSON EXPORTED_PACK_ROOT OUTPUT_DIR [--profile]\n";return 2;}
    ULONG_PTR token=0;GdiplusStartupInput input;if(GdiplusStartup(&token,&input,nullptr)!=Ok)return 2;int result=0;
    try {
        auto root=LocalPath(argv[2]),output=LocalPath(argv[3]);CreateDirectoryW(output.c_str(),nullptr);auto bytes=ReadFileBytes(LocalPath(argv[1]),4*1024*1024);auto s=ParseSnapshot(bytes);
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
        auto schemaToken="\"schemaVersion\": "+std::to_string(s.schema);
        Reject([&]{ParseSnapshot(Replace(bytes,schemaToken,"\"schemaVersion\": 2"));});
        Reject([&]{ParseSnapshot(Replace(bytes,schemaToken,"\"schemaVersion\": 5"));});
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
        auto v4=Replace(bytes,schemaToken,"\"schemaVersion\": 4");v4=PoseField(v4,"displayName","\"Windhawk test\"");v4=PoseField(v4,"workLocation","\"overflow\"");v4=PoseField(v4,"queued","true");v4=PoseField(v4,"action","\"typing\"");
        auto added=ParseSnapshot(v4,&s);Check(added.schema==4&&added.agents.front().queued&&added.agents.front().workLocation=="overflow"&&added.agents.front().displayName=="Windhawk test","v4 optional visual metadata");
        Check(added.agents.front().frame.x==s.agents.front().frame.x&&added.agents.front().frame.sheet==s.agents.front().frame.sheet,"v4 supplied frame remains authoritative");
        auto addedRender=renderer.Render(added,700,480,0,false,false,s.generated);SavePng(*addedRender,output+L"\\windhawk-v4-additions.png");
        auto noExtra=added;noExtra.agents.front().queued=false;noExtra.agents.front().workLocation="desk";auto ordinary=renderer.Render(noExtra,700,480,0,false,false,s.generated);Check(Different(*addedRender,*ordinary),"v4 queue/laptop render branches");
        Check(ParseSnapshot(PoseField(v4,"desk","{\"x\":-1,\"y\":-1}"),&s).agents.front().dx==-1,"v4 unassigned desk sentinel");
        auto render=[&](int w,int h,int z,bool transparent,bool offline,const wchar_t* name){auto image=renderer.Render(s,w,h,z,transparent,offline,offline?0:s.generated);SavePng(*image,output+L"\\"+name);};
        render(s.w*32+32,s.h*32+32,2,false,false,L"windhawk-2x.png");render(700,480,0,false,false,L"windhawk-auto.png");render(700,480,0,true,false,L"windhawk-transparent.png");render(700,480,0,false,true,L"windhawk-offline.png");
        auto first=s.agents.front();auto camera=ViewCamera(s,700,480,0);auto box=AgentBounds(first,first.frame);if(first.visible)Check(renderer.Hit(s,700,480,0,camera.x+(box.x+box.w/2)*camera.zoom,camera.y+(box.y+box.h/2)*camera.zoom,false,0)!=nullptr,"Sprite hover geometry");
        auto wrong=s;wrong.rawAssets.clear();wrong.sheets.begin()->second.sha=std::string(64,'0');Reject([&]{renderer.Load(wrong,root);});
        // A failed candidate must leave the old images renderable.
        auto retained=renderer.Render(s,700,480,0,false,false,s.generated);Check(retained->GetLastStatus()==Ok,"Last-good image transaction");
        auto idle0=renderer.Render(s,700,480,0,false,true,0),idle1=renderer.Render(s,700,480,0,false,true,1);Check(Different(*idle0,*idle1),"Disconnected idle continues animating");
        if(argc>4&&wcscmp(argv[4],L"--profile")==0)Profile(LocalPath(argv[1]),root,renderer,s);
        std::cout<<"PASS: "<<checks<<" contract/parser/geometry/cache checks; PNGs use the mod renderer.\n";
    }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";result=1;}
    GdiplusShutdown(token);return result;
}
