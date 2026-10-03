// ==WindhawkMod==
// @id              vibedotcom
// @name            Vibedotcom pixel office
// @description     Passive pixel office driven by the headless Vibedotcom tracker
// @version         0.1.0
// @author          KangDohwa
// @license         MIT
// @include         explorer.exe
// @architecture    amd64
// @compilerOptions -fms-extensions -lgdiplus -lgdi32 -luser32 -lbcrypt -lole32 -ldwmapi -ldwrite
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
Run Vibedotcom.Tracker separately. This mod reads version 3 and additive v4+ state.json
and content-addressed PNGs; it never opens agent logs or runs a second simulation.
Default state: %LOCALAPPDATA%\Vibedotcom\state.json. Blank pack path means the
state file's directory (the tracker's exported sheets, not its source assets).
Settings changes reload the window. Desktop mode is best-effort; unsupported
shell topologies fall back to a bottom-level window. No secure lock-screen UI.
See windhawk/README.md for installation, testing and the manual shell checklist.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- mode: normal
  $name: Window mode
  $options:
  - normal: Normal
  - topmost: Always on top
  - desktop-level: Desktop level
- clickThrough: false
  $name: Click through
- zoom: 0
  $name: Integer zoom (0 = auto, 1-6)
- opacity: 100
  $name: Opacity (10-100 percent)
- x: 80
  $name: Horizontal position (screen pixels)
- y: 180
  $name: Vertical position (screen pixels)
- width: 720
  $name: Window width (screen pixels)
- height: 520
  $name: Window height (screen pixels)
- statePath: '%LOCALAPPDATA%\Vibedotcom\state.json'
  $name: State JSON path (local file)
- packPath: ''
  $name: Exported PNG root (blank = state directory)
- fpsCap: 30
  $name: Maximum render FPS (1-60)
- showTooltip: true
  $name: Show passive hover tooltip
- transparentBackground: false
  $name: Hide floor and walls
*/
// ==/WindhawkModSettings==

#ifndef VIBEDOTCOM_TEST
#include <windhawk_api.h>
#endif
#include <windows.h>
#include <commctrl.h>
#include <gdiplus.h>
#include <dwrite.h>
#include <bcrypt.h>
#include <algorithm>
#include <array>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <new>
#include <utility>
#include <set>
#include <stdexcept>
#include <string>
#include <string_view>
#include <tuple>
#include <vector>

namespace vdc {
// ===== BEGIN SHARED JSON / CONTRACT / GEOMETRY (included by test harness) =====
// Original small parser: no vendored dependencies. Strict JSON, UTF-8 and UTF-16
// surrogate validation, exact int64 parsing, duplicate-key and resource limits.
struct Error : std::runtime_error { using std::runtime_error::runtime_error; };
void Require(bool ok, const char* what) { if (!ok) throw Error(what); }
void CheckStop();
std::wstring Wide(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), nullptr, 0);
    Require(n > 0, "Invalid UTF-8"); std::wstring w(n, 0);
    Require(MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), w.data(), n) == n, "Invalid UTF-8");
    return w;
}
std::string Utf8Text(const std::wstring& w) {
    if(w.empty())return {};int n=WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),nullptr,0,nullptr,nullptr);std::string s(n,0);WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),s.data(),n,nullptr,nullptr);return s;
}
uint64_t Hash(const std::string& s) {
    uint64_t h = 14695981039346656037ULL;
    for (unsigned char c : s) { h ^= c; h *= 1099511628211ULL; } return h;
}
struct Json {
    enum Kind { Null, Boolean, Number, String, Array, Object } kind = Null;
    std::string value;
    std::vector<Json> array;
    std::map<std::string, Json> object;
    size_t begin = 0, end = 0;
    bool reused=false;
    const Json& At(const char* key) const {
        Require(kind == Object, "Expected object"); auto i = object.find(key);
        Require(i != object.end(), "Missing field"); return i->second;
    }
    const Json& Optional(const char* key) const {
        static const Json absent; Require(kind == Object, "Expected object");
        auto i = object.find(key); return i == object.end() ? absent : i->second;
    }
    std::string Str() const { Require(kind == String, "Expected string"); return value; }
    std::string StrOr(std::string d = {}) const { return kind == Null ? d : Str(); }
    bool Bool() const { Require(kind == Boolean, "Expected bool"); return value == "true"; }
    bool BoolOr(bool d) const { return kind == Null ? d : Bool(); }
    int64_t Int(int64_t lo, int64_t hi) const {
        Require(kind == Number, "Expected integer"); int64_t n = 0;
        auto r = std::from_chars(value.data(), value.data()+value.size(), n);
        Require(r.ec == std::errc() && r.ptr == value.data()+value.size() && n >= lo && n <= hi, "Integer bounds"); return n;
    }
    int64_t IntOr(int64_t d,int64_t lo,int64_t hi) const { return kind==Null?d:Int(lo,hi); }
    double Num(double lo, double hi) const {
        Require(kind == Number, "Expected number"); double n = 0;
        auto r = std::from_chars(value.data(), value.data()+value.size(), n);
        Require(r.ec == std::errc() && r.ptr == value.data()+value.size() && std::isfinite(n) && n >= lo && n <= hi, "Number bounds"); return n;
    }
    double NumOr(double d, double lo, double hi) const { return kind == Null ? d : Num(lo, hi); }
    const std::vector<Json>& Items(size_t max = 16384) const {
        Require(kind == Array && array.size() <= max, "Array bounds"); return array;
    }
};
class Parser {
    const std::string& s; size_t p = 0, nodes = 0;
    const std::map<std::string,std::string_view>* cache=nullptr;
    char Peek() const { return p < s.size() ? s[p] : 0; }
    void Space() { while (Peek()==' ' || Peek()=='\n' || Peek()=='\r' || Peek()=='\t') ++p; }
    void Take(char c) { Require(Peek()==c, "JSON syntax"); ++p; }
    bool Digit() const { return Peek()>='0' && Peek()<='9'; }
    unsigned Hex() {
        unsigned n = 0;
        for (int k=0;k<4;++k) {
            char c=Peek(); ++p; unsigned v=c>='0'&&c<='9' ? c-'0' : c>='a'&&c<='f' ? c-'a'+10 : c>='A'&&c<='F' ? c-'A'+10 : 99;
            Require(v<16,"Invalid Unicode escape"); n=n*16+v;
        } return n;
    }
    static void Utf8(std::string& out, unsigned c) {
        if(c<128) out+=static_cast<char>(c);
        else if(c<2048) { out+=static_cast<char>(192|(c>>6)); out+=static_cast<char>(128|(c&63)); }
        else if(c<65536) { out+=static_cast<char>(224|(c>>12)); out+=static_cast<char>(128|((c>>6)&63)); out+=static_cast<char>(128|(c&63)); }
        else { out+=static_cast<char>(240|(c>>18)); out+=static_cast<char>(128|((c>>12)&63)); out+=static_cast<char>(128|((c>>6)&63)); out+=static_cast<char>(128|(c&63)); }
    }
    std::string String() {
        Take('"'); std::string out;
        while(Peek()!='"') {
            unsigned char c=static_cast<unsigned char>(Peek()); Require(c>=32,"Control/truncated string"); ++p;
            if(c!='\\') { out+=static_cast<char>(c); continue; }
            char e=Peek(); ++p;
            switch(e) {
                case '"': case '\\': case '/': out+=e; break;
                case 'b': out+='\b'; break; case 'f': out+='\f'; break; case 'n': out+='\n'; break; case 'r': out+='\r'; break; case 't': out+='\t'; break;
                case 'u': {
                    unsigned u=Hex();
                    if(u>=0xD800 && u<=0xDBFF) { Take('\\'); Take('u'); unsigned l=Hex(); Require(l>=0xDC00&&l<=0xDFFF,"Invalid surrogate"); u=0x10000+((u-0xD800)<<10)+(l-0xDC00); }
                    else Require(u<0xDC00||u>0xDFFF,"Lone surrogate");
                    Require(u!=0,"Embedded NUL"); Utf8(out,u); break;
                }
                default: throw Error("Invalid escape");
            }
        }
        Take('"'); Require(out.size()<=65536,"String too large"); Wide(out); return out;
    }
    Json Value(int depth) {
        Require(depth<=64 && ++nodes<=250000,"JSON complexity limit");if((nodes&255)==0)CheckStop(); Space(); Json j; j.begin=p; char c=Peek();
        if(c=='{') {
            j.kind=Json::Object; ++p; Space();
            if(Peek()!='}') { while(true) {
                auto key=String(); Space(); Take(':'); Space();Json v;
                // Only skip exact bytes of previously validated root objects.
                // Syntax outside the reused value is still checked normally.
                auto cached=cache?cache->find(key):std::map<std::string,std::string_view>::const_iterator{};
                if(depth==0&&cache&&cached!=cache->end()&&s.compare(p,cached->second.size(),cached->second)==0){v.kind=Json::Object;v.reused=true;v.begin=p;p+=cached->second.size();v.end=p;}
                else v=Value(depth+1);
                Require(j.object.emplace(key,std::move(v)).second,"Duplicate JSON key"); Space();
                if(Peek()!=',') break; ++p; Space();
            } }
            Take('}');
        } else if(c=='[') {
            j.kind=Json::Array; ++p; Space();
            if(Peek()!=']') while(true) { j.array.push_back(Value(depth+1)); Space(); if(Peek()!=',') break; ++p; }
            Take(']');
        } else if(c=='"') { j.kind=Json::String; j.value=String(); }
        else if(c=='t'||c=='f'||c=='n') {
            const char* literal=c=='t'?"true":c=='f'?"false":"null";
            for(const char* a=literal;*a;++a) Take(*a);
            j.kind=c=='n'?Json::Null:Json::Boolean; j.value=literal;
        } else {
            j.kind=Json::Number; size_t start=p;
            if(Peek()=='-') ++p;
            if(Peek()=='0') ++p; else { Require(Peek()>='1'&&Peek()<='9',"Invalid number"); while(Digit()) ++p; }
            if(Peek()=='.') { ++p; Require(Digit(),"Invalid fraction"); while(Digit()) ++p; }
            if(Peek()=='e'||Peek()=='E') { ++p; if(Peek()=='+'||Peek()=='-') ++p; Require(Digit(),"Invalid exponent"); while(Digit()) ++p; }
            j.value=s.substr(start,p-start); j.Num(-std::numeric_limits<double>::max(),std::numeric_limits<double>::max());
        }
        j.end=p; return j;
    }
public:
    explicit Parser(const std::string& input,const std::map<std::string,std::string_view>* validated=nullptr):s(input),cache(validated) {}
    Json Parse() {
        Require(s.size()<=4*1024*1024,"JSON exceeds 4 MiB");
        if(s.starts_with("\xEF\xBB\xBF")) p=3;
        auto j=Value(0); Space(); Require(p==s.size(),"Trailing JSON data"); return j;
    }
};
std::wstring LocalPath(std::wstring path) {
    Require(!path.empty() && path.find(L'\0')==std::wstring::npos,"Empty/NUL path");
    DWORD n=ExpandEnvironmentStringsW(path.c_str(),nullptr,0); Require(n>0&&n<32768,"Path too long");
    std::wstring expanded(n,0); Require(ExpandEnvironmentStringsW(path.c_str(),expanded.data(),n)==n,"Expand path failed"); expanded.resize(n-1);
    std::vector<wchar_t> full(32768); n=GetFullPathNameW(expanded.c_str(),static_cast<DWORD>(full.size()),full.data(),nullptr);
    Require(n>0&&n<full.size(),"Invalid local path"); path.assign(full.data(),n);
    Require(path.size()>3 && path[1]==L':' && path[2]==L'\\' && path.find(L':',2)==std::wstring::npos,"Use a local drive path");
    std::wstring drive=path.substr(0,3); auto type=GetDriveTypeW(drive.c_str()); Require(type==DRIVE_FIXED||type==DRIVE_REMOVABLE,"No network paths");
    while(path.size()>3 && path.back()==L'\\') path.pop_back(); return path;
}
std::wstring Parent(const std::wstring& path) { return path.substr(0,path.find_last_of(L"\\/")); }
std::wstring ValidateRelativeSheetPath(const std::string& relative) {
    auto r=Wide(relative); Require(!r.empty() && r[0]!=L'/' && r[0]!=L'\\' && r.find(L':')==r.npos&&r.find(L'\0')==r.npos,"Relative sheet path required");
    std::replace(r.begin(),r.end(),L'/',L'\\');
    Require(r.back()!=L'\\',"Sheet filename required");
    size_t p=0; while(p<r.size()) { size_t end=r.find(L'\\',p); auto part=r.substr(p,end==r.npos?r.npos:end-p);
        Require(!part.empty()&&part!=L"."&&part!=L".."&&part.back()!=L'.'&&part.back()!=L' ',"Invalid sheet component"); if(end==r.npos) break; p=end+1; }
    return r;
}
std::wstring SheetPath(const std::wstring& root,const std::string& relative) {
    auto r=ValidateRelativeSheetPath(relative);
    auto path=LocalPath(root+L"\\"+r); Require(path.size()>root.size()&&_wcsnicmp(path.c_str(),root.c_str(),root.size())==0&&path[root.size()]==L'\\',"Sheet escapes root"); return path;
}
struct FileHandle {
    HANDLE h=INVALID_HANDLE_VALUE;
    explicit FileHandle(HANDLE value):h(value) {} ~FileHandle(){ if(h&&h!=INVALID_HANDLE_VALUE) CloseHandle(h); }
    FileHandle(const FileHandle&)=delete;
};
thread_local HANDLE readStop=nullptr;
void CheckStop() { Require(!readStop||WaitForSingleObject(readStop,0)==WAIT_TIMEOUT,"Read cancelled"); }
std::string ReadFileBytes(const std::wstring& path,size_t max,const std::wstring& root={}) {
    CheckStop();
    FileHandle f(CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN|FILE_FLAG_OVERLAPPED,nullptr));
    Require(f.h!=INVALID_HANDLE_VALUE,"Open file failed");
    if(!root.empty()) {
        FileHandle d(CreateFileW(root.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS,nullptr));
        Require(d.h!=INVALID_HANDLE_VALUE,"Open pack root failed");
        std::vector<wchar_t> a(32768),b(32768);
        DWORD na=GetFinalPathNameByHandleW(d.h,a.data(),static_cast<DWORD>(a.size()),FILE_NAME_NORMALIZED), nb=GetFinalPathNameByHandleW(f.h,b.data(),static_cast<DWORD>(b.size()),FILE_NAME_NORMALIZED);
        Require(na>0&&nb>na&&na<a.size()&&nb<b.size(),"Resolve sheet path failed");
        while(na>0&&a[na-1]==L'\\') --na;
        Require(_wcsnicmp(a.data(),b.data(),na)==0&&b[na]==L'\\',"Sheet reparse point escapes root");
    }
    LARGE_INTEGER length{}; Require(GetFileSizeEx(f.h,&length)&&length.QuadPart>0&&static_cast<uint64_t>(length.QuadPart)<=max,"File size limit");
    FILETIME before{},after{}; Require(GetFileTime(f.h,nullptr,nullptr,&before),"File time failed");
    std::string bytes(static_cast<size_t>(length.QuadPart),0); DWORD read=0;
    FileHandle ready(CreateEventW(nullptr,TRUE,FALSE,nullptr));Require(ready.h!=nullptr,"Read event allocation");
    OVERLAPPED operation{};operation.hEvent=ready.h;
    BOOL done=ReadFile(f.h,bytes.data(),static_cast<DWORD>(bytes.size()),&read,&operation);
    if(!done&&GetLastError()==ERROR_IO_PENDING) {
        HANDLE events[]{ready.h,readStop};DWORD result=WaitForMultipleObjects(readStop?2:1,events,FALSE,5000);
        if(result!=WAIT_OBJECT_0) {
            CancelIoEx(f.h,&operation);
            // OVERLAPPED and destination MUST outlive completion, even cancellation.
            GetOverlappedResult(f.h,&operation,&read,TRUE);
            throw Error("Read cancelled or timed out");
        }
        done=GetOverlappedResult(f.h,&operation,&read,FALSE);
    }
    Require(done&&read==bytes.size(),"Incomplete read");CheckStop();
    LARGE_INTEGER end{}; Require(GetFileSizeEx(f.h,&end)&&end.QuadPart==length.QuadPart&&GetFileTime(f.h,nullptr,nullptr,&after)&&CompareFileTime(&before,&after)==0,"File changed during read");
    return bytes;
}
double Timestamp(const std::string& s) {
    Require(s.size()>=20,"Invalid timestamp");
    auto digits=[&](size_t p,size_t n) { Require(p+n<=s.size(),"Timestamp truncated"); int v=0; for(size_t k=p;k<p+n;++k){Require(s[k]>='0'&&s[k]<='9',"Timestamp digit");v=v*10+s[k]-'0';}return v; };
    Require(s[4]=='-'&&s[7]=='-'&&s[10]=='T'&&s[13]==':'&&s[16]==':',"Timestamp syntax");
    SYSTEMTIME t{}; t.wYear=static_cast<WORD>(digits(0,4));t.wMonth=static_cast<WORD>(digits(5,2));t.wDay=static_cast<WORD>(digits(8,2));t.wHour=static_cast<WORD>(digits(11,2));t.wMinute=static_cast<WORD>(digits(14,2));t.wSecond=static_cast<WORD>(digits(17,2));
    Require(t.wYear>=1970&&t.wMonth>=1&&t.wMonth<=12&&t.wDay>=1&&t.wDay<=31&&t.wHour<24&&t.wMinute<60&&t.wSecond<60,"Timestamp range");
    size_t p=19; double fraction=0,place=.1;
    if(s[p]=='.'){++p;size_t start=p;while(p<s.size()&&s[p]>='0'&&s[p]<='9'){fraction+=(s[p++]-'0')*place;place*=.1;}Require(p>start,"Timestamp fraction");}
    int offset=0;
    if(p<s.size()&&s[p]=='Z') ++p;
    else { Require(p+6==s.size()&&(s[p]=='+'||s[p]=='-')&&s[p+3]==':',"Timestamp UTC offset");int h=digits(p+1,2),m=digits(p+4,2);Require(h<=14&&m<60&&(h<14||m==0),"Timestamp offset bounds");offset=(h*3600+m*60)*(s[p]=='+'?1:-1);p+=6; }
    Require(p==s.size(),"Timestamp trailing data");FILETIME ft{}; Require(SystemTimeToFileTime(&t,&ft),"Timestamp date");
    SYSTEMTIME check{};FileTimeToSystemTime(&ft,&check);Require(check.wMonth==t.wMonth&&check.wDay==t.wDay,"Invalid calendar date");
    ULARGE_INTEGER v{};v.LowPart=ft.dwLowDateTime;v.HighPart=ft.dwHighDateTime;return v.QuadPart/1e7-11644473600.0+fraction-offset;
}
double Now() { FILETIME ft{};GetSystemTimeAsFileTime(&ft);ULARGE_INTEGER v{};v.LowPart=ft.dwLowDateTime;v.HighPart=ft.dwHighDateTime;return v.QuadPart/1e7-11644473600.0; }
struct Frame { std::string sheet;int row=0,col=0,x=0,y=0,w=0,h=0;double ax=0,ay=0,scale=1,ox=0,oy=0; };
struct Clip { std::string name,direction;int row=0,col=0,frames=1;double fps=1,ox=0,oy=0;bool loop=true; };
struct Sheet { std::string id,file,sha;int cw=0,ch=0,w=0,h=0;double ax=0,ay=0,scale=1;std::vector<Clip> clips; };
struct TileType { int id=0;std::string kind,color,sheet,animation; };
struct Variant { int rotation=0,layer=1,w=1,h=1;double z=0;std::string state,sheet,animation; };
struct Furniture { std::wstring ordinal;std::string id,kind,asset,state,owner,deskId,monitor;int x=0,y=0,w=0,h=0,rotation=0;Variant variant;Frame frame;bool hasFrame=false; };
struct Agent { std::wstring ordinal;std::string id,parent,provider,name,role,model,project,task,tool,toolKind,state,action,facing,reaction,partner,character;
    std::string displayName,workLocation="desk",providerIcon,deskId,overflowSeatId;bool queued=false;
    double x=0,y=0,opacity=1,since=0;int dx=0,dy=0;bool visible=false,sub=false,carrying=false,paper=false;Frame frame; };
