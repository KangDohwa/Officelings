// Retain every legacy hidden-host check without editing tests/.
#define wmain LegacyHostMain
#include "../tests/windhawk/host_tests.cpp"
#undef wmain

int wmain(int argc,wchar_t** argv){
    const int prior=LegacyHostMain(argc,argv);if(prior)return prior;
    try{
        TestEnvironment environment;environment.reset();GdiPlusLifetime graphics;graphics.start();g_settings.tagMode=TagMode::Always;
        WorkerState worker;worker.packRoot=LocalPath(argv[2]);
        auto snapshot=ParseSnapshot(ReadFileBytes(LocalPath(argv[1]),4*1024*1024));
        Require(!snapshot.agents.empty(),"Synthetic host metadata fixture");
        snapshot.agents.front().name=std::string(4096,'N');
        worker.renderer.Load(snapshot,worker.packRoot);worker.snapshot=snapshot;worker.hasState=true;
        ViewRequest request;request.enabled=true;request.zoom=2;request.serial=1;
        auto packet=worker.build(request,snapshot.generated,VisualKey(snapshot,false,snapshot.generated));
        size_t aliases=0,plate=kNoHit;
        for(size_t i=0;i<packet->hits.size();++i){const auto& hit=packet->hits[i];if(hit.dataIndex==kNoHit)continue;++aliases;
            HostCheck(hit.agent.id.empty()&&hit.tooltip.empty()&&!HitMetadata(*packet,i).agent.id.empty()&&!HitMetadata(*packet,i).tooltip.empty(),"Tag/desk hit shares full metadata without duplicating text");
            if(hit.plate)plate=i;
        }
        HostCheck(aliases>0&&plate!=kNoHit,"Always mode publishes linked visible plate hits");
        request.hoverPacket=packet;request.hoverIndex=plate;
        HostCheck(RequestedHover(request)==HitMetadata(*packet,plate).agent.id&&RequestedTag(request)==packet->hits[plate].key,"Plate hover retains agent identity and distinct tag key");
        std::cout<<"PASS: "<<hostChecks<<" total hidden host/metadata checks.\n";
    }catch(const std::exception& error){std::cerr<<"FAIL: "<<error.what()<<"\n";return 1;}
    return 0;
}
