// Standalone actual renderer; no DLL injection, HWNDs, personal data or internal CPU observer.
#define OFFICELINGS_TEST
#include <iostream>
#ifndef PERF_MOD_SOURCE
#define PERF_MOD_SOURCE "../../windhawk/officelings.wh.cpp"
#endif
#include PERF_MOD_SOURCE
using namespace vdc;

int wmain(int argc, wchar_t** argv) {
    if(argc!=3) return 2;
    ULONG_PTR token=0;Gdiplus::GdiplusStartupInput input;
    if(Gdiplus::GdiplusStartup(&token,&input,nullptr)!=Gdiplus::Ok) return 2;
    int result=0;
    try {
        auto snapshot=ParseSnapshot(ReadFileBytes(LocalPath(argv[1]),4*1024*1024));
        snapshot.hasAmbient=false;
        Renderer renderer;renderer.Load(snapshot,LocalPath(argv[2]));
        auto image=renderer.Render(snapshot,720,520,0,false,false,Now());
        auto started=Now();auto key=VisualKey(snapshot,false,started);
        // Finite 90-second workload: external collector discards 30s and measures 30s.
        while(Now()-started<90) {
            auto now=Now();auto next=VisualKey(snapshot,false,now);
            if(next!=key) { image=renderer.Render(snapshot,720,520,0,false,false,now);key=next; }
            Sleep(200);
        }
    } catch(const std::exception& e) { std::cerr<<e.what()<<"\n";result=1; }
    Gdiplus::GdiplusShutdown(token);return result;
}