struct Zone {std::string name;int x=0,y=0,w=0,h=0;};
struct Snapshot { int schema=3,w=0,h=0,doorX=0,doorY=0;int64_t sequence=0;double generated=0;uint64_t staticKey=0;
    std::string rawAssets,rawOffice;
    std::vector<int> tiles;std::map<std::string,Sheet> sheets;std::map<int,TileType> types;std::map<std::string,std::vector<Variant>> variants;
    std::vector<Furniture> furniture;std::vector<Agent> agents;std::vector<Zone> zones;bool cozy=false; };
Frame SelectFrame(const Sheet& s,const std::string& action,const std::string& facing,double seconds) {
    auto find=[&](auto predicate)->const Clip* { for(const auto& c:s.clips) if(predicate(c)) return &c;return nullptr; };
    const Clip* c=find([&](const Clip& v){return v.name==action&&v.direction==facing;});
    if(!c)c=find([&](const Clip& v){return v.name==action&&v.direction=="any";});
    if(!c)c=find([](const Clip& v){return v.name=="idle";});if(!c)c=&s.clips.front();
    double tick=std::floor(std::max(0.0,seconds)*c->fps);int k=c->loop?static_cast<int>(std::fmod(tick,c->frames)):static_cast<int>(std::min(tick,double(c->frames-1)));
    return {s.id,c->row,c->col+k,(c->col+k)*s.cw,c->row*s.ch,s.cw,s.ch,s.ax,s.ay,s.scale,c->ox,c->oy};
}
Frame ParseFrame(const Json& j,const Snapshot& s) {
    Frame f;f.sheet=j.At("sheetId").Str();auto it=s.sheets.find(f.sheet);Require(it!=s.sheets.end(),"Unknown sprite sheet");const auto& sh=it->second;
    f.row=static_cast<int>(j.At("row").Int(0,8192));f.col=static_cast<int>(j.At("column").Int(0,8192));
    f.x=static_cast<int>(j.At("x").Int(0,sh.w));f.y=static_cast<int>(j.At("y").Int(0,sh.h));f.w=static_cast<int>(j.At("width").Int(1,sh.w));f.h=static_cast<int>(j.At("height").Int(1,sh.h));
    Require(f.x+f.w<=sh.w&&f.y+f.h<=sh.h,"Sprite outside sheet");
    Require(f.row<sh.h/sh.ch&&f.col<sh.w/sh.cw,"Sprite index outside sheet");
    f.ax=j.At("anchorX").Num(-8192,8192);f.ay=j.At("anchorY").Num(-8192,8192);f.scale=j.At("scale").Num(.125,4);f.ox=j.Optional("offsetX").NumOr(0,-8192,8192);f.oy=j.Optional("offsetY").NumOr(0,-8192,8192);return f;
}
Snapshot ParseSnapshot(const std::string& bytes,const Snapshot* previous=nullptr) {
    std::map<std::string,std::string_view> cache;
    if(previous){cache.emplace("assets",previous->rawAssets);cache.emplace("office",previous->rawOffice);}
    auto j=Parser(bytes,previous?&cache:nullptr).Parse();
    bool reuse=previous&&j.At("assets").reused&&j.At("office").reused;
    if(previous&&!reuse)j=Parser(bytes).Parse();
    Snapshot s=reuse?*previous:Snapshot{};s.agents.clear();for(auto& f:s.furniture)f.hasFrame=false;
    s.schema=static_cast<int>(j.At("schemaVersion").Int(3,INT32_MAX));s.sequence=j.At("sequence").Int(1,INT64_MAX);s.generated=Timestamp(j.At("generatedAt").Str());
    const auto& atlas=j.At("atlas");atlas.At("version").Int(2,2);
    const auto& m=j.At("assets");const auto& o=j.At("office");
    if(!reuse){m.At("version").Int(1,1);
    for(const auto& v:m.At("sheets").Items(128)) {
        Sheet sh;sh.id=v.At("id").Str();sh.file=v.At("file").Str();sh.sha=v.At("sha256").Str();
        Require(!sh.id.empty()&&sh.sha.size()==64&&sh.sha.find_first_not_of("0123456789abcdef")==std::string::npos,"Invalid sheet identity/hash");
        // Validate relative path even before a configured root is known.
        ValidateRelativeSheetPath(sh.file);
        sh.cw=static_cast<int>(v.At("cellW").Int(1,512));sh.ch=static_cast<int>(v.At("cellH").Int(1,512));sh.w=static_cast<int>(v.At("width").Int(1,8192));sh.h=static_cast<int>(v.At("height").Int(1,8192));
        Require(int64_t(sh.w)*sh.h<=16777216&&sh.w%sh.cw==0&&sh.h%sh.ch==0,"Sheet dimensions");
        sh.ax=v.At("anchor").At("x").Num(-8192,8192);sh.ay=v.At("anchor").At("y").Num(-8192,8192);sh.scale=v.Optional("scale").NumOr(1,.125,4);
        std::set<std::pair<std::string,std::string>> seen;
        for(const auto& r:v.At("rows").Items(1024)) {
            Clip c;c.name=r.At("name").Str();c.direction=r.At("direction").Str();c.row=static_cast<int>(r.At("row").Int(0,8192));c.col=static_cast<int>(r.Optional("column").IntOr(0,0,8192));
            c.frames=static_cast<int>(r.At("frames").Int(1,256));c.fps=r.At("fps").Num(.001,60);c.loop=r.Optional("loop").BoolOr(true);
            if(r.Optional("footOffset").kind!=Json::Null){c.ox=r.At("footOffset").At("x").Num(-8192,8192);c.oy=r.At("footOffset").At("y").Num(-8192,8192);}
            Require((c.row+1)*sh.ch<=sh.h&&(c.col+c.frames)*sh.cw<=sh.w&&seen.emplace(c.name,c.direction).second,"Invalid clip");
            Require(c.direction=="any"||c.direction=="up"||c.direction=="down"||c.direction=="left"||c.direction=="right","Clip direction");sh.clips.push_back(c);
        }
        Require(!sh.clips.empty()&&s.sheets.emplace(sh.id,std::move(sh)).second,"Empty/duplicate sheet");
    }
    Require(s.sheets.contains("fallback"),"Missing fallback atlas");
    for(const auto& v:m.At("tileTypes").Items(1024)) {
        TileType t;t.id=static_cast<int>(v.At("id").Int(0,65535));t.kind=v.At("kind").Str();t.color=v.At("color").Str();t.sheet=v.Optional("sheetId").StrOr();t.animation=v.Optional("animation").StrOr();
        Require((t.kind=="floor"||t.kind=="wall"||t.kind=="void")&&t.color.size()==7&&t.color[0]=='#'&&t.color.find_first_not_of("0123456789abcdefABCDEF",1)==std::string::npos,"Invalid tile type");
        Require(t.sheet.empty()||s.sheets.contains(t.sheet),"Unknown tile sheet");Require(s.types.emplace(t.id,t).second,"Duplicate tile ID");
    }
    for(const auto& v:m.At("furniture").Items(4096)) {
        auto id=v.At("id").Str();std::vector<Variant> variants;std::set<std::pair<int,std::string>> seen;
        for(const auto& a:v.At("variants").Items(256)) {
            Variant f;f.rotation=static_cast<int>(a.At("rotation").Int(0,270));f.state=a.At("state").Str();f.layer=static_cast<int>(a.Optional("layer").IntOr(1,0,2));f.z=a.Optional("zOrder").NumOr(0,-8192,8192);
            f.w=static_cast<int>(a.At("footprintW").Int(1,64));f.h=static_cast<int>(a.At("footprintH").Int(1,64));f.sheet=a.Optional("sheetId").StrOr();f.animation=a.Optional("animation").StrOr();
            Require(f.rotation%90==0&&seen.emplace(f.rotation,f.state).second&&(f.sheet.empty()||s.sheets.contains(f.sheet)),"Invalid furniture variant");variants.push_back(f);
        }
        Require(!variants.empty()&&s.variants.emplace(id,std::move(variants)).second,"Empty/duplicate furniture asset");if(id=="cozy-desk")s.cozy=true;
    }
    o.At("version").Int(1,1);o.At("tileSize").Int(16,16);s.w=static_cast<int>(o.At("widthTiles").Int(8,128));s.h=static_cast<int>(o.At("heightTiles").Int(8,128));
    s.doorX=static_cast<int>(o.At("door").At("x").Int(0,s.w-1));s.doorY=static_cast<int>(o.At("door").At("y").Int(0,s.h-1));
    for(const auto& t:o.At("tiles").Items()) {int id=static_cast<int>(t.Int(0,65535));Require(s.types.contains(id),"Unknown tile ID");s.tiles.push_back(id);}Require(s.tiles.size()==size_t(s.w*s.h),"Tile count mismatch");
    std::set<std::string> seen;
    for(const auto& v:o.At("furniture").Items(4096)) {
        Furniture f;f.id=v.At("id").Str();f.ordinal=Wide(f.id);f.kind=v.At("kind").Str();f.asset=v.Optional("assetId").StrOr(f.kind);f.state=v.Optional("state").StrOr("default");f.owner=v.Optional("agentId").StrOr();
        f.deskId=v.Optional("deskId").StrOr(f.id);f.monitor=v.Optional("monitorState").StrOr();
        f.x=static_cast<int>(v.At("x").Int(0,s.w-1));f.y=static_cast<int>(v.At("y").Int(0,s.h-1));f.w=static_cast<int>(v.At("width").Int(1,64));f.h=static_cast<int>(v.At("height").Int(1,64));f.rotation=static_cast<int>(v.Optional("rotation").IntOr(0,0,270));
        Require(!f.id.empty()&&seen.insert(f.id).second&&f.x+f.w<=s.w&&f.y+f.h<=s.h,"Furniture bounds/ID");
        if(s.variants.contains(f.asset)) {const auto& vs=s.variants.at(f.asset);auto it=std::find_if(vs.begin(),vs.end(),[&](const Variant& a){return a.rotation==f.rotation&&a.state==f.state;});Require(it!=vs.end(),"Missing furniture variant");f.variant=*it;Require(f.w==it->w&&f.h==it->h,"Furniture footprint mismatch");}
        s.furniture.push_back(f);
    }
    }
    const auto& fallback=s.sheets.at("fallback");
    Require(fallback.w==atlas.At("width").Int(1,8192)&&fallback.h==atlas.At("height").Int(1,8192)&&atlas.At("cellWidth").Int(16,16)==16&&atlas.At("cellHeight").Int(24,24)==24&&fallback.cw==16&&fallback.ch==24&&fallback.w>=144&&fallback.h>=168,"Fallback atlas mismatch");
    Require(atlas.At("columns").Int(1,8192)*16==fallback.w&&atlas.At("rows").Int(1,8192)*24==fallback.h,"Atlas grid mismatch");
    std::set<std::string> seen;
    for(const auto& v:j.At("furnitureSprites").Items(4096)) {
        auto id=v.At("id").Str();auto it=std::find_if(s.furniture.begin(),s.furniture.end(),[&](const Furniture& f){return f.id==id;});Require(it!=s.furniture.end()&&seen.insert(id).second,"Furniture frame ID");it->frame=ParseFrame(v.At("sprite"),s);it->hasFrame=true;
        it->monitor=v.Optional("monitorState").StrOr(it->monitor);
    }
    if(!j.At("furnitureSprites").array.empty())for(const auto& f:s.furniture)Require(f.variant.sheet.empty()||f.hasFrame,"Missing authoritative furniture frame");
    if(!reuse&&o.Optional("zones").kind!=Json::Null)for(const auto& v:o.At("zones").Items(256)) { Zone z;z.name=v.At("name").Str();z.x=static_cast<int>(v.At("x").Int(0,s.w-1));z.y=static_cast<int>(v.At("y").Int(0,s.h-1));z.w=static_cast<int>(v.At("width").Int(1,s.w));z.h=static_cast<int>(v.At("height").Int(1,s.h));Require(z.x+z.w<=s.w&&z.y+z.h<=s.h,"Zone bounds");s.zones.push_back(z); }
    seen.clear();
    for(const auto& v:j.At("agents").Items(2048)) {
        const auto& a=v.At("agent");const auto& p=v.At("pose");Agent t;
        t.id=a.At("id").Str();t.ordinal=Wide(t.id);Require(!t.id.empty()&&seen.insert(t.id).second,"Duplicate/empty agent ID");t.parent=a.Optional("parentId").StrOr();t.provider=a.At("provider").Str();Require(t.provider=="codex"||t.provider=="claude"||t.provider=="antigravity","Unknown provider");
        t.name=a.At("name").Str();t.role=a.Optional("role").StrOr();t.model=a.Optional("model").StrOr("unknown");t.project=a.Optional("cwd").StrOr("unknown");t.task=a.At("task").Str();t.tool=a.Optional("tool").StrOr("none");t.toolKind=a.Optional("toolKind").StrOr();t.state=a.At("state").Str();t.sub=a.At("isSubagent").Bool();t.since=Timestamp(a.At("stateSince").Str());Timestamp(a.At("lastSeen").Str());
        t.x=p.At("x").Num(-1,s.w);t.y=p.At("y").Num(-1,s.h);t.dx=static_cast<int>(p.At("desk").At("x").Int(s.schema>=4?-1:0,s.w-1));t.dy=static_cast<int>(p.At("desk").At("y").Int(s.schema>=4?-1:0,s.h-1));
        t.action=p.At("action").Str();t.facing=p.At("facing").Str();t.visible=p.At("visible").Bool();t.opacity=p.Optional("opacity").NumOr(1,0,1);t.carrying=p.Optional("carryingPaper").BoolOr(false);t.paper=p.Optional("reportOnDesk").BoolOr(false);t.reaction=p.Optional("reaction").StrOr();t.partner=p.Optional("chatPartnerId").StrOr();t.character=p.Optional("characterId").StrOr();
        t.displayName=p.Optional("displayName").StrOr(t.name);t.providerIcon=p.Optional("providerIcon").StrOr();t.workLocation=p.Optional("workLocation").StrOr("desk");t.queued=p.Optional("queued").BoolOr(false);
        t.deskId=p.Optional("deskId").StrOr();t.overflowSeatId=p.Optional("overflowSeatId").StrOr();
        t.frame=ParseFrame(p.At("sprite"),s);s.agents.push_back(t);
    }
    if(!reuse){s.rawAssets=bytes.substr(m.begin,m.end-m.begin);s.rawOffice=bytes.substr(o.begin,o.end-o.begin);s.staticKey=Hash(s.rawOffice+s.rawAssets);}return s;
}
struct Bounds {double x=0,y=0,w=0,h=0;bool Contains(double px,double py) const{return px>=x&&py>=y&&px<x+w&&py<y+h;}};
// Host policies are shared with the offscreen regression harness; no HWNDs.
DWORD HostRetryDelay(unsigned failures) {return std::min<DWORD>(30000,1000u<<std::min(failures,5u));}
SIZE ClampClientExtent(int width,int height) {return {std::clamp(width,1,4096),std::clamp(height,1,2048)};}
bool HoverEligible(bool visible,bool iconic,bool cloaked,bool currentDesktop) {return visible&&!iconic&&!cloaked&&currentDesktop;}
int DpiPixels(int dip,UINT dpi) {return MulDiv(dip,static_cast<int>(std::clamp<UINT>(dpi,48,960)),96);}
RECT RecoverOffscreenRect(RECT rect,RECT work,bool intersectsMonitor) {
    if(intersectsMonitor)return rect;
    LONG width=rect.right-rect.left,height=rect.bottom-rect.top;
    rect.left=work.left;rect.top=work.top;rect.right=rect.left+width;rect.bottom=rect.top+height;return rect;
}
Bounds AgentBounds(const Agent& a,const Frame& f) {return {std::nearbyint((a.x+.5)*16-f.ax*f.scale+f.ox),std::nearbyint((a.y+1)*16-f.ay*f.scale+f.oy),f.w*f.scale,f.h*f.scale};}
struct DrawItem {int layer;double y;std::wstring id;bool agent;size_t index;};
std::vector<DrawItem> Scene(const Snapshot& s) {
    std::vector<DrawItem> items;
    for(size_t i=0;i<s.furniture.size();++i){const auto& f=s.furniture[i];items.push_back({f.variant.layer,(f.y+f.h)*16+f.variant.z,f.ordinal,false,i});}
    for(size_t i=0;i<s.agents.size();++i){const auto& a=s.agents[i];if(a.visible)items.push_back({1,(a.y+1)*16,a.ordinal,true,i});}
    std::stable_sort(items.begin(),items.end(),[](const DrawItem& a,const DrawItem& b){return std::tie(a.layer,a.y,a.id)<std::tie(b.layer,b.y,b.id);});return items;
}
bool Offline(const Snapshot& s,double now) {return now-s.generated>10||s.generated-now>10;}
Frame DisplayFrame(const Snapshot& s,const Agent& a,bool offline,double seconds) {
    // Only disconnected visual idle frames are advanced locally. Positions, seats,
    // paths, furniture, opacity and normal live frames always belong to Core.
    if(!offline)return a.frame;
    const auto& sh=s.sheets.at(a.frame.sheet);
    bool animatedIdle=std::any_of(sh.clips.begin(),sh.clips.end(),[](const Clip& c){return c.name=="idle"&&c.frames>1;});
    if(animatedIdle){auto c=std::find_if(sh.clips.begin(),sh.clips.end(),[](const Clip& row){return row.name=="idle";});return SelectFrame(sh,"idle",a.facing,std::floor(std::max(0.0,seconds))/c->fps);}
    if(sh.id=="fallback") {auto f=a.frame;f.col=0;f.x=0;f.ox=0;f.oy=0;return f;}
    return SelectFrame(sh,"idle",a.facing,0);
}
// ===== END SHARED JSON / CONTRACT / GEOMETRY =====

