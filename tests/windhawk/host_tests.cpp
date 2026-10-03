// Standalone host tests operate only on this executable's hidden HWNDs.
#define VIBEDOTCOM_TEST
#define VIBEDOTCOM_HOST_TEST
#include <windows.h>
BOOL WINAPI VdcHostTestCancelSynchronousIo(HANDLE thread);
#define CancelSynchronousIo VdcHostTestCancelSynchronousIo
#include "../../windhawk/vibedotcom.wh.cpp"
#undef CancelSynchronousIo
#include <iostream>

namespace {
struct CancelObservation {
    HANDLE worker=nullptr,firstCall=nullptr;
    std::atomic<unsigned> calls{0};
    DWORD initialError=ERROR_SUCCESS;BOOL initialResult=TRUE;
};
std::atomic<CancelObservation*> cancelObservation{nullptr};
}
BOOL WINAPI VdcHostTestCancelSynchronousIo(HANDLE thread){
    BOOL result=::CancelSynchronousIo(thread);DWORD error=GetLastError();
    if(auto* observation=cancelObservation.load();observation&&observation->worker==thread){
        if(observation->calls.fetch_add(1)==0){
            observation->initialResult=result;observation->initialError=error;
            SetEvent(observation->firstCall);
        }
    }
    SetLastError(error);return result;
}
namespace {
int hostChecks=0,cancelModes=0;
constexpr wchar_t kTestHiddenClass[]=L"Vibedotcom.HostTest.Hidden";
void HostCheck(bool condition,const char* message){++hostChecks;Require(condition,message);}
void Phase(const char* name){std::cout<<"HOST PHASE: "<<name<<std::endl;}

LRESULT CALLBACK TestViewProc(HWND w,UINT msg,WPARAM wp,LPARAM lp){
    if(msg==WM_CANCELMODE)++cancelModes;
    auto result=ViewProc(w,msg,wp,lp);
    if(msg==WM_NCCREATE)std::cout<<"View WM_NCCREATE: hwnd="<<w<<" result="<<result<<" lastError="<<GetLastError()<<std::endl;
    return result;
}
struct TestEnvironment {
    FileHandle stop{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
    FileHandle wake{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
    WindowClass view{kViewClass},hidden{kTestHiddenClass};
    TestEnvironment(){
        Require(stop.h&&stop.h!=INVALID_HANDLE_VALUE&&wake.h&&wake.h!=INVALID_HANDLE_VALUE,"Host test events");
        g_module=GetModuleHandleW(nullptr);g_stop=stop.h;g_workWake=wake.h;
        g_settings=Settings{};g_settings.tooltip=false;
        view.init(TestViewProc);hidden.init(DefWindowProcW);
    }
    void reset(){
        Require(!g_office.load()&&!g_notify.load()&&!g_worker&&!g_thread,"Prior owned host resources released");
        g_uiThreadId.store(0);g_shellRequest.store(nullptr);g_shellBusy.store(false);g_shellOutcome.store(0);
        Require(ResetEvent(g_stop)&&ResetEvent(g_workWake),"Reset owned host events");
        ClearMailbox();cancelModes=0;g_failAllocationAfter=-1;
    }
    ~TestEnvironment(){ClearMailbox();g_stop=nullptr;g_workWake=nullptr;g_uiThreadId.store(0);}
};
void RequireOwnHidden(HWND w,const char* purpose){
    DWORD creationError=GetLastError(),pid=0;DWORD thread=w?GetWindowThreadProcessId(w,&pid):0;
    if(!w||thread!=GetCurrentThreadId()||pid!=GetCurrentProcessId())
        std::cerr<<"HWND ownership failure: "<<purpose<<" hwnd="<<w<<" lastError="<<creationError<<" pid="<<pid<<" thread="<<thread<<" expectedPid="<<GetCurrentProcessId()<<" expectedThread="<<GetCurrentThreadId()<<std::endl;
    Require(w&&thread==GetCurrentThreadId()&&pid==GetCurrentProcessId(),"Test HWND ownership");
    Require(!IsWindowVisible(w)&&(GetWindowLongPtrW(w,GWL_STYLE)&WS_VISIBLE)==0,"Test HWND starts hidden");
}
HWND HiddenView(Ui& ui,DWORD style=WS_POPUP){
    HWND w=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,kViewClass,L"",style,
        0,0,480,320,nullptr,nullptr,g_module,&ui);
    RequireOwnHidden(w,"ViewProc view");Require(ui.window.get()==w,"Actual ViewProc attached owned Ui");return w;
}
void CheckCompatibilityManifest(std::string manifest){
    for(char& c:manifest)if(c>='A'&&c<='Z')c=static_cast<char>(c-'A'+'a');
    const bool supported=manifest.find("4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38")!=manifest.npos||
        manifest.find("1f676c76-80e1-4239-95bb-83d0f6d0da78")!=manifest.npos||
        manifest.find("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a")!=manifest.npos;
    HostCheck(supported&&manifest.find("<compatibility")!=manifest.npos&&manifest.find("<supportedos")!=manifest.npos,"Owned test activation manifest declares Windows 8 or newer compatibility");
}
struct ScopedTestCompatibility {
    HANDLE context=INVALID_HANDLE_VALUE;ULONG_PTR cookie=0;
    explicit ScopedTestCompatibility(const std::wstring& path){
        CheckCompatibilityManifest(ReadFileBytes(path,64*1024));
        ACTCTXW description{};description.cbSize=sizeof(description);description.lpSource=path.c_str();
        context=CreateActCtxW(&description);Require(context!=INVALID_HANDLE_VALUE,"Create owned test activation context");
        if(!ActivateActCtx(context,&cookie)){ReleaseActCtx(context);context=INVALID_HANDLE_VALUE;throw Error("Activate owned test context");}
    }
    ~ScopedTestCompatibility(){if(context!=INVALID_HANDLE_VALUE){DeactivateActCtx(0,cookie);ReleaseActCtx(context);}}
};

struct ListMessages {unsigned pointerMessages=0,hitTests=0;};
LRESULT CALLBACK ListSpy(HWND w,UINT msg,WPARAM wp,LPARAM lp,UINT_PTR,DWORD_PTR reference){
    auto& calls=*reinterpret_cast<ListMessages*>(reference);
    if(msg>=WM_USER&&lp)++calls.pointerMessages;
    if(msg==LVM_HITTEST)++calls.hitTests;
    return DefSubclassProc(w,msg,wp,lp);
}
struct ListSpyScope {
    HWND window;
    ListSpyScope(HWND w,ListMessages& calls):window(w){Require(SetWindowSubclass(w,ListSpy,1,reinterpret_cast<DWORD_PTR>(&calls))!=FALSE,"Owned list-view observer");}
    ~ListSpyScope(){RemoveWindowSubclass(window,ListSpy,1);}
};
void ListViewSuppressionTest(TestEnvironment& environment){
    environment.reset();INITCOMMONCONTROLSEX controls{sizeof(controls),ICC_LISTVIEW_CLASSES};Require(InitCommonControlsEx(&controls)!=FALSE,"List-view class initialization");
    ListMessages calls;OwnedWindow parent,list,child;
    parent.value=CreateWindowExW(WS_EX_NOACTIVATE,kTestHiddenClass,L"",WS_POPUP,0,0,320,240,nullptr,nullptr,g_module,nullptr);RequireOwnHidden(parent.get(),"list-view parent");
    list.value=CreateWindowExW(0,L"SysListView32",L"",WS_CHILD|LVS_REPORT,0,0,100,100,parent.get(),nullptr,g_module,nullptr);RequireOwnHidden(list.get(),"SysListView32");
    child.value=CreateWindowExW(0,kTestHiddenClass,L"",WS_CHILD,0,0,8,8,list.get(),nullptr,g_module,nullptr);RequireOwnHidden(child.get(),"list-view descendant");
    ListSpyScope observer(list.get(),calls);
    HostCheck(UnderListView(list.get())&&UnderListView(child.get()),"F1 owned hidden list-view and descendants suppress hover");
    HostCheck(!UnderListView(parent.get())&&!UnderListView(nullptr),"F1 unrelated owned window and null have no list-view suppression");
    HostCheck(calls.pointerMessages==0&&calls.hitTests==0,"F1 list-view suppression sends no pointer or hit-test control messages");
}
void ModalStopTests(TestEnvironment& environment){
    for(UINT enter:{WM_ENTERSIZEMOVE,WM_ENTERMENULOOP}){
        Phase(enter==WM_ENTERSIZEMOVE?"F4 WM_ENTERSIZEMOVE":"F4 WM_ENTERMENULOOP");
        environment.reset();Ui ui;HWND w=HiddenView(ui);
        SendMessageW(w,enter,0,0);
        HostCheck(ui.running&&!ui.stopping&&(enter==WM_ENTERSIZEMOVE?ui.inSizeMove:ui.inMenu),"F4 actual ViewProc records owned modal-entry notification");
        SendMessageW(w,WM_TIMER,kStopTimer,0);
        HostCheck(ui.running&&IsWindow(w),"F4 stop timer preserves owned window before stop signal");
        Require(ResetEvent(g_workWake)&&SetEvent(g_stop),"Signal owned modal stop event");
        Require(PostMessageW(w,WM_TIMER,kStopTimer,0)!=FALSE,"Post owned modal stop timer");
        MSG message{};Require(PeekMessageW(&message,w,WM_TIMER,WM_TIMER,PM_REMOVE)!=FALSE,"Read owned modal stop timer");DispatchMessageW(&message);
        HostCheck(!ui.running&&ui.stopping&&!ui.active&&!ui.window.get()&&!IsWindow(w),"F4 stop timer destroys owned hidden view and terminates Ui");
        HostCheck(cancelModes>0&&WaitForSingleObject(g_workWake,0)==WAIT_OBJECT_0&&!g_office.load(),"F4 stop path cancels owned mode, wakes worker and releases HWND publication");
    }
}
void ClientExtentTests(TestEnvironment& environment){
    for(DWORD style:{DWORD(WS_POPUP),DWORD(WS_OVERLAPPEDWINDOW)}){
        environment.reset();Ui ui;HWND w=HiddenView(ui,style);
        Require(SetWindowPos(w,nullptr,0,0,8000,6000,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE)!=FALSE,"Resize owned hidden view beyond limits");
        RECT client{};Require(GetClientRect(w,&client)!=FALSE,"Read oversized owned client");
        HostCheck(client.right-client.left==4096&&client.bottom-client.top==2048,"F9 actual hidden popup/framed client clamps to buffer limits");
        HostCheck(ui.width==client.right-client.left&&ui.height==client.bottom-client.top,"F9 Ui render extent equals actual clamped client");
        auto request=LatestRequest();HostCheck(request.width==ui.width&&request.height==ui.height&&!request.enabled,"F9 clamped hidden viewport stages matching inactive worker request");
        Require(SetWindowPos(w,nullptr,0,0,640,480,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE)!=FALSE,"Resize owned view within limits");
        Require(GetClientRect(w,&client)!=FALSE,"Read normal owned client");
        HostCheck(ui.width==client.right-client.left&&ui.height==client.bottom-client.top&&ui.width<=640&&ui.height<=480,"F9 normal programmatic resize keeps real and rendered client extent equal");
        HostCheck(!IsWindowVisible(w),"F9 extent enforcement leaves the owned window hidden");
    }
}
void TooltipFontTests(TestEnvironment& environment){
    environment.reset();Ui ui;
    for(UINT dpi:{96u,144u,192u}){
        Require(SetTipDpi(ui,dpi),"Create owned tooltip font");LOGFONTW font{};
        Require(GetObjectW(ui.tipFont.value,sizeof(font),&font)==static_cast<int>(sizeof(font)),"Read owned tooltip font metrics");
        int height=dpi==96?-13:dpi==144?-20:-26;
        HostCheck(ui.tipDpi==dpi&&font.lfHeight==height&&font.lfWeight==FW_NORMAL,"F10 native tooltip font scales at 100/150/200 percent DPI");
        HFONT same=ui.tipFont.value;HostCheck(SetTipDpi(ui,dpi)&&ui.tipFont.value==same,"F10 unchanged DPI reuses owned tooltip font");
    }
    HostCheck(SetTipDpi(ui,0)&&ui.tipDpi==96,"F10 missing DPI restores the native 96 DPI tooltip font");
}
void HiddenChildAlphaTests(TestEnvironment& environment){
    environment.reset();OwnedWindow parent,child;
    parent.value=CreateWindowExW(WS_EX_NOACTIVATE,kTestHiddenClass,L"",WS_POPUP,0,0,100,100,nullptr,nullptr,g_module,nullptr);RequireOwnHidden(parent.get(),"alpha parent");
    child.value=CreateWindowExW(WS_EX_LAYERED|WS_EX_NOACTIVATE|WS_EX_NOPARENTNOTIFY,kTestHiddenClass,L"",WS_CHILD,0,0,64,48,parent.get(),nullptr,g_module,nullptr);RequireOwnHidden(child.get(),"layered child");
    DibSurface surface;Require(surface.resize(64,48),"Owned premultiplied child DIB");
    const DWORD pixel=(128u<<24)|(96u<<16)|(64u<<8)|32u;auto* pixels=static_cast<DWORD*>(surface.bits);std::fill(pixels,pixels+64*48,pixel);
    for(int opacity:{50,100}){
        POINT source{};SIZE size{64,48};BLENDFUNCTION blend{AC_SRC_OVER,0,static_cast<BYTE>(opacity*255/100),AC_SRC_ALPHA};
        BOOL admitted=UpdateLayeredWindow(child.get(),nullptr,nullptr,&size,surface.dc,&source,0,&blend,ULW_ALPHA);
        DWORD error=admitted?ERROR_SUCCESS:GetLastError();if(!admitted)std::cerr<<"Hidden child ULW_ALPHA rejected, error="<<error<<"\n";
        HostCheck(admitted!=FALSE,"B hidden owned layered child admits premultiplied alpha and constant opacity");
        HostCheck(pixels[0]==pixel&&pixels[64*48-1]==pixel,"B alpha presentation preserves premultiplied source DIB");
        HostCheck(!IsWindowVisible(parent.get())&&!IsWindowVisible(child.get()),"B alpha API admission leaves owned parent and child hidden");
    }
}

struct PacketBuildResult {
    std::wstring stateFile,root;ViewRequest request;
    std::shared_ptr<const Packet> packet,cached;
    DWORD threadId=0;bool orderMatches=false,pixelsMatch=false,rejected=false,retained=false;
    std::string error;
};
struct WorkerAllocationFault {
    WorkerAllocationFault(){g_failAllocationAfter=0;}
    ~WorkerAllocationFault(){g_failAllocationAfter=-1;}
};
DWORD WINAPI PacketBuildThread(void* value){
    auto& result=*static_cast<PacketBuildResult*>(value);
    try{
        ReadStopScope stop;WorkerState worker;result.threadId=GetCurrentThreadId();worker.packRoot=result.root;
        auto s=ParseSnapshot(ReadFileBytes(result.stateFile,4*1024*1024));Require(!s.agents.empty(),"Host packet fixture agent");
        Agent a=s.agents.front();a.id="a";a.ordinal=L"a";a.name="A";a.displayName="A";a.parent.clear();a.action="idle";a.state="idle";a.visible=true;a.opacity=1;a.x=3;a.y=4;a.dx=3;a.dy=4;a.workLocation="desk";a.queued=false;a.paper=false;a.carrying=false;a.reaction.clear();a.partner.clear();
        a.frame=SelectFrame(s.sheets.at("fallback"),"idle","down",0);a.frame.ax=8;a.frame.ay=24;a.frame.scale=1;a.frame.ox=a.frame.oy=0;auto z=a;z.id="z";z.ordinal=L"z";z.name="Z";z.displayName="Z";z.sub=true;
        s.schema=4;s.w=s.h=8;s.doorX=0;s.doorY=7;s.cozy=true;s.furniture.clear();s.zones.clear();s.agents={a,z};
        auto floor=std::find_if(s.types.begin(),s.types.end(),[](const auto& t){return t.second.kind=="floor";});Require(floor!=s.types.end(),"Host packet fixture floor");s.tiles.assign(64,floor->first);s.staticKey=Hash("host-packet-regression"+s.rawAssets);
        worker.renderer.Load(s,result.root);worker.snapshot=s;worker.hasState=true;const double now=s.generated;uint64_t key=VisualKey(s,false,now)^Hash(RequestedHover(result.request));
        result.packet=worker.build(result.request,now,key);auto boxes=worker.renderer.HitBoxes(s,result.request.width,result.request.height,static_cast<int>(result.request.zoom),false,now);
        result.orderMatches=boxes.size()==2&&result.packet->hits.size()==boxes.size()&&result.packet->hits.front().agent.id=="z";
        for(size_t i=0;i<boxes.size()&&result.orderMatches;++i){const auto& expected=boxes[i].first;const auto& actual=result.packet->hits[i];result.orderMatches=actual.agent.id==s.agents.at(boxes[i].second).id&&std::abs(actual.bounds.x-expected.x)<1e-6&&std::abs(actual.bounds.y-expected.y)<1e-6&&std::abs(actual.bounds.w-expected.w)<1e-6&&std::abs(actual.bounds.h-expected.h)<1e-6&&actual.tooltip==Wide(Tooltip(actual.agent,now));}
        auto image=worker.renderer.Render(s,result.request.width,result.request.height,static_cast<int>(result.request.zoom),false,false,now,RequestedHover(result.request));
        {PixelLock lock(*image,result.request.width,result.request.height);size_t row=static_cast<size_t>(result.request.width)*4;result.pixelsMatch=result.packet->pixels->bgra.size()==row*result.request.height;for(int y=0;y<result.request.height&&result.pixelsMatch;++y)result.pixelsMatch=std::memcmp(static_cast<const BYTE*>(lock.data.Scan0)+static_cast<ptrdiff_t>(y)*lock.data.Stride,result.packet->pixels->bgra.data()+static_cast<size_t>(y)*row,row)==0;lock.Close();}
        auto next=result.request;++next.serial;result.cached=worker.build(next,now,key);auto failing=next;++failing.width;auto serial=worker.pixelSerial;
        {WorkerAllocationFault injection;try{worker.build(failing,now,key);}catch(const std::bad_alloc&){result.rejected=true;}}
        result.retained=worker.pixels==result.packet->pixels&&worker.pixelSerial==serial&&worker.build(next,now,key)->pixels==result.packet->pixels;
    }catch(const std::exception& e){result.error=e.what();}catch(...){result.error="Unexpected packet worker exception";}
    return result.error.empty()?0:1;
}
struct JoinedTestThread {
    HANDLE handle;
    ~JoinedTestThread(){if(handle){if(WaitForSingleObject(handle,0)!=WAIT_OBJECT_0){SignalStop();WaitForSingleObject(handle,INFINITE);}CloseHandle(handle);}}
};
struct PipeReadResult {
    HANDLE pipe,ready,firstCancellation;
    BOOL succeeded=FALSE;DWORD error=ERROR_SUCCESS,bytes=0;
};
DWORD WINAPI BlockedPipeRead(void* value){
    auto& result=*static_cast<PipeReadResult*>(value);SetEvent(result.ready);
    if(WaitForSingleObject(result.firstCancellation,INFINITE)!=WAIT_OBJECT_0){result.error=GetLastError();return 1;}
    BYTE byte=0;result.succeeded=ReadFile(result.pipe,&byte,1,&result.bytes,nullptr);
    result.error=result.succeeded?ERROR_SUCCESS:GetLastError();return 0;
}
struct PipeWatchdogResult {
    HANDLE worker,writer,firstCancellation;
    bool fired=false,drained=false;
};
DWORD WINAPI DrainPipeOnTimeout(void* value){
    auto& result=*static_cast<PipeWatchdogResult*>(value);
    if(WaitForSingleObject(result.worker,3000)!=WAIT_OBJECT_0){
        result.fired=true;SetEvent(result.firstCancellation);BYTE byte=1;DWORD written=0;
        result.drained=WriteFile(result.writer,&byte,1,&written,nullptr)!=FALSE&&written==1;
    }
    return 0;
}
struct PipeRaceThreads {
    HANDLE reader=nullptr,watchdog=nullptr,writer,release;
    ~PipeRaceThreads(){
        if(reader&&WaitForSingleObject(reader,0)!=WAIT_OBJECT_0){
            SetEvent(release);BYTE byte=1;DWORD written=0;WriteFile(writer,&byte,1,&written,nullptr);
            ::CancelSynchronousIo(reader);
        }
        if(reader)WaitForSingleObject(reader,INFINITE);
        if(watchdog)WaitForSingleObject(watchdog,INFINITE);
        cancelObservation.store(nullptr);if(g_worker==reader)g_worker=nullptr;
        if(reader)CloseHandle(reader);if(watchdog)CloseHandle(watchdog);
    }
};
void SynchronousCancellationRaceTests(TestEnvironment& environment){
    environment.reset();
    auto name=L"\\\\.\\pipe\\Vibedotcom.HostTest."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(GetTickCount64());
    FileHandle pipe(CreateNamedPipeW(name.c_str(),PIPE_ACCESS_INBOUND|FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,64,64,0,nullptr));Require(pipe.h!=INVALID_HANDLE_VALUE,"Create owned local test pipe");
    FileHandle writer(CreateFileW(name.c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr));Require(writer.h!=INVALID_HANDLE_VALUE,"Open owned pipe writer");
    BOOL connected=ConnectNamedPipe(pipe.h,nullptr);Require(connected||GetLastError()==ERROR_PIPE_CONNECTED,"Connect owned synchronous pipe");
    FileHandle ready(CreateEventW(nullptr,TRUE,FALSE,nullptr)),firstCancellation(CreateEventW(nullptr,TRUE,FALSE,nullptr));Require(ready.h&&firstCancellation.h,"Owned pipe race barriers");
    PipeReadResult read{pipe.h,ready.h,firstCancellation.h};CancelObservation observation;observation.firstCall=firstCancellation.h;
    PipeWatchdogResult watchdog{nullptr,writer.h,firstCancellation.h};PipeRaceThreads threads{nullptr,nullptr,writer.h,firstCancellation.h};
    threads.reader=CreateThread(nullptr,0,BlockedPipeRead,&read,0,nullptr);Require(threads.reader,"Create owned synchronous reader");
    HostCheck(WaitForSingleObject(ready.h,5000)==WAIT_OBJECT_0,"F3 owned pipe reader reaches the pre-IO cancellation barrier");
    observation.worker=threads.reader;watchdog.worker=threads.reader;cancelObservation.store(&observation);g_worker=threads.reader;
    threads.watchdog=CreateThread(nullptr,0,DrainPipeOnTimeout,&watchdog,0,nullptr);Require(threads.watchdog,"Create owned pipe drain watchdog");
    Require(SetEvent(g_stop),"Signal stop before joining owned synchronous reader");
    // The observer calls the real cancellation API before releasing ReadFile.
    // A one-shot join therefore needs the watchdog's byte and fails below.
    JoinSceneFromUi();
    HostCheck(WaitForSingleObject(threads.reader,0)==WAIT_OBJECT_0&&WaitForSingleObject(threads.watchdog,5000)==WAIT_OBJECT_0,"F3 cancellation join and owned watchdog finish");
    std::cout<<"Pipe race: cancellations="<<observation.calls.load()<<" initialError="<<observation.initialError<<" readError="<<read.error<<" watchdog="<<watchdog.fired<<" drained="<<watchdog.drained<<std::endl;
    HostCheck(!observation.initialResult&&observation.initialError==ERROR_NOT_FOUND,"F3 first actual cancellation precedes the synchronous IO");
    HostCheck(observation.calls.load()>=2&&!watchdog.fired&&!read.succeeded&&read.bytes==0&&read.error==ERROR_OPERATION_ABORTED,"F3 repeated join cancellation catches IO starting after the first cancellation");
}
void WorkerPacketTests(TestEnvironment& environment,const std::wstring& stateFile,const std::wstring& root){
    environment.reset();Ui ui;HiddenView(ui);auto hover=std::make_shared<Packet>();CachedHit hit;hit.agent.id="cached-hover";hover->hits.push_back(hit);ui.hoverPacket=hover;ui.hoverIndex=0;ui.active=true;ui.zoom=2;
    Require(ResetEvent(g_workWake),"Reset request wake");auto prior=ui.requestSerial;PublishRequest(ui);auto request=LatestRequest();
    HostCheck(request.serial==prior+1&&request.width==ui.width&&request.height==ui.height&&request.zoom==2&&request.enabled&&RequestedHover(request)=="cached-hover","F3 Ui stages dimensions and pinned cached hover without rendering");
    HostCheck(WaitForSingleObject(g_workWake,0)==WAIT_OBJECT_0,"F3 request staging wakes the worker");
    PacketBuildResult result;result.stateFile=stateFile;result.root=root;result.request=request;JoinedTestThread thread{CreateThread(nullptr,0,PacketBuildThread,&result,0,nullptr)};Require(thread.handle!=nullptr,"Create owned packet test worker");
    HostCheck(WaitForSingleObject(thread.handle,30000)==WAIT_OBJECT_0,"F3 standalone packet worker completes");Require(result.error.empty(),result.error.c_str());
    HostCheck(result.threadId!=GetCurrentThreadId()&&result.packet&&result.packet->requestSerial==request.serial&&result.packet->hasState&&!result.packet->offline,"F3 actual WorkerState builds complete packet off the owning UI thread");
    HostCheck(result.orderMatches&&result.pixelsMatch,"F3 packet cached hits match renderer depth order and copied pixels");
    HostCheck(result.cached->pixels==result.packet->pixels&&result.cached->requestSerial==request.serial+1,"F3 metadata request reuses completed worker pixels");
    HostCheck(result.rejected&&result.retained,"F2 worker build bad_alloc preserves prior packet pixels and serial");
    HostCheck(!result.packet->hits.front().tooltip.empty()&&!result.packet->pixels->bgra.empty(),"F3 cached tooltip and pixels outlive the worker renderer");
    ui.active=false;PublishRequest(ui);HostCheck(!LatestRequest().enabled,"F3 inactive owned view disables worker request");
}
}

int wmain(int argc,wchar_t** argv){
    if(argc<3){std::cerr<<"Usage: host_tests STATE_JSON EXPORTED_PACK_ROOT [TEST_MANIFEST]\n";return 2;}
    try{
        Phase("sidecar compatibility activation");
        auto manifest=argc>3?LocalPath(argv[3]):Parent(LocalPath(Wide(__FILE__)))+L"\\host_tests.manifest";ScopedTestCompatibility compatibility(manifest);
        Phase("owned class/event and GDI initialization");
        TestEnvironment environment;GdiPlusLifetime graphics;graphics.start();DpiScope dpi(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);Require(dpi.previous!=nullptr,"Host test DPI context");
        Phase("F1 list-view suppression");ListViewSuppressionTest(environment);ModalStopTests(environment);
        Phase("F9 client extent");ClientExtentTests(environment);Phase("F10 tooltip font");TooltipFontTests(environment);
        Phase("B hidden child alpha");HiddenChildAlphaTests(environment);
        Phase("F3 synchronous IO cancellation race");SynchronousCancellationRaceTests(environment);
        Phase("F3 worker packet and F2 allocation failure");
        WorkerPacketTests(environment,LocalPath(argv[1]),LocalPath(argv[2]));
        std::cout<<"PASS: "<<hostChecks<<" hidden host checks. Real native modal interaction and callback C++/SEH body-fault injection remain unverified.\n";
    }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
    return 0;
}
