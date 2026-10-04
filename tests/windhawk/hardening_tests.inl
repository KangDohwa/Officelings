// Included inside the synthetic offscreen harness's anonymous namespace.
void HardeningInputTests(const std::string& bytes,const Snapshot& snapshot) {
    auto tree=Parser(bytes).Parse();
    auto longName=ObjectField(bytes,tree.At("agents").array.front().At("agent"),"name","\""+std::string(65000,'n')+"\"");
    longName=PoseField(longName,"displayName","null");
    auto bounded=ParseSnapshot(longName,&snapshot);
    Check(Wide(bounded.agents.front().displayName).size()==16,"Defaulted display names are bounded before measurement");
    auto crop=Parser("{\"sheetId\":\"fallback\",\"row\":0,\"column\":0,\"x\":0,\"y\":0,\"width\":17,\"height\":24,\"anchorX\":8,\"anchorY\":24,\"scale\":1}").Parse();
    Reject([&]{ParseFrame(crop,snapshot);});

    wchar_t temp[MAX_PATH]{};Require(GetTempPathW(MAX_PATH,temp)>0,"Synthetic TEMP path");
    auto work=std::wstring(temp)+L"vdc-hardening-"+std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64());
    auto outside=work+L"\\outside",alias=work+L"\\alias",marker=outside+L"\\marker.txt",png=outside+L"\\padded.png";
    Require(CreateDirectoryW(work.c_str(),nullptr)&&CreateDirectoryW(outside.c_str(),nullptr)&&CreateDirectoryW(alias.c_str(),nullptr),"Create synthetic boundary directories");
    struct Cleanup {
        std::wstring work,outside,alias,marker,png;
        ~Cleanup(){DeleteFileW(png.c_str());DeleteFileW(marker.c_str());RemoveDirectoryW(alias.c_str());RemoveDirectoryW(outside.c_str());RemoveDirectoryW(work.c_str());}
    } cleanup{work,outside,alias,marker,png};
    {
        FileHandle file(CreateFileW(marker.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,0,nullptr));DWORD wrote=0;
        Require(file.h!=INVALID_HANDLE_VALUE&&WriteFile(file.h,"keep",4,&wrote,nullptr)&&wrote==4,"Write synthetic marker");
    }
    Check(ReadFileBytes(marker,100)=="keep","Regular local boundary file remains readable");
    {
        auto held=LockAncestors(marker);
        FileHandle write(CreateFileW(outside.c_str(),GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,nullptr));
        Check(write.h==INVALID_HANDLE_VALUE&&GetLastError()==ERROR_SHARING_VIOLATION,"Held ancestors reject reparse mutation as well as replacement");
    }
    // Valid tiny PNGs with large ancillary chunks must count against the pack's
    // compressed budget even when decoded pixels and individual files are small.
    {
        SafeGdi<Bitmap> tiny(16,24,PixelFormat32bppARGB);SavePng(tiny.get(),png);
        auto original=ReadFileBytes(png,1024*1024);
        std::string chunk="vpAd";chunk.append(24*1024*1024,'\0');
        uint32_t crc=0xffffffff;for(unsigned char byte:chunk){crc^=byte;for(int bit=0;bit<8;++bit)crc=(crc>>1)^(0xedb88320u&-(crc&1));}crc^=0xffffffff;
        auto bigEndian=[](uint32_t value){std::string result;for(int shift:{24,16,8,0})result.push_back(static_cast<char>(value>>shift));return result;};
        auto padded=original.substr(0,original.size()-12)+bigEndian(24*1024*1024)+chunk+bigEndian(crc)+original.substr(original.size()-12);
        {
            FileHandle file(CreateFileW(png.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr));DWORD wrote=0;
            Require(file.h!=INVALID_HANDLE_VALUE&&WriteFile(file.h,padded.data(),static_cast<DWORD>(padded.size()),&wrote,nullptr)&&wrote==padded.size(),"Write synthetic padded PNG");
        }
        auto candidate=snapshot;candidate.sheets.clear();auto sheet=snapshot.sheets.at("fallback");
        sheet.file="padded.png";sheet.w=16;sheet.h=24;sheet.sha=Sha256(padded);
        sheet.id="first";candidate.sheets.emplace(sheet.id,sheet);sheet.id="second";candidate.sheets.emplace(sheet.id,sheet);
        Renderer held;held.Load(candidate,outside);Check(true,"Two padded PNGs fit the compressed budget");
        auto three=candidate;sheet.id="third";three.sheets.emplace(sheet.id,sheet);
        Reject([&]{held.Load(three,outside);});held.Load(candidate,outside);
        Check(true,"Rejected aggregate pack retains the previous cache");
    }
    // Mount-point junctions need no symlink privilege and never target a network.
    struct MountPoint {
        DWORD tag;WORD length,reserved,subOffset,subLength,printOffset,printLength;wchar_t buffer[1024];
    } point{};
    auto substitute=L"\\??\\"+outside;
    Require(substitute.size()+outside.size()+2<1024,"Synthetic junction size");
    point.tag=IO_REPARSE_TAG_MOUNT_POINT;point.subLength=static_cast<WORD>(substitute.size()*2);
    point.printOffset=point.subLength+2;point.printLength=static_cast<WORD>(outside.size()*2);
    point.length=static_cast<WORD>(8+point.printOffset+point.printLength+2);
    std::copy(substitute.begin(),substitute.end(),point.buffer);
    std::copy(outside.begin(),outside.end(),point.buffer+point.printOffset/2);
    {
        FileHandle file(CreateFileW(alias.c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr));DWORD wrote=0;
        Require(file.h!=INVALID_HANDLE_VALUE&&DeviceIoControl(file.h,FSCTL_SET_REPARSE_POINT,&point,point.length+8,nullptr,0,&wrote,nullptr),"Create synthetic junction");
    }
    Reject([&]{LocalPath(alias+L"\\marker.txt");});
    Reject([&]{ReadFileBytes(alias+L"\\marker.txt",100);});
    Reject([&]{SheetPath(work,"alias/marker.txt");});
    Check(ReadFileBytes(marker,100)=="keep","Rejected link leaves the outside marker intact");
}