// ===== BEGIN SHARED GDI+ RENDERER (no HWNDs, used verbatim offscreen) =====
using namespace Gdiplus;
// Keep native fault handling inside a destructor-free leaf. Convert its result
// to a C++ exception outside __try so callers' temporary owners still unwind
// with Windhawk's MinGW/libunwind toolchain. Outer callback guards remain the
// last resort for faults outside these individually guarded native calls.
template<class F> __declspec(noinline) auto NativeCall(F& call) -> decltype(call()) {return call();}
template<class F> __declspec(noinline) auto NativeLeaf(F& call,bool& fault) -> decltype(call()) {
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
    __try { return NativeCall(call); }
    __except(EXCEPTION_EXECUTE_HANDLER) { fault=true;return {}; }
#else
    return call();
#endif
}
template<class F> auto Native(F&& call) -> decltype(call()) {
    bool fault=false;auto result=NativeLeaf(call,fault);
    Require(!fault,"Native graphics operation fault");return result;
}
template<class F> void NativeCleanup(F&& call) noexcept {
    bool fault=false;NativeLeaf(call,fault);
}
void CheckGdiStatus(Status status,const char* message="GDI+ operation failed") {Require(status==Ok,message);}
#define GdiCheck(call, ...) CheckGdiStatus(Native([&]{return (call);}) __VA_OPT__(,) __VA_ARGS__)
template<class T> class SafeGdi {
    alignas(T) unsigned char storage[sizeof(T)];
    T* value=nullptr;
public:
    template<class... Args> explicit SafeGdi(Args&&... args) {
        value=Native([&]{return ::new(static_cast<void*>(storage)) T(std::forward<Args>(args)...);});
        try {GdiCheck(value->GetLastStatus(),"GDI+ construction failed");}
        catch(...) {NativeCleanup([&]{value->~T();return 0;});value=nullptr;throw;}
    }
    SafeGdi(const SafeGdi&)=delete;
    ~SafeGdi() noexcept {if(value)NativeCleanup([&]{value->~T();return 0;});}
    T& get() noexcept {return *value;}
};
#ifdef VIBEDOTCOM_TEST
int g_failAllocationAfter=-1;
#endif
void ThrowIfInjected() {
#ifdef VIBEDOTCOM_TEST
    if(g_failAllocationAfter==0)throw std::bad_alloc();
    if(g_failAllocationAfter>0)--g_failAllocationAfter;
#endif
    CheckStop();
}
// GdiplusBase::new can return null even for a throwing new-expression. Bypass
// BOTH its allocator and deleter; never invoke the wrapper's unsafe Clone().
template<class T> struct GdiDelete {void operator()(T* p) const noexcept {
    if(p){NativeCleanup([&]{p->~T();return 0;});::operator delete(p);}
}};
template<class T> using GdiPtr=std::unique_ptr<T,GdiDelete<T>>;
template<class T> struct ComRelease {void operator()(T* p) const noexcept {if(p)NativeCleanup([&]{return p->Release();});}};
template<class T> using ComPtr=std::unique_ptr<T,ComRelease<T>>;
template<class T> struct ComSlot {
    T* value=nullptr;
    ~ComSlot() noexcept {ComRelease<T>{}(value);}
    ComPtr<T> take() noexcept {return ComPtr<T>(std::exchange(value,nullptr));}
};
template<class T,class... Args> GdiPtr<T> MakeGdi(Args&&... args) {
    ThrowIfInjected();void* storage=::operator new(sizeof(T));GdiPtr<T> object;
    try {object.reset(Native([&]{return ::new(storage) T(std::forward<Args>(args)...);}));}
    catch(...) {::operator delete(storage);throw;}
    Require(object!=nullptr,"GDI+ wrapper allocation");GdiCheck(object->GetLastStatus(),"GDI+ construction failed");return object;
}
struct PixelLock {
    Bitmap& bitmap;BitmapData data{};bool locked=false;
    PixelLock(Bitmap& b,int w,int h):bitmap(b) {
        Gdiplus::Rect rect(0,0,w,h);GdiCheck(bitmap.LockBits(&rect,ImageLockModeRead,PixelFormat32bppPARGB,&data),"Read bitmap pixels");locked=true;
        if(!data.Scan0||std::abs(int64_t(data.Stride))<int64_t(w)*4){NativeCleanup([&]{return bitmap.UnlockBits(&data);});locked=false;throw Error("Invalid pixel stride");}
    }
    ~PixelLock(){if(locked)NativeCleanup([&]{return bitmap.UnlockBits(&data);});}
    void Close(){locked=false;auto status=Native([&]{return bitmap.UnlockBits(&data);});GdiCheck(status,"Unlock bitmap pixels");}
};
GdiPtr<Bitmap> CopyBitmap(Bitmap& source,int x,int y,int w,int h) {
    auto copy=MakeGdi<Bitmap>(w,h,PixelFormat32bppPARGB);SafeGdi<Graphics> owner(copy.get());auto& g=owner.get();
    GdiCheck(g.SetCompositingMode(CompositingModeSourceCopy));
    GdiCheck(g.SetInterpolationMode(InterpolationModeNearestNeighbor));GdiCheck(g.SetPixelOffsetMode(PixelOffsetModeHalf));
    GdiCheck(g.DrawImage(&source,Gdiplus::Rect(0,0,w,h),x,y,w,h,UnitPixel));return copy;
}
std::string Sha256(const std::string& bytes) {
    BCRYPT_ALG_HANDLE alg=nullptr;Require(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)==0&&alg!=nullptr,"SHA256 provider");
    std::array<UCHAR,32> digest{};auto status=BCryptHash(alg,nullptr,0,reinterpret_cast<PUCHAR>(const_cast<char*>(bytes.data())),static_cast<ULONG>(bytes.size()),digest.data(),static_cast<ULONG>(digest.size()));
    BCryptCloseAlgorithmProvider(alg,0);Require(status==0,"SHA256 failed");std::string out;constexpr char hex[]="0123456789abcdef";for(auto b:digest){out+=hex[b>>4];out+=hex[b&15];}return out;
}
GdiPtr<Bitmap> Decode(const std::string& bytes,const Sheet& sheet) {
    Require(bytes.size()>=33&&bytes.compare(0,8,"\x89PNG\r\n\x1a\n",8)==0&&bytes.compare(12,4,"IHDR")==0,"Expected PNG");
    auto be=[&](size_t p){uint32_t n=0;for(size_t i=p;i<p+4;++i)n=(n<<8)|static_cast<unsigned char>(bytes[i]);return n;};
    Require(be(16)==static_cast<unsigned>(sheet.w)&&be(20)==static_cast<unsigned>(sheet.h)&&bytes[24]==8&&bytes[26]==0&&bytes[27]==0&&bytes[28]==0,"PNG metadata mismatch");
    Require(Sha256(bytes)==sheet.sha,"PNG hash mismatch");
    HGLOBAL memory=GlobalAlloc(GMEM_MOVEABLE,bytes.size());Require(memory!=nullptr,"PNG memory allocation");
    void* dest=GlobalLock(memory);if(!dest){GlobalFree(memory);throw Error("PNG memory lock");}memcpy(dest,bytes.data(),bytes.size());GlobalUnlock(memory);
    IStream* stream=nullptr;auto hr=CreateStreamOnHGlobal(memory,TRUE,&stream);if(FAILED(hr)){if(stream)ComRelease<IStream>{}(stream);else GlobalFree(memory);throw Error("PNG stream");}
    ComPtr<IStream> release(stream);
    if(!stream){GlobalFree(memory);throw Error("PNG stream null");}
    GdiPtr<Bitmap> image;
    { SafeGdi<Bitmap> owner(stream,FALSE);auto& decoded=owner.get();
      GdiCheck(decoded.GetLastStatus(),"PNG decode failed");auto w=Native([&]{return decoded.GetWidth();});GdiCheck(decoded.GetLastStatus());auto h=Native([&]{return decoded.GetHeight();});GdiCheck(decoded.GetLastStatus());
      Require(w==static_cast<unsigned>(sheet.w)&&h==static_cast<unsigned>(sheet.h),"PNG decode dimensions");image=CopyBitmap(decoded,0,0,sheet.w,sheet.h); }
    Require(image!=nullptr,"PNG decode null");return image;
}
Color HexColor(const std::string& s,BYTE alpha=255) {unsigned value=0;std::from_chars(s.data()+1,s.data()+s.size(),value,16);return Color(alpha,(value>>16)&255,(value>>8)&255,value&255);}
Color ProviderColor(const Agent& a,BYTE alpha=255) {return HexColor(a.provider=="codex"?"#329b91":a.provider=="claude"?"#d57e4e":"#827ed1",alpha);}
struct Camera {double zoom=1,x=0,y=0;};
Camera ViewCamera(const Snapshot& s,int width,int height,int requested) {
    double fit=std::min(std::max(1,width-20)/double(s.w*16),std::max(1,height-(s.schema>=4?40:16))/double(s.h*16));
    double z=requested?std::clamp(requested,1,6):fit>=1?std::min(6.0,std::floor(fit)):std::max(.01,fit);
    return {z,std::nearbyint((width-s.w*16*z)/2.0),std::nearbyint((height-s.h*16*z)/2.0)};
}
class Renderer {
    struct Image {std::wstring path;std::string sha;std::shared_ptr<Bitmap> pixels;};
    std::map<std::string,Image> images;
    using CropKey=std::tuple<std::string,int,int,int,int>;
    std::map<CropKey,GdiPtr<Bitmap>> crops;
    uint64_t cropBytes=0;
    std::map<CropKey,int> tops;
    std::map<CropKey,Bounds> content;
    std::string loadedManifest;
    std::wstring loadedRoot;
    GdiPtr<FontFamily> family;
    std::map<int,GdiPtr<Font>> fonts;
    ComPtr<IDWriteFactory> textFactory;
    ComPtr<IDWriteTextFormat> tagFormat;
    std::map<std::wstring,double> nameWidths;
    double NameWidth(const std::wstring& name) {
        if(auto it=nameWidths.find(name);it!=nameWidths.end())return it->second;
        if(!textFactory){ThrowIfInjected();ComSlot<IDWriteFactory> slot;
            auto status=Native([&]{return DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),reinterpret_cast<IUnknown**>(&slot.value));});textFactory=slot.take();
            Require(SUCCEEDED(status)&&textFactory!=nullptr,"Text factory allocation");}
        if(!tagFormat){ThrowIfInjected();ComSlot<IDWriteTextFormat> slot;
            auto status=Native([&]{return textFactory->CreateTextFormat(L"Segoe UI",nullptr,DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,DWRITE_FONT_STRETCH_NORMAL,7,L"en-us",&slot.value);});tagFormat=slot.take();
            Require(SUCCEEDED(status)&&tagFormat!=nullptr,"Tag format allocation");}
        // WPF measures ideal glyph advances. GDI+ grid fitting inflates plate
        // widths and changes collision stacking, especially in overflow scenes.
        ThrowIfInjected();ComSlot<IDWriteTextLayout> slot;
        auto status=Native([&]{return textFactory->CreateTextLayout(name.c_str(),static_cast<UINT32>(name.size()),tagFormat.get(),500,32,&slot.value);});auto layout=slot.take();
        Require(SUCCEEDED(status)&&layout!=nullptr,"Tag layout allocation");DWRITE_TEXT_METRICS metrics{};
        Require(SUCCEEDED(Native([&]{return layout->GetMetrics(&metrics);}))&&std::isfinite(metrics.widthIncludingTrailingWhitespace),"Tag metrics");
        double width=metrics.widthIncludingTrailingWhitespace;
        if(nameWidths.size()>=256)nameWidths.clear();nameWidths.emplace(name,width);return width;
    }
    static void Configure(Graphics& g) {GdiCheck(g.GetLastStatus());GdiCheck(g.SetSmoothingMode(SmoothingModeNone));GdiCheck(g.SetInterpolationMode(InterpolationModeNearestNeighbor));GdiCheck(g.SetPixelOffsetMode(PixelOffsetModeHalf));GdiCheck(g.SetCompositingQuality(CompositingQualityHighSpeed));}
    static BYTE Alpha(double opacity) {return static_cast<BYTE>(std::clamp(opacity,0.0,1.0)*255+.5);}
    void Rect(Graphics& g,const std::string& color,double x,double y,double w,double h,double opacity=1) {
        if(w<=0||h<=0)return;SafeGdi<SolidBrush> owner(HexColor(color,Alpha(opacity)));auto& b=owner.get();GdiCheck(g.FillRectangle(&b,static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(w),static_cast<REAL>(h)));
    }
    void Text(Graphics& g,const std::string& value,double x,double y,int size,const std::string& color="#394940",bool bold=false,double width=500,bool measured=false) {
        if(!family)family=MakeGdi<FontFamily>(L"Segoe UI");int key=size*2+(bold?1:0);if(!fonts.contains(key))fonts.emplace(key,MakeGdi<Font>(family.get(),static_cast<REAL>(size),bold?FontStyleBold:FontStyleRegular,UnitPixel));
        SafeGdi<SolidBrush> brushOwner(HexColor(color));auto& brush=brushOwner.get();SafeGdi<StringFormat> formatOwner(StringFormatFlagsNoWrap|StringFormatFlagsNoFitBlackBox|StringFormatFlagsMeasureTrailingSpaces);auto& format=formatOwner.get();
        GdiCheck(format.GetLastStatus());auto text=Wide(value);
        if(measured) {
            // The plate already uses ideal advances. GDI+'s layout rectangle
            // reserves extra overhang padding and prematurely ellipsizes even
            // short names. Trim by the same metrics, then draw the whole run.
            if(NameWidth(text)>width) {
                auto prefix=text;
                do {if(prefix.empty())break;prefix.pop_back();if(!prefix.empty()&&prefix.back()>=0xD800&&prefix.back()<=0xDBFF)prefix.pop_back();text=prefix+L"\u2026";}while(NameWidth(text)>width);
            }
            GdiCheck(format.SetFormatFlags(format.GetFormatFlags()|StringFormatFlagsNoClip));
            GdiCheck(g.DrawString(text.c_str(),static_cast<INT>(text.size()),fonts.at(key).get(),PointF(static_cast<REAL>(x),static_cast<REAL>(y)),&format,&brush));
        } else {
            GdiCheck(format.SetTrimming(StringTrimmingEllipsisCharacter));
            GdiCheck(g.DrawString(text.c_str(),static_cast<INT>(text.size()),fonts.at(key).get(),RectF(static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(std::max(1.0,width)),static_cast<REAL>(size*2+4)),&format,&brush));
        }
    }
    Bitmap& Crop(const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=crops.find(key);if(it!=crops.end())return *it->second;
        uint64_t bytes=uint64_t(f.w)*f.h*4;
        if(crops.size()>=512||cropBytes+bytes>64*1024*1024){crops.clear();tops.clear();content.clear();cropBytes=0;}
        auto bitmap=CopyBitmap(*images.at(f.sheet).pixels,f.x,f.y,f.w,f.h);
        auto* ptr=bitmap.get();crops.emplace(key,std::move(bitmap));cropBytes+=bytes;return *ptr;
    }
    void Sprite(Graphics& g,const Frame& f,const Bounds& r,double opacity=1) {
        SafeGdi<ImageAttributes> owner;auto& attributes=owner.get();ColorMatrix matrix={{{1,0,0,0,0},{0,1,0,0,0},{0,0,1,0,0},{0,0,0,static_cast<REAL>(opacity),0},{0,0,0,0,1}}};
        GdiCheck(attributes.GetLastStatus());GdiCheck(attributes.SetColorMatrix(&matrix));GdiCheck(attributes.SetWrapMode(WrapModeTileFlipXY));auto& bitmap=Crop(f);
        GdiCheck(g.DrawImage(&bitmap,RectF(static_cast<REAL>(r.x),static_cast<REAL>(r.y),static_cast<REAL>(r.w),static_cast<REAL>(r.h)),0,0,static_cast<REAL>(f.w),static_cast<REAL>(f.h),UnitPixel,&attributes));
    }
    double HeadY(const Agent& a,const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=tops.find(key);
        if(it==tops.end()) {
            auto& bitmap=Crop(f);PixelLock lock(bitmap,f.w,f.h);int top=0;
            bool found=false;for(int y=0;y<f.h&&!found;++y){auto* p=static_cast<BYTE*>(lock.data.Scan0)+ptrdiff_t(y)*lock.data.Stride;for(int x=0;x<f.w;++x)if(p[x*4+3]>32){top=y;found=true;break;}}
            lock.Close();it=tops.emplace(key,top).first;
        }return AgentBounds(a,f).y+it->second*f.scale;
    }
    Bounds ContentBounds(const Agent& a,const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=content.find(key);
        if(it==content.end()) {
            auto& bitmap=Crop(f);PixelLock lock(bitmap,f.w,f.h);
            int l=f.w,t=f.h,r=0,b=0;for(int y=0;y<f.h;++y){auto* row=static_cast<BYTE*>(lock.data.Scan0)+ptrdiff_t(y)*lock.data.Stride;for(int x=0;x<f.w;++x)if(row[x*4+3]>32){l=std::min(l,x);t=std::min(t,y);r=std::max(r,x+1);b=std::max(b,y+1);}}
            lock.Close();Bounds bounds=r>l?Bounds{double(l),double(t),double(r-l),double(b-t)}:Bounds{0,0,double(f.w),double(f.h)};it=content.emplace(key,bounds).first;
        }
        auto bounds=AgentBounds(a,f);return {bounds.x+it->second.x*f.scale,bounds.y+it->second.y*f.scale,it->second.w*f.scale,it->second.h*f.scale};
    }
    void Room(Graphics& g,const Snapshot& s,bool transparent) {
        CheckStop();
        // Draw directly at the final camera scale. A 1x room raster loses
        // authored odd source pixels before a 2x view (e.g. 32px wall outlines).
        if(!transparent) {
            for(int y=0;y<s.h;++y)for(int x=0;x<s.w;++x){if(x==0)CheckStop();const auto& t=s.types.at(s.tiles[y*s.w+x]);if(t.kind=="void")continue;
                if(!t.sheet.empty()&&!t.animation.empty()){auto f=SelectFrame(s.sheets.at(t.sheet),t.animation,"any",0);Sprite(g,f,{double(x*16),double(y*16),16,16});}
                else {Rect(g,t.color,x*16,y*16,16,16);if(t.kind=="floor"){Rect(g,t.id==2?"#c9d1b8":"#d9c6a8",x*16,y*16+15,16,1);if((x+y)%3==0)Rect(g,"#d9c6a8",x*16,y*16,1,15);}}
            }
            if(!s.cozy)for(const auto& z:s.zones)if(z.name=="workspace"||z.name=="pantry") {Rect(g,"#eee5d2",z.x*16+24,24,std::min(72,z.w*16-24),11);std::string label=z.name;for(auto& c:label)c=static_cast<char>(toupper(static_cast<unsigned char>(c)));Text(g,label,z.x*16+26,25,7,"#59654c",true,z.w*16-28);}
        }
        if(std::none_of(s.furniture.begin(),s.furniture.end(),[](const Furniture& f){return f.kind=="door";})) {
            Rect(g,"#58675b",s.doorX*16-16,s.doorY*16-18,15,46);Rect(g,"#88a38e",s.doorX*16-14,s.doorY*16-16,11,42);Rect(g,"#f1d293",s.doorX*16-6,s.doorY*16+8,2,2);
        }
    }
    void FallbackFurniture(Graphics& g,const Furniture& f) {
        int x=f.x*16,y=f.y*16,w=f.w*16,h=f.h*16;
        auto r=[&](const char* c,int dx,int dy,int rw,int rh){Rect(g,c,x+dx,y+dy,rw,rh);};
        if(f.kind=="desk") {
            r("#cbb898",2,27,w,7);r("#806b55",3,25,3,9);r("#806b55",w-6,25,3,9);r("#ba8e65",0,15,w,13);r("#e2b784",0,13,w,11);r("#f5d5a6",0,13,w,2);
            auto monitor=f.monitor.empty()?f.state:f.monitor;r("#414e51",11,1,25,15);r(monitor=="working"?"#82b9a6":"#263d40",13,3,21,11);
            if(monitor=="working"){r("#eaf4c7",15,6,10,1);r("#eaf4c7",15,9,14,1);}
            r("#414e51",22,16,3,3);r("#f8f0d9",13,20,19,3);r("#bca47e",14,21,1,1);r("#bca47e",18,21,1,1);r("#bca47e",22,21,1,1);r("#f7ecd5",3,17,5,5);r("#ae784e",4,17,3,1);r("#516768",13,37,21,6);r("#749390",15,34,17,5);r("#516768",22,42,3,5);
        } else if(f.kind=="pantry") {
            r("#7e927a",0,0,w,h);r("#ece0b9",-1,0,w+2,6);for(int k=3;k<w;k+=24){r("#adbc95",k,9,20,20);r("#6b805f",k+15,14,2,2);}
            r("#424a43",8,-15,17,18);r("#778276",10,-13,13,5);r("#ddad65",12,-7,9,3);r("#3d433e",12,-3,9,3);r("#fbf1d9",15,-1,4,4);r("#566457",44,-2,26,5);r("#a6c4c0",46,-1,22,2);r("#8f9a88",62,-9,2,8);r("#8f9a88",58,-9,6,2);r("#f8f0df",32,-6,6,6);r("#ca945d",33,-6,4,1);
        } else if(f.kind=="water") {
            r("#88afbf",3,-9,10,13);r("#c3e1e5",5,-8,6,9);r("#778a93",5,3,6,3);r("#e7eadb",1,6,14,24);r("#b6c8bf",3,8,10,11);r("#467dae",4,9,2,3);r("#d18771",10,9,2,3);r("#647b7b",4,18,8,2);
        } else if(f.kind=="tv") {
            r("#445259",0,-12,w,30);r("#222f3b",3,-9,w-6,24);r("#89b8a9",4,-8,w-8,20);r("#4e7f78",4,3,w-8,9);r("#e77862",w/2-9,-3,18,12);r("#fff4df",w/2-2,0,3,6);r("#596760",28,18,8,5);r("#596760",18,22,28,3);
        } else if(f.kind=="sofa") {
            r("#496a60",0,-4,w,h+8);r("#86a88b",4,-3,w-8,7);r("#729c7c",5,5,w-10,10);r("#4f7766",w/2,5,2,10);r("#567764",0,1,6,17);r("#567764",w-6,1,6,17);r("#dfc7a0",9,1,11,9);r("#aabfa0",w-23,2,12,8);
        } else if(f.kind=="table") {
            r("#a18462",4,6,w-8,10);r("#e1ba83",0,0,w,8);r("#f4d3a1",0,0,w,2);r("#f8efda",10,-4,5,5);r("#b58156",11,-4,3,1);r("#f8efda",32,-4,5,5);
        } else if(f.kind=="plant") {
            r("#bc885f",3,7,11,10);r("#e0ac7c",2,6,13,3);r("#476a52",7,-9,2,17);r("#648764",-1,-7,9,5);r("#90af77",8,-12,8,6);r("#759c68",2,-16,9,6);
        } else r("#879982",0,0,w,h);
    }
    void Identity(Graphics& g,const Agent& a,const Frame& f,bool offline,double seconds) {
        double x=std::nearbyint((a.x+.5)*16),y=HeadY(a,f)-2;
        // A single-frame idle pack still gets a one-pixel breathing motion while
        // disconnected. It does not change pose/collision/scene sorting.
        if(offline&&static_cast<int>(std::fmod(seconds,2)))y+=1;
        double o=a.opacity;Rect(g,"#392b24",x-7,y-5,14,7,o);SafeGdi<SolidBrush> owner(ProviderColor(a,Alpha(o)));auto& accent=owner.get();GdiCheck(g.FillRectangle(&accent,static_cast<REAL>(x-6),static_cast<REAL>(y-4),12.0f,5.0f));
        if(!a.sub){Rect(g,"#392b24",x-5,y-10,10,6,o);Rect(g,"#f4c764",x-4,y-7,8,3,o);Rect(g,"#f4c764",x-4,y-9,2,2,o);Rect(g,"#f4c764",x-1,y-10,2,3,o);Rect(g,"#f4c764",x+2,y-9,2,2,o);}else Rect(g,"#fff2d2",x-2,y-3,4,3,o);
    }
    void Bubble(Graphics& g,const Snapshot& s,const Agent& a,const Frame& f,int ambient) {
        if(s.schema>=4&&a.queued)return;
        if(s.sheets.contains("icons")) {
            std::string clip=!a.reaction.empty()?"bubble-reaction":a.action=="napping"?"bubble-napping":a.action=="waiting"?"bubble-waiting":a.action=="thinking"?"bubble-thinking":a.action=="reporting"?"bubble-done":a.action=="typing"?"bubble-working":"";
            if(clip.empty())return;auto b=SelectFrame(s.sheets.at("icons"),clip,"any",0);bool seated=a.workLocation=="desk"&&(a.action=="typing"||a.action=="thinking"||a.action=="waiting");
            Sprite(g,b,{std::nearbyint((a.x+.5)*16+(s.schema>=4?15:8)),HeadY(a,f)-(s.schema>=4?(seated?16:42):30),s.schema>=4?24:b.w*b.scale,s.schema>=4?24:b.h*b.scale},a.opacity);return;
        }
        int bubble=!a.reaction.empty()?5:a.action=="napping"?6:a.action=="chatting"&&!a.partner.empty()?7:a.action=="coffee"&&ambient%4==0?8:a.action=="waiting"?5:a.action=="typing"&&!a.toolKind.empty()?a.toolKind=="shell"?0:a.toolKind=="file"?1:a.toolKind=="search"?2:a.toolKind=="agent"?3:4:-1;
        if(bubble<0)return;Frame b{"fallback",6,bubble,bubble*16,144,16,24,8,24,1,0,0};Sprite(g,b,{(a.x+.5)*16+(s.schema>=4?15:-8),AgentBounds(a,f).y-(s.schema>=4?41:17),16,24},a.opacity);
    }
    Bounds Tag(Graphics& g,const Snapshot& s,const Agent& a,double x,double y,std::vector<Bounds>& plates,double visibleTop) {
        auto name=Wide(a.displayName.empty()?a.name:a.displayName);
        // Core supplies Unicode text-element truncation. Preserve that label.
        if(a.displayName.empty()&&name.size()>16){name.resize(15);if(name.back()>=0xD800&&name.back()<=0xDBFF)name.pop_back();name+=L"…";}
        double width=std::clamp(std::ceil(NameWidth(name))+18,32.0,132.0);Bounds plate{std::clamp(x-width/2,2.0,std::max(2.0,s.w*16-width-2)),std::max(y,visibleTop),width,15};
        auto overlaps=[&](const Bounds& p){return p.x<=plate.x+plate.w&&p.x+p.w>=plate.x&&p.y<=plate.y+plate.h&&p.y+p.h>=plate.y;};
        for(int attempt=0;attempt<8&&plate.y-16>=visibleTop&&std::any_of(plates.begin(),plates.end(),overlaps);++attempt)plate.y-=16;plates.push_back(plate);
        Rect(g,a.sub?"#738b84":"#c99a52",plate.x-1,plate.y-1,plate.w+2,plate.h+2);Rect(g,"#172b30",plate.x,plate.y,plate.w,plate.h);
        if(s.sheets.contains("provider-icons")){auto f=SelectFrame(s.sheets.at("provider-icons"),a.providerIcon.empty()?a.provider:a.providerIcon,"any",0);Sprite(g,f,{plate.x+1,plate.y+1,12,12});}
        else {
            static constexpr const char* codex[]{"000001110000","000110011000","001100001100","011001100110","010011110010","110110011011","110110011011","010011110010","011001100110","001100001100","000110011000","000001110000"};
            static constexpr const char* claude[]{"000001000000","010001001000","001001010010","100101010100","011111111000","000111111111","111111110000","000111111110","001010101001","010010100100","000010001010","000010000000"};
            static constexpr const char* anti[]{"000001000000","000001100000","000001100000","000011110000","000111111000","111111111111","011111111110","000111111000","000011110000","000001100000","000001100000","000001000000"};
            auto provider=a.providerIcon.empty()?a.provider:a.providerIcon;auto rows=provider=="codex"?codex:provider=="claude"?claude:anti;
            for(int yy=0;yy<12;++yy)for(int xx=0;xx<12;++xx)if(rows[yy][xx]=='1')Rect(g,"#101c24",plate.x+xx,plate.y+yy,3,3);
            for(int yy=0;yy<12;++yy)for(int xx=0;xx<12;++xx)if(rows[yy][xx]=='1')Rect(g,provider=="codex"?"#329b91":provider=="claude"?"#d57e4e":"#827ed1",plate.x+1+xx,plate.y+1+yy,1,1);
        }
        Text(g,Utf8Text(name),plate.x+15,plate.y+3,7,"#f5edce",false,plate.w-16,true);
        return plate;
    }
    Bounds LaptopBounds(const Agent& a,const Frame& f) {auto body=ContentBounds(a,f);return {std::nearbyint(body.x+body.w/2-8),std::nearbyint(body.y+body.h-12),16,16};}
public:
    void Load(const Snapshot& s,const std::wstring& root) {
        std::map<std::string,Image> candidate;uint64_t pixels=0;bool changed=false;
        for(const auto& [id,sh]:s.sheets) {
            CheckStop();
            pixels+=uint64_t(sh.w)*sh.h;Require(pixels<=32*1024*1024,"Decoded pack exceeds 128 MiB");auto path=SheetPath(root,sh.file);
            auto old=images.find(id);if(old!=images.end()&&old->second.path==path&&old->second.sha==sh.sha){
                Require(old->second.pixels!=nullptr,"Cached image null");auto w=Native([&]{return old->second.pixels->GetWidth();});GdiCheck(old->second.pixels->GetLastStatus());auto h=Native([&]{return old->second.pixels->GetHeight();});GdiCheck(old->second.pixels->GetLastStatus());
                Require(w==static_cast<unsigned>(sh.w)&&h==static_cast<unsigned>(sh.h),"Cached PNG dimensions mismatch");candidate.emplace(id,old->second);continue;}
            auto bytes=ReadFileBytes(path,32*1024*1024,root);auto image=Decode(bytes,sh);candidate.emplace(id,Image{path,sh.sha,std::move(image)});changed=true;
        }
        // Commit only after every referenced sheet has decoded and matched SHA256.
        changed|=images.size()!=s.sheets.size();auto manifest=s.rawAssets;auto nextRoot=root;images.swap(candidate);
        if(changed){crops.clear();tops.clear();content.clear();cropBytes=0;}
        loadedManifest.swap(manifest);loadedRoot.swap(nextRoot);
    }
    GdiPtr<Bitmap> Render(const Snapshot& s,int width,int height,int zoom,bool transparent,bool offline,double time,const std::string& hovered={}) {
        Require(width>=1&&height>=1&&width<=4096&&height<=4096&&int64_t(width)*height<=8388608,"Viewport bounds");
        auto output=MakeGdi<Bitmap>(width,height,PixelFormat32bppPARGB);
        SafeGdi<Graphics> owner(output.get());auto& g=owner.get();Configure(g);if(s.schema>=4)GdiCheck(g.SetTextRenderingHint(TextRenderingHintSingleBitPerPixelGridFit));GdiCheck(g.Clear(transparent?Color(0,0,0,0):HexColor(s.schema>=4?"#10282b":"#302d2b")));
        const auto camera=ViewCamera(s,width,height,zoom);GdiCheck(g.TranslateTransform(static_cast<REAL>(camera.x),static_cast<REAL>(camera.y)));GdiCheck(g.ScaleTransform(static_cast<REAL>(camera.zoom),static_cast<REAL>(camera.zoom)));Room(g,s,transparent);
        std::map<std::string,const Agent*> byId;for(const auto& a:s.agents)byId.emplace(a.id,&a);
        SafeGdi<Pen> lineOwner(s.schema>=4?HexColor("#5cbbb5",64):HexColor("#9bab9e"),s.schema>=4?.6f:.7f);auto& line=lineOwner.get();GdiCheck(line.SetDashStyle(DashStyleDot));
        if(!offline)for(const auto& a:s.agents)if(a.visible&&byId.contains(a.parent)) {
            const auto& p=*byId.at(a.parent);if(!p.visible)continue;
            if(s.schema>=4){if(a.state!="working"&&a.state!="reporting"&&hovered!=a.id&&hovered!=p.id)continue;double x=(a.x+.5)*16,y=(a.y+1)*16-5,dx=(p.x-a.x)*16,dy=(p.y-a.y)*16,distance=std::hypot(dx,dy);if(distance>64||distance<12)continue;double scale=std::min(1.0,40/distance);GdiCheck(g.DrawLine(&line,static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(x+dx*scale),static_cast<REAL>(y+dy*scale)));}
            else if(a.action=="entering"||a.action=="walking"||a.action=="pickup"||a.action=="reporting")GdiCheck(g.DrawLine(&line,static_cast<REAL>((a.x+.5)*16),static_cast<REAL>((a.y+.3)*16),static_cast<REAL>((p.dx+.5)*16),static_cast<REAL>((p.dy+.3)*16)));
        }
        for(const auto& d:Scene(s)) {
            CheckStop();
            if(!d.agent){const auto& f=s.furniture[d.index];if(f.hasFrame)Sprite(g,f.frame,{f.x*16-f.frame.ax*f.frame.scale+f.frame.ox,f.y*16-f.frame.ay*f.frame.scale+f.frame.oy,f.frame.w*f.frame.scale,f.frame.h*f.frame.scale});else FallbackFurniture(g,f);}
            else {const auto& a=s.agents[d.index];auto f=DisplayFrame(s,a,offline,time);auto bounds=AgentBounds(a,f);if(offline&&static_cast<int>(std::fmod(time,2)))bounds.y+=1;Sprite(g,f,bounds,a.opacity);

                if(!offline&&a.carrying&&(a.action=="walking"||a.action=="entering"))Rect(g,"#fff9e7",(a.x+.5)*16+(a.facing=="left"?-10:4),(a.y+1)*16-12,6,8,a.opacity);}
        }
        std::vector<Bounds> laptopPlates;
        if(s.schema>=4&&!offline)for(const auto& a:s.agents)if(a.visible&&a.workLocation=="overflow"&&(a.action=="typing"||a.action=="thinking"||a.action=="waiting")) {
            auto bounds=LaptopBounds(a,a.frame);laptopPlates.push_back(bounds);
            if(s.sheets.contains("office-extras")){auto laptop=SelectFrame(s.sheets.at("office-extras"),"laptop","any",0);Sprite(g,laptop,bounds,a.opacity);}
            else {double x=bounds.x-2,y=bounds.y+2;Rect(g,"#19292d",x,y,19,11,a.opacity);Rect(g,"#8fd7c4",x+2,y+2,15,6,a.opacity);Rect(g,"#bec9be",x-1,y+10,21,3,a.opacity);}
        }
        for(const auto& desk:s.furniture)if(byId.contains(desk.owner)) {
            const auto& a=*byId.at(desk.owner);int x=desk.x*16,y=desk.y*16;SafeGdi<SolidBrush> owner(ProviderColor(a));auto& b=owner.get();GdiCheck(g.FillRectangle(&b,x,y+desk.h*16-2,5,2));
            if(a.paper&&(a.deskId.empty()||a.deskId==desk.deskId)){Rect(g,"#fff9e7",x+desk.w*16-12,y+18,9,7);Rect(g,"#a19a7e",x+desk.w*16-11,y+20,6,1);}
            if(s.schema==3){double labelY=(a.dy+1)*16+5;Rect(g,"#f4e2bd",x-2,labelY,std::min(desk.w*16+8,static_cast<int>(Wide(a.name).size())*4+8),10);Text(g,a.name,x+1,labelY,7,"#392b24",false,desk.w*16+8);}
        }
        for(const auto& a:s.agents)if(a.visible){auto f=DisplayFrame(s,a,offline,time);if(s.schema==3)Identity(g,a,f,offline,time);if(!offline)Bubble(g,s,a,f,static_cast<int>(std::fmod(time*2,120)));}
        if(s.schema>=4){
            std::vector<std::pair<double,const Agent*>> labels;
            for(const auto& a:s.agents)if(a.visible)labels.emplace_back(HeadY(a,DisplayFrame(s,a,offline,time)),&a);
            std::sort(labels.begin(),labels.end(),[](const auto& a,const auto& b){return a.first!=b.first?a.first<b.first:a.second->ordinal<b.second->ordinal;});
            std::vector<Bounds> plates=laptopPlates,queuePlates;double visibleTop=-camera.y/camera.zoom+1;
            for(const auto& [head,a]:labels){bool seated=a->workLocation=="desk"&&(a->action=="typing"||a->action=="thinking"||a->action=="waiting");
                auto plate=Tag(g,s,*a,std::nearbyint((a->x+.5)*16),head-(seated?34:17)+(offline&&static_cast<int>(std::fmod(time,2))?1:0),plates,visibleTop);
                if(a->queued&&!offline)queuePlates.push_back(plate);}
            for(const auto& desk:s.furniture)if(byId.contains(desk.owner)){const auto& a=*byId.at(desk.owner);bool besideDesk=std::abs(a.x-a.dx)+std::abs(a.y-a.dy)<=1.5;
                if(besideDesk&&(a.state=="working"||a.state=="thinking"||a.state=="waiting"))continue;
                Tag(g,s,a,desk.x*16+desk.w*8,desk.y*16+desk.h*16-13,plates,visibleTop);}
            for(const auto& plate:queuePlates){double x=plate.x+plate.w+3,y=plate.y+2;Rect(g,"#738b84",x-1,y-1,17,12);Rect(g,"#172b30",x,y,15,10);Text(g,"...",x+2,y-2,8,"#f5edce");}
        }
        if(s.agents.empty()){Text(g,"A quiet moment.",30,132,10,"#394940",true);Text(g,"Your agents will walk in.",30,151,7,"#394940",false,95);}
        GdiCheck(g.ResetTransform());if(offline){Rect(g,"#303d40",8,8,144,24);Rect(g,"#e6ad66",15,14,10,10);Rect(g,"#303d40",19,16,2,4);Rect(g,"#303d40",19,21,2,1);Text(g,"tracker offline",32,11,12,"#fff4df");}
        GdiCheck(g.GetLastStatus(),"GDI+ render failed");return output;
    }
    std::vector<std::pair<Bounds,size_t>> HitBoxes(const Snapshot& s,int width,int height,int zoom,bool offline,double time) {
        auto c=ViewCamera(s,width,height,zoom);std::vector<std::pair<Bounds,size_t>> boxes;auto scene=Scene(s);
        for(auto it=scene.rbegin();it!=scene.rend();++it)if(it->agent){const auto& a=s.agents[it->index];if(a.opacity<=0)continue;
            auto f=DisplayFrame(s,a,offline,time);auto b=s.schema>=4?ContentBounds(a,f):AgentBounds(a,f);
            if(offline&&static_cast<int>(std::fmod(time,2)))b.y+=1;
            boxes.emplace_back(Bounds{c.x+b.x*c.zoom,c.y+b.y*c.zoom,b.w*c.zoom,b.h*c.zoom},it->index);}
        return boxes;
    }
    const Agent* Hit(const Snapshot& s,int width,int height,int zoom,double px,double py,bool offline,double time) {
        auto c=ViewCamera(s,width,height,zoom);double x=(px-c.x)/c.zoom,y=(py-c.y)/c.zoom;auto scene=Scene(s);
        for(auto it=scene.rbegin();it!=scene.rend();++it)if(it->agent){const auto& a=s.agents[it->index];auto f=DisplayFrame(s,a,offline,time);auto bounds=s.schema>=4?ContentBounds(a,f):AgentBounds(a,f);if(offline&&static_cast<int>(std::fmod(time,2)))bounds.y+=1;if(a.opacity>0&&bounds.Contains(x,y))return &a;}return nullptr;
    }
};
std::string Tooltip(const Agent& a,double now) {
    auto task=a.task.empty()?"No task recorded":a.task;auto w=Wide(task);if(w.size()>150){w.resize(147);if(w.back()>=0xD800&&w.back()<=0xDBFF)w.pop_back();w+=L"...";int n=WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),nullptr,0,nullptr,nullptr);task.resize(n);WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),task.data(),n,nullptr,nullptr);}
    return a.provider+" · "+a.name+"\n"+(a.sub?"Sub-agent":"Supervisor")+(a.role.empty()?"":" · "+a.role)+"\nModel   "+a.model+"\nProject   "+a.project+"\nTask   "+task+"\nTool   "+a.tool+"\n"+a.state+" · "+std::to_string(static_cast<int64_t>(std::max(0.0,now-a.since)))+"s · "+a.action;
}
void SavePng(Bitmap& image,const std::wstring& path) {
    UINT count=0,size=0;Require(Native([&]{return GetImageEncodersSize(&count,&size);})==Ok&&count>0&&size>=sizeof(ImageCodecInfo)*count,"PNG encoders");std::vector<BYTE> buffer(size);auto* encoders=reinterpret_cast<ImageCodecInfo*>(buffer.data());Require(Native([&]{return GetImageEncoders(count,size,encoders);})==Ok,"PNG encoder list");
    for(UINT i=0;i<count;++i)if(encoders[i].MimeType&&wcscmp(encoders[i].MimeType,L"image/png")==0){GdiCheck(image.Save(path.c_str(),&encoders[i].Clsid,nullptr),"Save PNG");return;}throw Error("PNG encoder missing");
}
// ===== END SHARED GDI+ RENDERER =====

uint64_t VisualKey(const Snapshot& s,bool offline,double time) {
    uint64_t key=s.staticKey;auto add=[&](const auto& n){const auto* p=reinterpret_cast<const unsigned char*>(&n);for(size_t i=0;i<sizeof(n);++i){key^=p[i];key*=1099511628211ULL;}};
    auto str=[&](const std::string& v){add(Hash(v));};
    auto frame=[&](const Frame& f){str(f.sheet);add(f.x);add(f.y);add(f.w);add(f.h);add(f.ax);add(f.ay);add(f.scale);add(f.ox);add(f.oy);};
    add(offline);add(s.schema);for(const auto& f:s.furniture){str(f.owner);str(f.monitor);str(f.state);str(f.deskId);add(f.hasFrame);if(f.hasFrame)frame(f.frame);}
    for(const auto& a:s.agents){str(a.id);str(a.name);str(a.displayName);str(a.workLocation);str(a.providerIcon);str(a.deskId);str(a.overflowSeatId);str(a.state);add(a.queued);str(a.provider);str(a.parent);add(a.x);add(a.y);add(a.dx);add(a.dy);add(a.opacity);add(a.visible);add(a.sub);add(a.paper);add(a.carrying);str(a.action);str(a.toolKind);str(a.facing);str(a.reaction);str(a.partner);frame(DisplayFrame(s,a,offline,time));}
    if(offline&&std::any_of(s.agents.begin(),s.agents.end(),[](const Agent& a){return a.visible;})){int tick=static_cast<int>(std::fmod(time,2));add(tick);}
    else if(!s.sheets.contains("icons")&&std::any_of(s.agents.begin(),s.agents.end(),[](const Agent& a){return a.visible&&a.action=="coffee";})){int tick=static_cast<int>(std::fmod(time*2,4));add(tick);}
    return key;
}
} // namespace vdc

#if !defined(VIBEDOTCOM_TEST) || defined(VIBEDOTCOM_HOST_TEST)
// ===== BEGIN WINDHAWK WINDOW HOST =====
// Shared-renderer prerequisites supplied by the integrating source:
//   vdc::readStop: thread_local HANDLE, observed by cancellable ReadFileBytes.
//   Renderer::HitBoxes(s,w,h,zoom,offline,time): vector<pair<Bounds,size_t>>,
//     in front-first order, with camera applied (client pixel coordinates).
// No GDI+ object, snapshot, file IO, decoding or renderer call crosses into UI.
// -fms-extensions enables the local SEH callback boundaries on Windhawk clang.
#include <atomic>
#include <cstddef>
#include <cstring>
#include <mutex>
#include <utility>
#include <dwmapi.h>
#include <shobjidl.h>

namespace vdc { extern thread_local HANDLE readStop; }
namespace {
using namespace vdc;
constexpr wchar_t kViewClass[]=L"Vibedotcom.Windhawk.Office";
constexpr wchar_t kHelperClass[]=L"Vibedotcom.Windhawk.Helper";
constexpr wchar_t kTipClass[]=L"Vibedotcom.Windhawk.Tooltip";
constexpr UINT kShutdown=WM_APP+0x431, kPacketReady=WM_APP+0x432;
constexpr UINT_PTR kStopTimer=0x564443;
constexpr size_t kNoHit=std::numeric_limits<size_t>::max();
constexpr int kMaxWidth=4096, kMaxHeight=2048;
HINSTANCE g_module=nullptr;
HANDLE g_stop=nullptr, g_workWake=nullptr, g_thread=nullptr, g_worker=nullptr;
std::atomic<DWORD> g_uiThreadId{0};
std::atomic<HWND> g_office{nullptr}, g_notify{nullptr}, g_shellRequest{nullptr};
std::atomic<bool> g_shellBusy{false};
std::atomic<int> g_shellOutcome{0}; // 0 pending, 1 completed, -1 failed.

struct Settings {
    std::wstring mode,state,pack;
    int x=80,y=180,w=720,h=520,opacity=100,fps=30;
    double zoom=0;
    bool through=false,tooltip=true,transparent=false;
};
Settings g_settings; // Immutable from thread creation through both joins.

// Constant messages only: these paths neither convert strings nor allocate.
void FixedLog(const wchar_t* message) noexcept {
#ifdef VIBEDOTCOM_HOST_TEST
    (void)message;
#else
    OutputDebugStringW(message);
#endif
}
bool Stopping() noexcept {
    return !g_stop || WaitForSingleObject(g_stop,0)!=WAIT_TIMEOUT;
}
void SignalStop() noexcept {
    if(g_stop)SetEvent(g_stop);
    if(g_workWake)SetEvent(g_workWake);
    if(auto id=g_uiThreadId.load())PostThreadMessageW(id,kShutdown,0,0);
    if(auto w=g_office.load())PostMessageW(w,kShutdown,0,0);
    if(auto w=g_notify.load())PostMessageW(w,kShutdown,0,0);
}

struct DpiScope {
    DPI_AWARENESS_CONTEXT previous=nullptr;
    explicit DpiScope(DPI_AWARENESS_CONTEXT context) noexcept {
        if(context)previous=SetThreadDpiAwarenessContext(context);
    }
    explicit DpiScope(HWND w) noexcept : DpiScope(
        w?GetWindowDpiAwarenessContext(w):DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) {}
    ~DpiScope() noexcept { if(previous)SetThreadDpiAwarenessContext(previous); }
    DpiScope(const DpiScope&)=delete;
    DpiScope& operator=(const DpiScope&)=delete;
};
struct OwnedWindow {
    HWND value=nullptr;
    HWND get() const noexcept { return value; }
    void forget(HWND w) noexcept { if(value==w)value=nullptr; }
    void reset() noexcept {
        auto w=std::exchange(value,nullptr);
        if(w&&IsWindow(w))DestroyWindow(w);
    }
    ~OwnedWindow() noexcept { reset(); }
    OwnedWindow()=default;
    OwnedWindow(const OwnedWindow&)=delete;
    OwnedWindow& operator=(const OwnedWindow&)=delete;
};
struct OwnedFont {
    HFONT value=nullptr;
    void reset(HFONT font=nullptr) noexcept {
        auto old=std::exchange(value,font);if(old)DeleteObject(old);
    }
    ~OwnedFont() noexcept { reset(); }
};
struct DcSelection {
    HDC dc;HGDIOBJ previous;
    DcSelection(HDC d,HGDIOBJ object) noexcept : dc(d),previous(SelectObject(d,object)) {}
    bool valid() const noexcept { return previous&&previous!=HGDI_ERROR; }
    ~DcSelection() noexcept { if(valid())SelectObject(dc,previous); }
};
struct WindowDc {
    HWND window;HDC dc;
    explicit WindowDc(HWND w) noexcept : window(w),dc(GetDC(w)) {}
    ~WindowDc() noexcept { if(dc)ReleaseDC(window,dc); }
};
struct PaintDc {
    HWND window;PAINTSTRUCT paint{};HDC dc;
    explicit PaintDc(HWND w) noexcept : window(w),dc(BeginPaint(w,&paint)) {}
    ~PaintDc() noexcept { EndPaint(window,&paint); }
};
struct DibSurface {
    HDC dc=nullptr;HBITMAP bitmap=nullptr;HGDIOBJ previous=nullptr;
    void* bits=nullptr;int width=0,height=0;
    void reset() noexcept {
        if(dc&&previous&&previous!=HGDI_ERROR)SelectObject(dc,previous);
        if(bitmap)DeleteObject(bitmap);
        if(dc)DeleteDC(dc);
        dc=nullptr;bitmap=nullptr;previous=nullptr;bits=nullptr;width=height=0;
    }
    ~DibSurface() noexcept { reset(); }
    bool resize(int w,int h) noexcept {
        if(bits&&width==w&&height==h)return true;
        const SIZE extent=ClampClientExtent(w,h);
        if(extent.cx!=w||extent.cy!=h)return false;
        DibSurface next;
        next.dc=CreateCompatibleDC(nullptr);if(!next.dc)return false;
        BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth=w;info.bmiHeader.biHeight=-h;
        info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
        info.bmiHeader.biCompression=BI_RGB;
        next.bitmap=CreateDIBSection(next.dc,&info,DIB_RGB_COLORS,&next.bits,nullptr,0);
        if(!next.bitmap||!next.bits)return false;
        next.previous=SelectObject(next.dc,next.bitmap);
        if(!next.previous||next.previous==HGDI_ERROR)return false;
        next.width=w;next.height=h;
        std::swap(dc,next.dc);std::swap(bitmap,next.bitmap);
        std::swap(previous,next.previous);std::swap(bits,next.bits);
        std::swap(width,next.width);std::swap(height,next.height);
        return true;
    }
    DibSurface()=default;
    DibSurface(const DibSurface&)=delete;
    DibSurface& operator=(const DibSurface&)=delete;
};

struct Pixels {
    int width=0,height=0;uint64_t serial=0;
    std::vector<BYTE> bgra; // Top-down, tightly packed, premultiplied BGRA.
    std::vector<BYTE> framed; // Worker-composited opaque background, if framed.
};
struct CachedHit { Bounds bounds;Agent agent;std::wstring tooltip; };
struct Packet {
    uint64_t requestSerial=0;
    double cameraZoom=1;
    bool hasState=false,offline=true;
    std::shared_ptr<const Pixels> pixels;
    std::vector<CachedHit> hits;
};
struct ViewRequest {
    uint64_t serial=0;
    int width=720,height=520;
    double zoom=0;
    bool enabled=false;
    std::shared_ptr<const Packet> hoverPacket;
    size_t hoverIndex=kNoHit;
};
struct Mailbox {
    std::mutex mutex;
    ViewRequest request;
    std::shared_ptr<const Packet> packet;
};
Mailbox g_scene;
ViewRequest LatestRequest() {
    std::lock_guard<std::mutex> lock(g_scene.mutex);return g_scene.request;
}
std::shared_ptr<const Packet> LatestPacket() {
    std::lock_guard<std::mutex> lock(g_scene.mutex);return g_scene.packet;
}
void ClearMailbox() {
    ViewRequest request;std::shared_ptr<const Packet> packet;
    { std::lock_guard<std::mutex> lock(g_scene.mutex);
      std::swap(request,g_scene.request);packet.swap(g_scene.packet); }
    // The last references and all vector/string storage are freed outside lock.
}
const std::string& RequestedHover(const ViewRequest& request) noexcept {
    static const std::string empty;
    if(request.hoverPacket&&request.hoverIndex<request.hoverPacket->hits.size())
        return request.hoverPacket->hits[request.hoverIndex].agent.id;
    return empty;
}

struct Host {
    HWND parent=nullptr,icons=nullptr,wallpaper=nullptr,iconHost=nullptr;
    bool operator==(const Host&) const=default;
};
bool InThisProcess(HWND w) noexcept {
    DWORD pid=0;return w&&GetWindowThreadProcessId(w,&pid)&&pid==GetCurrentProcessId();
}
BOOL CALLBACK ClassicHost(HWND top,LPARAM value) noexcept {
    if(!InThisProcess(top)||!FindWindowExW(top,nullptr,L"SHELLDLL_DefView",nullptr))return TRUE;
    unsigned count=0;
    for(HWND next=GetWindow(top,GW_HWNDNEXT);next&&count++<1024;next=GetWindow(next,GW_HWNDNEXT)) {
        wchar_t name[64]{};GetClassNameW(next,name,64);
        if(wcscmp(name,L"WorkerW")==0&&InThisProcess(next)&&IsWindowVisible(next)&&
           !FindWindowExW(next,nullptr,L"SHELLDLL_DefView",nullptr)) {
            *reinterpret_cast<Host*>(value)={next,nullptr,next,top};return FALSE;
        }
    }
    return TRUE;
}
Host FindHost() noexcept {
    HWND progman=FindWindowW(L"Progman",nullptr);if(!InThisProcess(progman))return {};
    HWND icons=FindWindowExW(progman,nullptr,L"SHELLDLL_DefView",nullptr);
    HWND worker=FindWindowExW(progman,nullptr,L"WorkerW",nullptr);
    if(InThisProcess(icons)&&InThisProcess(worker)&&IsWindowVisible(worker)&&
       (GetWindowLongPtrW(progman,GWL_EXSTYLE)&WS_EX_NOREDIRECTIONBITMAP))
        return {progman,icons,worker,progman};
    Host host;EnumWindows(ClassicHost,reinterpret_cast<LPARAM>(&host));return host;
}

// This thread has no windows, parents, COM UI objects or input attachments.
// Its only shell operation consumes an HWND value and sends scalar parameters.
void ServiceShellRequest() noexcept {
    HWND progman=g_shellRequest.exchange(nullptr);
    if(!progman)return;
    bool ok=false;
    if(!Stopping()&&InThisProcess(progman)) {
        DWORD_PTR result=0;
        constexpr UINT flags=SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT|SMTO_BLOCK;
        bool first=SendMessageTimeoutW(progman,0x052C,0xD,0,flags,250,&result)!=0;
        bool second=false;
        if(!Stopping()&&InThisProcess(progman))
            second=SendMessageTimeoutW(progman,0x052C,0xD,1,flags,250,&result)!=0;
        ok=first&&second;
    }
    g_shellOutcome.store(ok?1:-1);g_shellBusy.store(false);
    if(!ok&&!Stopping())FixedLog(L"Vibedotcom: shell layer request failed; retry is bounded by backoff.\n");
    if(auto w=g_notify.load())PostMessageW(w,kPacketReady,0,0);
}
struct GdiPlusLifetime {
    ULONG_PTR token=0;
    void start() {
        Gdiplus::GdiplusStartupInput input;
        Require(Native([&]{return Gdiplus::GdiplusStartup(&token,&input,nullptr);})==Gdiplus::Ok,"GDI+ startup failed");
    }
    ~GdiPlusLifetime() noexcept { if(token)NativeCleanup([&]{Gdiplus::GdiplusShutdown(token);return 0;}); }
};
struct ReadStopScope {
    ReadStopScope() noexcept { vdc::readStop=g_stop; }
    ~ReadStopScope() noexcept { vdc::readStop=nullptr; }
};
std::shared_ptr<Pixels> CopyPixels(Gdiplus::Bitmap& image,int w,int h,uint64_t serial,int schema) {
    const SIZE extent=ClampClientExtent(w,h);
    Require(extent.cx==w&&extent.cy==h,"Host viewport bounds");
    auto pixels=std::make_shared<Pixels>();pixels->width=w;pixels->height=h;pixels->serial=serial;
    const size_t row=static_cast<size_t>(w)*4;
    pixels->bgra.resize(row*static_cast<size_t>(h));
    { PixelLock lock(image,w,h);
      Require(lock.data.Scan0&&std::abs(static_cast<int64_t>(lock.data.Stride))>=static_cast<int64_t>(row),"Render stride bounds");
      for(int y=0;y<h;++y) {
          if(Stopping())throw Error("Worker stopping");
          const auto* source=static_cast<const BYTE*>(lock.data.Scan0)+static_cast<ptrdiff_t>(y)*lock.data.Stride;
          std::memcpy(pixels->bgra.data()+static_cast<size_t>(y)*row,source,row);
      }
      lock.Close(); }
    if(g_settings.mode!=L"desktop-level"&&!g_settings.transparent) {
        pixels->framed=pixels->bgra;
        const BYTE background[3]={43,static_cast<BYTE>(schema>=4?40:45),static_cast<BYTE>(schema>=4?16:48)};
        for(int y=0;y<h;++y) {
            if(Stopping())throw Error("Worker stopping");
            for(size_t offset=static_cast<size_t>(y)*row;offset<static_cast<size_t>(y+1)*row;offset+=4) {
                const unsigned inverse=255-pixels->framed[offset+3];
                for(int c=0;c<3;++c)pixels->framed[offset+c]=static_cast<BYTE>(std::min(255u,
                    pixels->framed[offset+c]+(background[c]*inverse+127)/255));
                pixels->framed[offset+3]=255;
            }
        }
    }
    return pixels;
}
std::shared_ptr<Pixels> WaitingPixels(const ViewRequest& request,uint64_t serial) {
    // Guard each native operation so temporary owners unwind on a native fault.
    SafeGdi<Bitmap> imageOwner(request.width,request.height,PixelFormat32bppPARGB);auto& image=imageOwner.get();
    GdiCheck(image.GetLastStatus(),"Waiting buffer allocation");
    { SafeGdi<Graphics> graphicsOwner(&image);auto& graphics=graphicsOwner.get();
      GdiCheck(graphics.GetLastStatus(),"Waiting graphics allocation");
      GdiCheck(graphics.Clear(g_settings.transparent?Gdiplus::Color(0,0,0,0):Gdiplus::Color(255,48,45,43)));
      SafeGdi<FontFamily> familyOwner(L"Segoe UI");
      SafeGdi<Font> fontOwner(&familyOwner.get(),13.0f,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);auto& font=fontOwner.get();
      GdiCheck(font.GetLastStatus(),"Waiting font allocation");
      SafeGdi<SolidBrush> backgroundOwner(Gdiplus::Color(255,48,61,64));auto& background=backgroundOwner.get();
      GdiCheck(background.GetLastStatus(),"Waiting brush allocation");
      SafeGdi<SolidBrush> textOwner(Gdiplus::Color(255,255,244,223));auto& text=textOwner.get();
      GdiCheck(text.GetLastStatus(),"Waiting brush allocation");
      GdiCheck(graphics.FillRectangle(&background,8,8,std::max(1,std::min(request.width-8,390)),32));
      GdiCheck(graphics.DrawString(L"tracker offline — waiting for a valid snapshot",-1,&font,Gdiplus::PointF(16,14),&text)); }
    return CopyPixels(image,request.width,request.height,serial,3);
}
struct WorkerState {
    Renderer renderer;Snapshot snapshot;
    bool hasState=false,accepted=false,rendered=false;
    FILETIME acceptedTime{};DWORD acceptedSize=0;
    unsigned readFailures=0,frameFailures=0;
    ULONGLONG nextRead=0;
    uint64_t stateSerial=0,pixelSerial=0,visualKey=0;
    double pixelZoom=0;
    std::shared_ptr<const Pixels> pixels;
    std::wstring packRoot;
    void read(ULONGLONG tick) {
        WIN32_FILE_ATTRIBUTE_DATA before{};
        const bool stamped=GetFileAttributesExW(g_settings.state.c_str(),GetFileExInfoStandard,&before)!=FALSE;
        if(stamped&&accepted&&before.nFileSizeHigh==0&&before.nFileSizeLow==acceptedSize&&
           CompareFileTime(&before.ftLastWriteTime,&acceptedTime)==0) { nextRead=tick+200;return; }
        try {
            if(Stopping())return;
            auto bytes=ReadFileBytes(g_settings.state,4*1024*1024);
            auto candidate=ParseSnapshot(bytes,hasState?&snapshot:nullptr);
            if(Stopping())return;
            renderer.Load(candidate,packRoot);
            if(Stopping())return;
            snapshot=std::move(candidate);hasState=true;++stateSerial;
            accepted=false;readFailures=0;nextRead=tick+200;
            WIN32_FILE_ATTRIBUTE_DATA after{};
            if(stamped&&GetFileAttributesExW(g_settings.state.c_str(),GetFileExInfoStandard,&after)&&
               before.nFileSizeHigh==0&&after.nFileSizeHigh==0&&after.nFileSizeLow==bytes.size()&&
               before.nFileSizeLow==after.nFileSizeLow&&CompareFileTime(&before.ftLastWriteTime,&after.ftLastWriteTime)==0) {
                acceptedTime=after.ftLastWriteTime;acceptedSize=after.nFileSizeLow;accepted=true;
            }
        } catch(...) {
            if(Stopping())return;
            if(readFailures<100000)++readFailures;
            nextRead=tick+(readFailures==1?40:readFailures==2?100:200);
            if(readFailures==1||readFailures==50)
                FixedLog(L"Vibedotcom: state/pack rejected; keeping the last valid scene.\n");
        }
    }
    std::shared_ptr<const Packet> build(const ViewRequest& request,double now,uint64_t key) {
        const bool offline=!hasState||Offline(snapshot,now);
        const bool newPixels=!rendered||!pixels||pixels->width!=request.width||pixels->height!=request.height||
            pixelZoom!=request.zoom||visualKey!=key;
        std::shared_ptr<const Pixels> candidatePixels=pixels;
        if(newPixels) {
            if(hasState) {
                auto image=renderer.Render(snapshot,request.width,request.height,static_cast<int>(request.zoom),
                    g_settings.transparent,offline,now,RequestedHover(request));
                candidatePixels=CopyPixels(*image,request.width,request.height,pixelSerial+1,snapshot.schema);
            } else candidatePixels=WaitingPixels(request,pixelSerial+1);
        }
        auto packet=std::make_shared<Packet>();packet->requestSerial=request.serial;
        packet->hasState=hasState;packet->offline=offline;packet->pixels=candidatePixels;
        if(hasState) {
            packet->cameraZoom=ViewCamera(snapshot,request.width,request.height,static_cast<int>(request.zoom)).zoom;
            auto boxes=renderer.HitBoxes(snapshot,request.width,request.height,static_cast<int>(request.zoom),offline,now);
            packet->hits.reserve(boxes.size());
            for(const auto& [bounds,index]:boxes) {
                if(Stopping())throw Error("Worker stopping");
                Require(index<snapshot.agents.size(),"Hit agent index bounds");
                const auto& agent=snapshot.agents[index];
                packet->hits.push_back({bounds,agent,Wide(Tooltip(agent,now))});
            }
        }
        // Renderer/hit/tooltip failure cannot replace the last good packet.
        pixels=std::move(candidatePixels);visualKey=key;pixelZoom=request.zoom;rendered=true;
        if(newPixels)++pixelSerial;
        return packet;
    }
};
void SceneLoopBody(WorkerState* state) noexcept {
    try {
        WorkerState& worker=*state;
        ULONGLONG nextFrame=0;
        uint64_t lastRequest=0,lastState=0,lastKey=0;
        int64_t lastSecond=-1;
        const ULONGLONG interval=static_cast<ULONGLONG>((1000+g_settings.fps-1)/g_settings.fps);
        while(!Stopping()) {
            ServiceShellRequest();
            const auto tick=GetTickCount64();
            if(tick>=worker.nextRead)worker.read(tick);
            if(Stopping())break;
            auto request=LatestRequest();
            const double now=Now();
            if(request.enabled&&tick>=nextFrame) {
                nextFrame=tick+interval;
                try {
                    const bool offline=!worker.hasState||Offline(worker.snapshot,now);
                    uint64_t key=worker.hasState?VisualKey(worker.snapshot,offline,now):0;
                    if(worker.hasState&&worker.snapshot.schema>=4)key^=Hash(RequestedHover(request));
                    const auto second=static_cast<int64_t>(std::floor(now));
                    const bool update=lastRequest!=request.serial||lastState!=worker.stateSerial||lastKey!=key||
                        (g_settings.tooltip&&request.hoverPacket&&lastSecond!=second);
                    if(update) {
                        auto packet=worker.build(request,now,key);
                        std::shared_ptr<const Packet> retired;
                        bool published=false;
                        { std::lock_guard<std::mutex> lock(g_scene.mutex);
                          if(!Stopping()&&g_scene.request.enabled&&g_scene.request.serial==request.serial) {
                              retired.swap(g_scene.packet);g_scene.packet=std::move(packet);published=true;
                          } }
                        if(published) {
                            lastRequest=request.serial;lastState=worker.stateSerial;lastKey=key;lastSecond=second;
                            if(auto w=g_notify.load())PostMessageW(w,kPacketReady,0,0);
                        }
                        worker.frameFailures=0;
                    }
                } catch(...) {
                    if(!Stopping()) {
                        if(worker.frameFailures<100000)++worker.frameFailures;
                        nextFrame=tick+200;
                        if(worker.frameFailures==1||worker.frameFailures==50)
                            FixedLog(L"Vibedotcom: frame failed; keeping the last complete frame.\n");
                    }
                }
            }
            ServiceShellRequest();
            const auto after=GetTickCount64();
            ULONGLONG due=worker.nextRead;
            if(request.enabled)due=std::min(due,nextFrame);
            const DWORD delay=static_cast<DWORD>(due>after?std::min<ULONGLONG>(due-after,200):1);
            HANDLE waits[]={g_stop,g_workWake};
            const DWORD result=WaitForMultipleObjects(2,waits,FALSE,delay);
            if(result==WAIT_OBJECT_0)break;
            if(result==WAIT_FAILED) { FixedLog(L"Vibedotcom: scene worker wait failed.\n");SignalStop();break; }
        }
    } catch(...) {
        FixedLog(L"Vibedotcom: scene loop failed.\n");SignalStop();
    }
}
bool SceneScopeBoundary(WorkerState* state) noexcept {
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
    __try { SceneLoopBody(state);return true; }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        FixedLog(L"Vibedotcom: scene native fault; leaving the worker scope.\n");SignalStop();return false;
    }
#else
    SceneLoopBody(state);return true;
#endif
}
DWORD SceneBody(void*) noexcept {
    try {
        ReadStopScope readStop;
        GdiPlusLifetime graphics;graphics.start();
        WorkerState worker; // Owned outside the SEH loop: destroyed before shutdown.
        worker.packRoot=g_settings.pack.empty()?Parent(g_settings.state):g_settings.pack;
        if(!SceneScopeBoundary(&worker))return 1;
    } catch(...) {
        FixedLog(L"Vibedotcom: scene worker stopped after an initialization failure.\n");SignalStop();return 1;
    }
    return 0;
}
DWORD WINAPI SceneThread(void* value) noexcept {
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
    __try { return SceneBody(value); }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        FixedLog(L"Vibedotcom: scene thread native fault.\n");SignalStop();return 1;
    }
#else
    return SceneBody(value);
#endif
}

struct VisibilityServices {
    using DwmAttribute=HRESULT (WINAPI*)(HWND,DWORD,PVOID,DWORD);
    using MonitorDpi=HRESULT (WINAPI*)(HMONITOR,int,UINT*,UINT*);
    HMODULE dwm=nullptr,shcore=nullptr;
    DwmAttribute attribute=nullptr;MonitorDpi monitorDpi=nullptr;
    IVirtualDesktopManager* desktops=nullptr;
    bool comInitialized=false;
    void init() noexcept {
        dwm=LoadLibraryExW(L"dwmapi.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
        shcore=LoadLibraryExW(L"shcore.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
        if(dwm)attribute=reinterpret_cast<DwmAttribute>(GetProcAddress(dwm,"DwmGetWindowAttribute"));
        if(shcore)monitorDpi=reinterpret_cast<MonitorDpi>(GetProcAddress(shcore,"GetDpiForMonitor"));
        HRESULT hr=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED|COINIT_DISABLE_OLE1DDE);
        comInitialized=SUCCEEDED(hr);
        if(comInitialized) {
            // Local GUID constants avoid adding a uuid library requirement.
            constexpr GUID clsid={0xaa509086,0x5ca9,0x4c25,{0x8f,0x95,0x58,0x9d,0x3c,0x07,0xb4,0x8a}};
            constexpr GUID iid={0xa5cd92ff,0x29be,0x454c,{0x8d,0x04,0xd8,0x28,0x79,0xfb,0x3f,0x1b}};
            CoCreateInstance(clsid,nullptr,CLSCTX_INPROC_SERVER,iid,reinterpret_cast<void**>(&desktops));
        }
    }
    bool cloaked(HWND w) const noexcept {
        DWORD cloak=0;
        return attribute&&SUCCEEDED(attribute(w,DWMWA_CLOAKED,&cloak,sizeof(cloak)))&&cloak!=0;
    }
    bool knownUncloaked(HWND w) const noexcept {
        DWORD cloak=0;
        return attribute&&SUCCEEDED(attribute(w,DWMWA_CLOAKED,&cloak,sizeof(cloak)))&&cloak==0;
    }
    UINT cursorDpi(POINT physical,HWND fallback) const noexcept {
        DpiScope scope(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        UINT x=0,y=0;
        if(monitorDpi&&SUCCEEDED(monitorDpi(MonitorFromPoint(physical,MONITOR_DEFAULTTONEAREST),0,&x,&y))&&x)
            return x;
        UINT dpi=fallback?GetDpiForWindow(fallback):0;return dpi?dpi:96;
    }
    ~VisibilityServices() noexcept {
        if(desktops)desktops->Release();
        if(comInitialized)CoUninitialize();
        if(shcore)FreeLibrary(shcore);
        if(dwm)FreeLibrary(dwm);
    }
};
struct Ui;
void PublishRequest(Ui& ui);
void HideTip(Ui& ui,bool clearHover=true) noexcept;
void StopUi(Ui& ui) noexcept;
void PresentLatest(Ui& ui);
void SyncViewport(Ui& ui);
void ProbeHost(Ui& ui,ULONGLONG tick);
void PlaceTip(Ui& ui,POINT physical);
LRESULT CALLBACK ViewProc(HWND,UINT,WPARAM,LPARAM) noexcept;
LRESULT CALLBACK HelperProc(HWND,UINT,WPARAM,LPARAM) noexcept;
LRESULT CALLBACK TipProc(HWND,UINT,WPARAM,LPARAM) noexcept;

struct Ui {
    VisibilityServices visibility;
    OwnedWindow window,helper,tip;
    OwnedFont tipFont;
    DibSurface surface;
    Host host,failedLayerHost;
    RECT physicalRect{g_settings.x,g_settings.y,g_settings.x+g_settings.w,g_settings.y+g_settings.h};
    std::shared_ptr<const Packet> presented,hoverPacket;
    size_t hoverIndex=kNoHit,tipIndex=kNoHit;
    uint64_t requestSerial=0,copiedPixelSerial=0;
    int width=720,height=520;
    double zoom=0;
    bool running=true,stopping=false,active=false,perPixel=true;
    bool tipShown=false,inSizeMove=false,inMenu=false,geometryRefresh=false,displayChanged=false;
    bool retryPresentation=false,desktopCurrent=true;
    unsigned missingRetries=0;
    UINT taskbarCreated=0,tipDpi=96;
    ULONGLONG nextProbe=0,nextShellRequest=0,nextHover=0,nextVisibility=0,nextDesktopCheck=0;
    ULONGLONG hoverSince=0,lastTip=0;
    std::shared_ptr<const Packet> tipPacket;
    ~Ui() noexcept { StopUi(*this);helper.reset(); }
};

void PublishRequest(Ui& ui) {
    if(ui.stopping)return;
    ViewRequest request;
    request.serial=++ui.requestSerial;request.width=ui.width;request.height=ui.height;
    request.zoom=ui.zoom;request.enabled=ui.active&&ui.window.get()!=nullptr;
    request.hoverPacket=ui.hoverPacket;request.hoverIndex=ui.hoverIndex;
    { std::lock_guard<std::mutex> lock(g_scene.mutex);std::swap(request,g_scene.request); }
    if(g_workWake)SetEvent(g_workWake);
}
void HideTip(Ui& ui,bool clearHover) noexcept {
    if(ui.tipShown&&ui.tip.get())ShowWindow(ui.tip.get(),SW_HIDE);
    ui.tipShown=false;ui.tipPacket.reset();ui.tipIndex=kNoHit;
    if(clearHover) { ui.hoverPacket.reset();ui.hoverIndex=kNoHit;ui.hoverSince=0; }
}
void StopUi(Ui& ui) noexcept {
    if(ui.stopping)return;
    ui.stopping=true;ui.running=false;ui.active=false;
    SignalStop();HideTip(ui);
    if(auto w=ui.window.get()) {
        KillTimer(w,kStopTimer);
        SendMessageW(w,WM_CANCELMODE,0,0); // Our HWND, never a shell window.
        if(GetCapture()==w)ReleaseCapture();
        ui.window.reset();
    }
    ui.tip.reset();ui.presented.reset();
}
bool OfficeShown(Ui& ui,ULONGLONG tick) noexcept {
    HWND w=ui.window.get();
    if(!w)return HoverEligible(false,false,false,false);
    bool visible=IsWindowVisible(w)!=FALSE,cloaked=false;
    unsigned count=0;
    for(HWND ancestor=w;ancestor&&ancestor!=GetDesktopWindow()&&count++<64;
        ancestor=GetAncestor(ancestor,GA_PARENT))
    {
        visible=visible&&IsWindowVisible(ancestor)!=FALSE;
        cloaked=cloaked||!ui.visibility.knownUncloaked(ancestor);
    }
    if(count>=64)visible=false;
    if(!ui.host.parent&&ui.visibility.desktops) {
        if(tick>=ui.nextDesktopCheck) {
            BOOL current=TRUE;
            const HRESULT hr=ui.visibility.desktops->IsWindowOnCurrentVirtualDesktop(w,&current);
            ui.desktopCurrent=SUCCEEDED(hr)&&current!=FALSE;ui.nextDesktopCheck=tick+200;
        }
    }
    return HoverEligible(visible,IsIconic(w)!=FALSE,cloaked,ui.host.parent||ui.desktopCurrent);
}
void UpdateVisibility(Ui& ui,ULONGLONG tick) {
    bool active=OfficeShown(ui,tick);
    if(!active) {
        const bool hovered=ui.hoverPacket!=nullptr;
        HideTip(ui);
        if(ui.active||hovered) { ui.active=false;PublishRequest(ui); }
    } else if(!ui.active) { ui.active=true;PublishRequest(ui);ui.retryPresentation=true; }
}
struct OuterLimits { int minW=240,minH=160,maxW=kMaxWidth,maxH=kMaxHeight; };
OuterLimits Limits(DWORD style,DWORD ex,UINT dpi) noexcept {
    const SIZE maxClient=ClampClientExtent(kMaxWidth,kMaxHeight);
    RECT minimum{0,0,240,160},maximum{0,0,maxClient.cx,maxClient.cy};
    if(!dpi)dpi=96;
    if(!AdjustWindowRectExForDpi(&minimum,style,FALSE,ex,dpi))
        AdjustWindowRectEx(&minimum,style,FALSE,ex);
    if(!AdjustWindowRectExForDpi(&maximum,style,FALSE,ex,dpi))
        AdjustWindowRectEx(&maximum,style,FALSE,ex);
    return {minimum.right-minimum.left,minimum.bottom-minimum.top,
            maximum.right-maximum.left,maximum.bottom-maximum.top};
}
OuterLimits Limits(HWND w) noexcept {
    return Limits(static_cast<DWORD>(GetWindowLongPtrW(w,GWL_STYLE)),
                  static_cast<DWORD>(GetWindowLongPtrW(w,GWL_EXSTYLE)),GetDpiForWindow(w));
}
void RememberPhysicalRect(Ui& ui) noexcept {
    if(!ui.window.get()||IsIconic(ui.window.get()))return;
    DpiScope physical(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    RECT rect{};
    if(physical.previous&&GetWindowRect(ui.window.get(),&rect)&&rect.right>rect.left&&rect.bottom>rect.top)
        ui.physicalRect=rect;
}
bool RecoverFullyOffscreen(RECT& rect) noexcept {
    DpiScope physical(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if(!physical.previous||MonitorFromRect(&rect,MONITOR_DEFAULTTONULL))return false;
    MONITORINFO info{};info.cbSize=sizeof(info);
    if(!GetMonitorInfoW(MonitorFromRect(&rect,MONITOR_DEFAULTTONEAREST),&info))return false;
    rect=RecoverOffscreenRect(rect,info.rcWork,false);return true;
}
bool TargetPlacement(HWND parent,const RECT& physical,POINT& position,SIZE& size) noexcept {
    POINT start{physical.left,physical.top},end{physical.right,physical.bottom};
    if(parent) {
        if(!PhysicalToLogicalPointForPerMonitorDPI(parent,&start)||
           !PhysicalToLogicalPointForPerMonitorDPI(parent,&end))return false;
        size={end.x-start.x,end.y-start.y};
        if(!ScreenToClient(parent,&start))return false;
    } else size={end.x-start.x,end.y-start.y};
    position=start;return size.cx>0&&size.cy>0;
}
void SyncViewport(Ui& ui) {
    if(!ui.window.get())return;
    DpiScope scope(ui.window.get());RECT rect{};
    if(!scope.previous||!GetClientRect(ui.window.get(),&rect))return;
    const int width=rect.right-rect.left,height=rect.bottom-rect.top;
    if(width<=0||height<=0||IsIconic(ui.window.get())) {
        if(ui.active) { ui.active=false;HideTip(ui);PublishRequest(ui); }return;
    }
    const SIZE extent=ClampClientExtent(width,height);
    if(width!=extent.cx||height!=extent.cy) {
        // Enforce the real HWND dimensions as well as WM_WINDOWPOSCHANGING.
        RECT outer{};GetWindowRect(ui.window.get(),&outer);auto limits=Limits(ui.window.get());
        SetWindowPos(ui.window.get(),nullptr,0,0,
            std::min<int>(outer.right-outer.left,limits.maxW),
            std::min<int>(outer.bottom-outer.top,limits.maxH),SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
        return;
    }
    if(ui.width!=width||ui.height!=height) {
        ui.width=extent.cx;ui.height=extent.cy;HideTip(ui);PublishRequest(ui);
    }
}
bool CreateOffice(Ui& ui,const Host& host) {
    if(Stopping())return false;
    DpiScope context(host.parent);
    if(!context.previous) { FixedLog(L"Vibedotcom: target DPI context unavailable.\n");return false; }
    const bool perPixel=host.parent||g_settings.mode==L"desktop-level"||g_settings.transparent;
    DWORD style=host.parent?WS_CHILD:perPixel?WS_POPUP:WS_OVERLAPPEDWINDOW;
    DWORD ex=WS_EX_LAYERED|WS_EX_TOOLWINDOW;
    if(g_settings.through)ex|=WS_EX_TRANSPARENT;
    if(g_settings.through||g_settings.mode==L"desktop-level")ex|=WS_EX_NOACTIVATE;
    if(host.parent)ex|=WS_EX_NOPARENTNOTIFY;
    POINT position{};SIZE size{};
    if(!TargetPlacement(host.parent,ui.physicalRect,position,size))return false;
    const UINT dpi=host.parent?GetDpiForWindow(host.parent):GetDpiForSystem();
    auto limits=Limits(style,ex,dpi);
    size.cx=std::clamp<int>(size.cx,limits.minW,limits.maxW);
    size.cy=std::clamp<int>(size.cy,limits.minH,limits.maxH);
    ui.host=host;ui.perPixel=perPixel;ui.active=false;ui.desktopCurrent=true;ui.nextDesktopCheck=0;
    HWND w=CreateWindowExW(ex,kViewClass,L"Vibedotcom",style,position.x,position.y,size.cx,size.cy,
        host.parent,nullptr,g_module,&ui);
    if(!w) { ui.window.reset();ui.host={};return false; }
    // Framed windows use WM_PAINT/BitBlt plus constant opacity. Their worker
    // buffer is premultiplied-composited over an opaque background. They NEVER
    // use UpdateLayeredWindow; no LWA call is made on any per-pixel HWND.
    if(!perPixel&&!SetLayeredWindowAttributes(w,0,static_cast<BYTE>(g_settings.opacity*255/100),LWA_ALPHA)) {
        ui.window.reset();ui.host={};FixedLog(L"Vibedotcom: framed opacity setup failed.\n");return false;
    }
    ui.copiedPixelSerial=0;ui.presented.reset();ui.retryPresentation=true;
    HWND order=host.parent?(host.icons?host.icons:HWND_TOP):
        g_settings.mode==L"topmost"?HWND_TOPMOST:g_settings.mode==L"desktop-level"?HWND_BOTTOM:HWND_NOTOPMOST;
    if(!SetWindowPos(w,order,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW))
        FixedLog(L"Vibedotcom: office placement failed.\n");
    SyncViewport(ui);RememberPhysicalRect(ui);UpdateVisibility(ui,GetTickCount64());PublishRequest(ui);
    return true;
}
void ApplyPhysicalPlacement(Ui& ui) {
    if(!ui.window.get())return;
    DpiScope context(ui.window.get());POINT position{};SIZE size{};
    if(!context.previous||!TargetPlacement(ui.host.parent,ui.physicalRect,position,size))return;
    auto limits=Limits(ui.window.get());
    if(!SetWindowPos(ui.window.get(),nullptr,position.x,position.y,
        std::clamp<int>(size.cx,limits.minW,limits.maxW),std::clamp<int>(size.cy,limits.minH,limits.maxH),
        SWP_NOZORDER|SWP_NOACTIVATE))FixedLog(L"Vibedotcom: display geometry mapping failed.\n");
    SyncViewport(ui);
}
void ProbeHost(Ui& ui,ULONGLONG tick) {
    HWND shell=GetShellWindow();if(!shell)shell=FindWindowW(L"Progman",nullptr);
    if(shell&&!InThisProcess(shell)) { StopUi(ui);return; }
    Host detected;
    const bool desktop=g_settings.mode==L"desktop-level";
    if(shell&&desktop)detected=FindHost();
    const bool missing=!shell||(desktop&&!detected.parent);
    if(missing) {
        const DWORD delay=HostRetryDelay(ui.missingRetries<4?ui.missingRetries:5);
        ui.nextProbe=tick+delay;
        if(desktop&&shell&&tick>=ui.nextShellRequest&&!g_shellBusy.load()) {
            HWND progman=FindWindowW(L"Progman",nullptr);
            if(InThisProcess(progman)) {
                g_shellBusy.store(true);g_shellOutcome.store(0);g_shellRequest.store(progman);
                ui.nextShellRequest=tick+delay;
                if(g_workWake)SetEvent(g_workWake);
            }
        }
        if(ui.missingRetries<4)++ui.missingRetries;
    } else {
        ui.missingRetries=0;ui.nextShellRequest=0;ui.nextProbe=tick+2000;
    }
    const bool displayChanged=std::exchange(ui.displayChanged,false);
    if(!shell) {
        if(displayChanged)RecoverFullyOffscreen(ui.physicalRect);
        HideTip(ui);UpdateVisibility(ui,tick);return;
    }
    if(detected.parent&&!(detected==ui.failedLayerHost))ui.failedLayerHost={};
    Host host=detected==ui.failedLayerHost?Host{}:detected;
    if(ui.window.get()&&!(host==ui.host)) {
        RememberPhysicalRect(ui);HideTip(ui);ui.active=false;ui.window.reset();ui.presented.reset();
    }
    if(displayChanged&&RecoverFullyOffscreen(ui.physicalRect))ui.geometryRefresh=true;
    if(!ui.window.get()) {
        if(!CreateOffice(ui,host)&&host.parent) {
            ui.failedLayerHost=host;CreateOffice(ui,{});
        }
    } else if(ui.geometryRefresh)ApplyPhysicalPlacement(ui);
    ui.geometryRefresh=false;
    if(desktop&&ui.window.get()) {
        HWND order=ui.host.parent?(ui.host.icons?ui.host.icons:HWND_TOP):HWND_BOTTOM;
        if(!ui.host.parent||!ui.host.icons||GetWindow(ui.window.get(),GW_HWNDPREV)!=ui.host.icons)
            if(!SetWindowPos(ui.window.get(),order,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE))
                FixedLog(L"Vibedotcom: office sibling placement failed.\n");
    }
}
void PresentLatest(Ui& ui) {
    if(ui.stopping||Stopping())return;
    UpdateVisibility(ui,GetTickCount64());
    if(!ui.active||!ui.window.get())return;
    auto packet=LatestPacket();
    if(!packet||!packet->pixels||packet->requestSerial!=ui.requestSerial||
       packet->pixels->width!=ui.width||packet->pixels->height!=ui.height)return;
    if(ui.presented&&ui.presented->pixels==packet->pixels&&!ui.retryPresentation) {
        ui.presented=std::move(packet);return; // Metadata-only update needs no blit.
    }
    const auto& pixels=*packet->pixels;
    if(!ui.surface.resize(pixels.width,pixels.height)) {
        ui.retryPresentation=true;
        FixedLog(L"Vibedotcom: presentation surface allocation failed; keeping the old frame.\n");return;
    }
    if(!ui.presented||ui.copiedPixelSerial!=pixels.serial) {
        const auto& bytes=ui.perPixel?pixels.bgra:pixels.framed;
        if(bytes.size()!=static_cast<size_t>(pixels.width)*pixels.height*4)return;
        std::memcpy(ui.surface.bits,bytes.data(),bytes.size());ui.copiedPixelSerial=pixels.serial;
    }
    if(ui.perPixel) {
        DpiScope context(ui.window.get());
        POINT source{};SIZE size{ui.width,ui.height};
        BLENDFUNCTION blend{AC_SRC_OVER,0,static_cast<BYTE>(g_settings.opacity*255/100),AC_SRC_ALPHA};
        // Keep the current position: pptDst=null avoids child/screen ambiguity.
        if(!context.previous||!UpdateLayeredWindow(ui.window.get(),nullptr,nullptr,&size,ui.surface.dc,&source,0,&blend,ULW_ALPHA)) {
            ui.retryPresentation=true;
            FixedLog(L"Vibedotcom: per-pixel presentation failed.\n");
            if(ui.host.parent) {
                Host failed=ui.host;RememberPhysicalRect(ui);HideTip(ui);ui.active=false;
                ui.window.reset();ui.failedLayerHost=failed;ui.presented.reset();
                // A new frameless top-level HWND retains per-pixel alpha.
                // The shell parent is never restyled, hidden or destroyed.
                CreateOffice(ui,{});ui.nextProbe=GetTickCount64()+2000;
            }
            return;
        }
    } else if(!ui.presented||ui.presented->pixels!=packet->pixels||ui.retryPresentation)
        InvalidateRect(ui.window.get(),nullptr,FALSE);
    ui.presented=std::move(packet);ui.retryPresentation=false;
}

bool UnderListView(HWND under) noexcept {
    unsigned count=0;
    for(HWND w=under;w&&count++<64;w=GetAncestor(w,GA_PARENT)) {
        wchar_t name[64]{};GetClassNameW(w,name,64);
        if(wcscmp(name,L"SysListView32")==0)return true;
    }
    return false;
}
bool HoverVisible(Ui& ui,POINT physical) noexcept {
    if(!ui.host.parent&&!ui.visibility.desktops)return false;
    DpiScope scope(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if(!scope.previous)return false;
    HWND under=WindowFromPoint(physical);
    // Suppress the whole list-view area; never send LVM_HITTEST or pointers.
    if(!under||UnderListView(under))return false;
    bool visible=under==ui.window.get()||IsChild(ui.window.get(),under);
    HWND root=GetAncestor(under,GA_ROOT);
    if(g_settings.through&&!ui.host.parent) {
        unsigned count=0;
        for(HWND top=GetTopWindow(nullptr);top&&count++<4096;top=GetWindow(top,GW_HWNDNEXT)) {
            if(top==ui.tip.get()||top==ui.helper.get()||!IsWindowVisible(top)||IsIconic(top)||ui.visibility.cloaked(top))continue;
            if(top==ui.window.get()) { visible=true;break; }
            if(top==root)break;
        }
    }
    if(g_settings.mode==L"desktop-level")
        visible=visible||root==ui.host.parent||root==ui.host.wallpaper||root==ui.host.iconHost;
    return visible;
}
bool SetTipDpi(Ui& ui,UINT dpi) noexcept {
    if(!dpi)dpi=96;
    if(ui.tipFont.value&&ui.tipDpi==dpi)return true;
    HFONT font=CreateFontW(-DpiPixels(13,dpi),0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,
        DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
    if(!font) { FixedLog(L"Vibedotcom: tooltip font allocation failed.\n");return false; }
    ui.tipFont.reset(font);ui.tipDpi=dpi;return true;
}
void PlaceTip(Ui& ui,POINT physical) {
    if(!ui.tip.get()||!ui.tipPacket||ui.tipIndex>=ui.tipPacket->hits.size())return;
    DpiScope scope(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if(!scope.previous||!SetTipDpi(ui,ui.visibility.cursorDpi(physical,ui.tip.get()))) { HideTip(ui,false);return; }
    MONITORINFO monitor{};monitor.cbSize=sizeof(monitor);
    if(!GetMonitorInfoW(MonitorFromPoint(physical,MONITOR_DEFAULTTONEAREST),&monitor)) { HideTip(ui,false);return; }
    const int padX=DpiPixels(12,ui.tipDpi),padY=DpiPixels(10,ui.tipDpi);
    const int width=std::max(1,std::min<int>(DpiPixels(364,ui.tipDpi),monitor.rcWork.right-monitor.rcWork.left));
    const int maxHeight=std::max(1,std::min<int>(DpiPixels(440,ui.tipDpi),monitor.rcWork.bottom-monitor.rcWork.top));
    const auto& text=ui.tipPacket->hits[ui.tipIndex].tooltip;
    WindowDc dc(ui.tip.get());if(!dc.dc) { HideTip(ui,false);return; }
    int height=0;
    { DcSelection font(dc.dc,ui.tipFont.value);
      if(!font.valid()) { HideTip(ui,false);return; }
      RECT measure{0,0,std::max(1,width-padX*2),0};
      DrawTextW(dc.dc,text.c_str(),static_cast<int>(text.size()),&measure,DT_CALCRECT|DT_WORDBREAK|DT_NOPREFIX);
      height=std::clamp<int>(measure.bottom+padY*2,1,maxHeight); }
    const LONG x=std::clamp<LONG>(physical.x+DpiPixels(18,ui.tipDpi),monitor.rcWork.left,
        std::max<LONG>(monitor.rcWork.left,monitor.rcWork.right-width));
    const LONG y=std::clamp<LONG>(physical.y+DpiPixels(22,ui.tipDpi),monitor.rcWork.top,
        std::max<LONG>(monitor.rcWork.top,monitor.rcWork.bottom-height));
    if(!SetWindowPos(ui.tip.get(),HWND_TOPMOST,x,y,width,height,SWP_NOACTIVATE|SWP_SHOWWINDOW)) { HideTip(ui,false);return; }
    InvalidateRect(ui.tip.get(),nullptr,FALSE);ui.tipShown=true;
}
void Hover(Ui& ui,ULONGLONG tick) {
    const bool hadHover=ui.hoverPacket!=nullptr;
    auto clear=[&]() { HideTip(ui);if(hadHover)PublishRequest(ui); };
    if(!g_settings.tooltip||ui.inMenu||ui.inSizeMove||!OfficeShown(ui,tick)||!ui.presented||!ui.presented->hasState||
       ui.presented->pixels->width!=ui.width||ui.presented->pixels->height!=ui.height) { clear();return; }
    POINT physical{};
    if(!GetPhysicalCursorPos(&physical)||!HoverVisible(ui,physical)) { clear();return; }
    POINT local=physical;
    { DpiScope context(ui.window.get());
      if(!context.previous||!PhysicalToLogicalPointForPerMonitorDPI(ui.window.get(),&local)||
         !ScreenToClient(ui.window.get(),&local)) { clear();return; } }
    if(local.x<0||local.y<0||local.x>=ui.width||local.y>=ui.height) { clear();return; }
    size_t hit=kNoHit;
    for(size_t i=0;i<ui.presented->hits.size();++i)
        if(ui.presented->hits[i].bounds.Contains(local.x,local.y)) { hit=i;break; }
    if(hit==kNoHit) { clear();return; }
    const auto& agent=ui.presented->hits[hit].agent;
    if(!ui.hoverPacket||ui.hoverIndex>=ui.hoverPacket->hits.size()||ui.hoverPacket->hits[ui.hoverIndex].agent.id!=agent.id) {
        HideTip(ui);ui.hoverPacket=ui.presented;ui.hoverIndex=hit;ui.hoverSince=tick;PublishRequest(ui);
    }
    if(tick-ui.hoverSince<220||(ui.tipShown&&tick-ui.lastTip<200))return;
    if(!ui.tip.get()) {
        DpiScope context(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        if(!context.previous)return;
        ui.tip.value=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT,kTipClass,L"",WS_POPUP,
            0,0,0,0,ui.helper.get(),nullptr,g_module,&ui);
        if(!ui.tip.get())return;
    }
    ui.tipPacket=ui.presented;ui.tipIndex=hit;PlaceTip(ui,physical);ui.lastTip=tick;
}

LRESULT ViewBody(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE) {
        ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
        if(!ui)return FALSE;
        SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));ui->window.value=w;g_office.store(w);
    }
    if(ui)switch(msg) {
        case kShutdown:StopUi(*ui);return 0;
        case WM_ERASEBKGND:return 1;
        case WM_PAINT: {
            PaintDc paint(w);
            if(paint.dc&&!ui->perPixel) {
                RECT client{};GetClientRect(w,&client);
                FillRect(paint.dc,&client,static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
                if(ui->surface.dc&&ui->presented)
                    BitBlt(paint.dc,0,0,std::min(ui->width,ui->surface.width),std::min(ui->height,ui->surface.height),ui->surface.dc,0,0,SRCCOPY);
            }
            return 0;
        }
        case WM_WINDOWPOSCHANGING: {
            auto* position=reinterpret_cast<WINDOWPOS*>(lp);
            if(!(position->flags&SWP_NOSIZE)) {
                auto limits=Limits(w);
                position->cx=std::clamp(position->cx,limits.minW,limits.maxW);
                position->cy=std::clamp(position->cy,limits.minH,limits.maxH);
            }
            break;
        }
        case WM_WINDOWPOSCHANGED:RememberPhysicalRect(*ui);break;
        case WM_SIZE:SyncViewport(*ui);UpdateVisibility(*ui,GetTickCount64());return 0;
        case WM_GETMINMAXINFO: {
            auto* info=reinterpret_cast<MINMAXINFO*>(lp);auto limits=Limits(w);
            info->ptMinTrackSize={limits.minW,limits.minH};info->ptMaxTrackSize={limits.maxW,limits.maxH};
            MONITORINFO monitor{};monitor.cbSize=sizeof(monitor);
            if(GetMonitorInfoW(MonitorFromWindow(w,MONITOR_DEFAULTTONEAREST),&monitor)) {
                info->ptMaxPosition={monitor.rcWork.left-monitor.rcMonitor.left,monitor.rcWork.top-monitor.rcMonitor.top};
                info->ptMaxSize={std::min<LONG>(limits.maxW,monitor.rcWork.right-monitor.rcWork.left),
                                 std::min<LONG>(limits.maxH,monitor.rcWork.bottom-monitor.rcWork.top)};
            } else info->ptMaxSize={limits.maxW,limits.maxH};
            return 0;
        }
        case WM_DPICHANGED: {
            HideTip(*ui);
            if(!ui->host.parent) {
                const auto* rect=reinterpret_cast<const RECT*>(lp);
                SetWindowPos(w,nullptr,rect->left,rect->top,rect->right-rect->left,rect->bottom-rect->top,SWP_NOZORDER|SWP_NOACTIVATE);
            } else ui->geometryRefresh=true;
            ui->nextProbe=0;SyncViewport(*ui);PublishRequest(*ui);return 0;
        }
        case WM_DPICHANGED_AFTERPARENT:
            ui->geometryRefresh=true;ui->nextProbe=0;HideTip(*ui);SyncViewport(*ui);PublishRequest(*ui);return 0;
        case WM_DISPLAYCHANGE:
            ui->displayChanged=true;ui->geometryRefresh=true;ui->nextProbe=0;HideTip(*ui);PublishRequest(*ui);break;
        case WM_ENTERSIZEMOVE:case WM_ENTERMENULOOP:
            if(msg==WM_ENTERSIZEMOVE)ui->inSizeMove=true;else ui->inMenu=true;
            HideTip(*ui);PublishRequest(*ui);
            if(!SetTimer(w,kStopTimer,50,nullptr)) { FixedLog(L"Vibedotcom: modal stop timer failed.\n");StopUi(*ui); }
            return 0;
        case WM_EXITSIZEMOVE:case WM_EXITMENULOOP:
            if(msg==WM_EXITSIZEMOVE)ui->inSizeMove=false;else ui->inMenu=false;
            if(!ui->inSizeMove&&!ui->inMenu)KillTimer(w,kStopTimer);
            RememberPhysicalRect(*ui);SyncViewport(*ui);return 0;
        case WM_TIMER:
            if(wp==kStopTimer) { if(Stopping())StopUi(*ui);return 0; }break;
        case WM_MOUSEWHEEL:case WM_KEYDOWN: {
            int step=0;
            if(msg==WM_MOUSEWHEEL)step=static_cast<short>(HIWORD(wp))>0?1:-1;
            else if(wp==VK_ADD||wp==VK_OEM_PLUS)step=1;
            else if(wp==VK_SUBTRACT||wp==VK_OEM_MINUS)step=-1;
            if(step) {
                const double camera=ui->zoom>0?ui->zoom:ui->presented?ui->presented->cameraZoom:1;
                const int base=static_cast<int>(std::floor(camera));
                ui->zoom=std::clamp(base+step,1,6);HideTip(*ui);PublishRequest(*ui);return 0;
            }
            break;
        }
        case WM_MOUSEACTIVATE:
            if(g_settings.through||g_settings.mode==L"desktop-level")return MA_NOACTIVATE;break;
        case WM_NCHITTEST:if(g_settings.through)return HTTRANSPARENT;break;
        case WM_CLOSE:StopUi(*ui);return 0;
        case WM_NCDESTROY: {
            HWND expected=w;g_office.compare_exchange_strong(expected,nullptr);
            ui->window.forget(w);ui->active=false;HideTip(*ui);ui->nextProbe=0;
            SetWindowLongPtrW(w,GWLP_USERDATA,0);PublishRequest(*ui);break;
        }
    }
    return DefWindowProcW(w,msg,wp,lp);
}
LRESULT HelperBody(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE) {
        ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
        if(!ui)return FALSE;
        SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));ui->helper.value=w;g_notify.store(w);
    }
    if(ui) {
        if(msg==kShutdown) { StopUi(*ui);return 0; }
        if(msg==kPacketReady) { PresentLatest(*ui);return 0; }
        if(msg==ui->taskbarCreated||msg==WM_DISPLAYCHANGE||msg==WM_SETTINGCHANGE||msg==WM_THEMECHANGED) {
            ui->nextProbe=0;ui->geometryRefresh=true;HideTip(*ui);
            if(msg==WM_DISPLAYCHANGE||(msg==WM_SETTINGCHANGE&&wp==SPI_SETWORKAREA))ui->displayChanged=true;
            PublishRequest(*ui);
        }
        if(msg==WM_NCDESTROY) {
            HWND expected=w;g_notify.compare_exchange_strong(expected,nullptr);
            ui->helper.forget(w);SetWindowLongPtrW(w,GWLP_USERDATA,0);
        }
    }
    return DefWindowProcW(w,msg,wp,lp);
}
LRESULT TipBody(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE) {
        ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
        if(!ui)return FALSE;
        SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));ui->tip.value=w;
    }
    if(msg==WM_MOUSEACTIVATE)return MA_NOACTIVATE;
    if(msg==WM_NCHITTEST)return HTTRANSPARENT;
    if(msg==WM_ERASEBKGND)return 1;
    if(ui)switch(msg) {
        case WM_PAINT: {
            PaintDc paint(w);
            if(paint.dc) {
                RECT rect{};GetClientRect(w,&rect);
                SetDCBrushColor(paint.dc,RGB(48,61,64));FillRect(paint.dc,&rect,static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
                if(ui->tipPacket&&ui->tipIndex<ui->tipPacket->hits.size()&&ui->tipFont.value) {
                    DcSelection font(paint.dc,ui->tipFont.value);
                    if(font.valid()) {
                        SetBkMode(paint.dc,TRANSPARENT);SetTextColor(paint.dc,RGB(255,244,223));
                        InflateRect(&rect,-DpiPixels(12,ui->tipDpi),-DpiPixels(10,ui->tipDpi));
                        const auto& text=ui->tipPacket->hits[ui->tipIndex].tooltip;
                        DrawTextW(paint.dc,text.c_str(),static_cast<int>(text.size()),&rect,DT_LEFT|DT_WORDBREAK|DT_NOPREFIX);
                    }
                }
            }
            return 0;
        }
        case WM_DPICHANGED: {
            SetTipDpi(*ui,HIWORD(wp));
            const auto* rect=reinterpret_cast<const RECT*>(lp);
            SetWindowPos(w,nullptr,rect->left,rect->top,rect->right-rect->left,rect->bottom-rect->top,SWP_NOZORDER|SWP_NOACTIVATE);
            POINT physical{};if(GetPhysicalCursorPos(&physical))PlaceTip(*ui,physical);
            InvalidateRect(w,nullptr,FALSE);return 0;
        }
        case kShutdown:StopUi(*ui);return 0;
        case WM_NCDESTROY:ui->tip.forget(w);HideTip(*ui,false);SetWindowLongPtrW(w,GWLP_USERDATA,0);break;
    }
    return DefWindowProcW(w,msg,wp,lp);
}
LRESULT CallbackFailure(UINT msg,const wchar_t* message) noexcept {
    FixedLog(message);SignalStop();return msg==WM_NCCREATE?FALSE:msg==WM_CREATE?-1:0;
}
LRESULT ViewCppBoundary(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    try { return ViewBody(w,msg,wp,lp); }
    catch(...) { return CallbackFailure(msg,L"Vibedotcom: office callback failed.\n"); }
}
LRESULT HelperCppBoundary(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    try { return HelperBody(w,msg,wp,lp); }
    catch(...) { return CallbackFailure(msg,L"Vibedotcom: helper callback failed.\n"); }
}
LRESULT TipCppBoundary(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    try { return TipBody(w,msg,wp,lp); }
    catch(...) { return CallbackFailure(msg,L"Vibedotcom: tooltip callback failed.\n"); }
}
// SEH is confined to callbacks. No process policy, VEH or exception-filter
// replacement is installed. The implementation functions own all C++ scopes.
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
LRESULT CALLBACK ViewProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    __try { return ViewCppBoundary(w,msg,wp,lp); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return CallbackFailure(msg,L"Vibedotcom: office callback native fault.\n"); }
}
LRESULT CALLBACK HelperProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    __try { return HelperCppBoundary(w,msg,wp,lp); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return CallbackFailure(msg,L"Vibedotcom: helper callback native fault.\n"); }
}
LRESULT CALLBACK TipProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept {
    __try { return TipCppBoundary(w,msg,wp,lp); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return CallbackFailure(msg,L"Vibedotcom: tooltip callback native fault.\n"); }
}
#else
LRESULT CALLBACK ViewProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept { return ViewCppBoundary(w,msg,wp,lp); }
LRESULT CALLBACK HelperProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept { return HelperCppBoundary(w,msg,wp,lp); }
LRESULT CALLBACK TipProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) noexcept { return TipCppBoundary(w,msg,wp,lp); }
#endif
struct WindowClass {
    const wchar_t* name;bool registered=false;
    explicit WindowClass(const wchar_t* value) noexcept : name(value) {}
    void init(WNDPROC proc) {
        WNDCLASSW type{};type.hInstance=g_module;type.lpszClassName=name;
        type.lpfnWndProc=proc;type.hCursor=LoadCursorW(nullptr,IDC_ARROW);
        registered=RegisterClassW(&type)!=0;Require(registered,"Window class registration failed");
    }
    ~WindowClass() noexcept { if(registered)UnregisterClassW(name,g_module); }
};
struct UiWork { Ui* ui;WindowClass* view;WindowClass* helper;WindowClass* tip; };
void UiLoopBody(UiWork* work) noexcept {
    try {
            Ui& ui=*work->ui;
            ui.zoom=g_settings.zoom;ui.visibility.init();
            ui.taskbarCreated=RegisterWindowMessageW(L"TaskbarCreated");
            work->view->init(ViewProc);work->helper->init(HelperProc);work->tip->init(TipProc);
            if(!Stopping()) {
                ui.helper.value=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,kHelperClass,L"",WS_POPUP,
                    0,0,0,0,nullptr,nullptr,g_module,&ui);
                Require(ui.helper.get()!=nullptr,"Helper window creation failed");
            }
            while(ui.running&&!Stopping()) {
                auto tick=GetTickCount64();
                if(tick>=ui.nextProbe)ProbeHost(ui,tick);
                if(!ui.running||Stopping())break;
                if(tick>=ui.nextVisibility) { ui.nextVisibility=tick+100;UpdateVisibility(ui,tick); }
                if(ui.retryPresentation)PresentLatest(ui);
                if(tick>=ui.nextHover) { ui.nextHover=tick+100;Hover(ui,tick); }
                ULONGLONG due=std::min(ui.nextProbe,ui.nextVisibility);
                if(g_settings.tooltip&&ui.window.get())due=std::min(due,ui.nextHover);
                const auto after=GetTickCount64();
                const DWORD delay=static_cast<DWORD>(due>after?std::min<ULONGLONG>(due-after,100):1);
                const DWORD result=MsgWaitForMultipleObjectsEx(1,&g_stop,delay,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
                if(result==WAIT_OBJECT_0)break;
                if(result==WAIT_FAILED) { FixedLog(L"Vibedotcom: UI message wait failed.\n");break; }
                MSG message{};
                while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
                    if(message.message==WM_QUIT||message.message==kShutdown) { StopUi(ui);break; }
                    TranslateMessage(&message);DispatchMessageW(&message);
                    if(!ui.running||Stopping())break;
                }
            }
    } catch(...) {
        FixedLog(L"Vibedotcom: UI stopped; releasing its windows and GDI resources.\n");SignalStop();
    }
}
bool UiScopeBoundary(UiWork* work) noexcept {
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
    __try { UiLoopBody(work);return true; }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        FixedLog(L"Vibedotcom: UI native fault; leaving its resource scope.\n");SignalStop();return false;
    }
#else
    UiLoopBody(work);return true;
#endif
}
void JoinSceneFromUi() noexcept {
    // Wh_ModUninit closes g_worker only after the UI thread has returned.
    // Synchronous metadata/open calls depend on driver cancellation support;
    // decoding is bounded by validated input, not by a hard execution deadline.
    if(g_worker) {
        // A metadata/open call may start just after an earlier cancellation.
        // Keep cancelling pending synchronous IO until the worker is joined.
        while(WaitForSingleObject(g_worker,50)==WAIT_TIMEOUT)CancelSynchronousIo(g_worker);
    }
}
DWORD UiBody(void*) noexcept {
    try {
        // Create the queue before publishing the thread identifier.
        MSG seed{};PeekMessageW(&seed,nullptr,0,0,PM_NOREMOVE);
        g_uiThreadId.store(GetCurrentThreadId());
        DpiScope dpi(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Require(dpi.previous!=nullptr,"UI DPI context unavailable");
        WindowClass view(kViewClass),helper(kHelperClass),tip(kTipClass);
        Ui ui; // Owners stay outside the SEH body, including HWND userdata.
        UiWork work{&ui,&view,&helper,&tip};
        UiScopeBoundary(&work);
        StopUi(ui);
    } catch(...) { FixedLog(L"Vibedotcom: UI initialization failed.\n");SignalStop(); }
    // UI windows are already destroyed, so this join cannot keep a child
    // input queue attached. WM_CLOSE takes this path as well as mod unload.
    JoinSceneFromUi();
    g_uiThreadId.store(0);return 0;
}
DWORD WINAPI UiThread(void* value) noexcept {
#if defined(_MSC_VER) || (defined(__clang__) && defined(_WIN32) && defined(__x86_64__))
    __try { return UiBody(value); }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        FixedLog(L"Vibedotcom: UI thread native fault.\n");SignalStop();JoinSceneFromUi();
        g_uiThreadId.store(0);return 1;
    }
#else
    return UiBody(value);
#endif
}
#ifndef VIBEDOTCOM_HOST_TEST
std::wstring StringSetting(const wchar_t* name) {
    struct SettingLease {
        PCWSTR text;
        ~SettingLease() noexcept { if(text)Wh_FreeStringSetting(text); }
    } setting{Wh_GetStringSetting(name)};
    return setting.text?setting.text:L"";
}
#endif
void JoinThread(HANDLE& thread) noexcept {
    if(!thread)return;
    WaitForSingleObject(thread,INFINITE);CloseHandle(thread);thread=nullptr;
}
void CloseEvent(HANDLE& event) noexcept {
    if(event)CloseHandle(std::exchange(event,nullptr));
}
} // namespace

#ifndef VIBEDOTCOM_HOST_TEST
BOOL Wh_ModInit() {
    try {
        HWND shell=GetShellWindow();if(!shell)shell=FindWindowW(L"Progman",nullptr);
        if(shell&&!InThisProcess(shell))return FALSE;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(UiThread),&g_module))return FALSE;
        g_settings.mode=StringSetting(L"mode");
        if(g_settings.mode!=L"normal"&&g_settings.mode!=L"topmost"&&g_settings.mode!=L"desktop-level")g_settings.mode=L"normal";
        g_settings.state=LocalPath(StringSetting(L"statePath"));
        auto pack=StringSetting(L"packPath");g_settings.pack=pack.empty()?L"":LocalPath(pack);
        g_settings.x=std::clamp(Wh_GetIntSetting(L"x"),-32000,32000);
        g_settings.y=std::clamp(Wh_GetIntSetting(L"y"),-32000,32000);
        g_settings.w=std::clamp(Wh_GetIntSetting(L"width"),240,kMaxWidth);
        g_settings.h=std::clamp(Wh_GetIntSetting(L"height"),160,kMaxHeight);
        g_settings.zoom=std::clamp(Wh_GetIntSetting(L"zoom"),0,6);
        g_settings.opacity=std::clamp(Wh_GetIntSetting(L"opacity"),10,100);
        g_settings.fps=std::clamp(Wh_GetIntSetting(L"fpsCap"),1,60);
        g_settings.through=Wh_GetIntSetting(L"clickThrough")!=0;
        g_settings.tooltip=Wh_GetIntSetting(L"showTooltip")!=0;
        g_settings.transparent=Wh_GetIntSetting(L"transparentBackground")!=0;
        ClearMailbox();
        g_shellRequest.store(nullptr);g_shellBusy.store(false);g_shellOutcome.store(0);
        g_office.store(nullptr);g_notify.store(nullptr);g_uiThreadId.store(0);
        g_stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        g_workWake=CreateEventW(nullptr,FALSE,FALSE,nullptr);
        if(!g_stop||!g_workWake) { CloseEvent(g_workWake);CloseEvent(g_stop);return FALSE; }
        return TRUE;
    } catch(...) {
        FixedLog(L"Vibedotcom: invalid viewer settings or initialization failure.\n");
        CloseEvent(g_workWake);CloseEvent(g_stop);return FALSE;
    }
}
void Wh_ModAfterInit() {
    g_worker=CreateThread(nullptr,0,SceneThread,nullptr,0,nullptr);
    if(!g_worker) { FixedLog(L"Vibedotcom: scene worker creation failed.\n");SignalStop();return; }
    g_thread=CreateThread(nullptr,0,UiThread,nullptr,0,nullptr);
    if(!g_thread) {
        FixedLog(L"Vibedotcom: UI thread creation failed.\n");SignalStop();
        JoinSceneFromUi();JoinThread(g_worker);
    }
}
BOOL Wh_ModSettingsChanged(BOOL* reload) { *reload=TRUE;return TRUE; }
void Wh_ModUninit() {
    SignalStop(); // Event + PostThreadMessage + atomic HWND messages, including native loops.
    if(g_worker)CancelSynchronousIo(g_worker); // readStop also cancels overlapped file IO.
    JoinThread(g_thread);JoinThread(g_worker); // Never unload live callback/worker code.
    try { ClearMailbox(); }
    catch(...) { FixedLog(L"Vibedotcom: mailbox cleanup failed.\n"); }
    g_shellRequest.store(nullptr);g_shellBusy.store(false);
    g_office.store(nullptr);g_notify.store(nullptr);g_uiThreadId.store(0);
    CloseEvent(g_workWake);CloseEvent(g_stop);g_settings={};
}
#endif
// ===== END WINDHAWK WINDOW HOST =====
#endif
