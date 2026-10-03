// ==WindhawkMod==
// @id              vibedotcom
// @name            Vibedotcom pixel office
// @description     Passive pixel office driven by the headless Vibedotcom tracker
// @version         0.1.0
// @author          Vibedotcom contributors
// @include         explorer.exe
// @architecture    amd64
// @compilerOptions -lgdiplus -lgdi32 -luser32 -lbcrypt -lole32
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
Run Vibedotcom.Tracker separately. This mod reads version 3 or 4 state.json
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
#include <bcrypt.h>
#include <algorithm>
#include <array>
#include <charconv>
#include <cmath>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
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
        Require(depth<=64 && ++nodes<=250000,"JSON complexity limit"); Space(); Json j; j.begin=p; char c=Peek();
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
std::wstring SheetPath(const std::wstring& root,const std::string& relative) {
    auto r=Wide(relative); Require(!r.empty() && r[0]!=L'/' && r[0]!=L'\\' && r.find(L':')==r.npos,"Relative sheet path required");
    std::replace(r.begin(),r.end(),L'/',L'\\');
    size_t p=0; while(p<r.size()) { size_t end=r.find(L'\\',p); auto part=r.substr(p,end==r.npos?r.npos:end-p);
        Require(!part.empty()&&part!=L"."&&part!=L".."&&part.back()!=L'.'&&part.back()!=L' ',"Invalid sheet component"); if(end==r.npos) break; p=end+1; }
    auto path=LocalPath(root+L"\\"+r); Require(path.size()>root.size()&&_wcsnicmp(path.c_str(),root.c_str(),root.size())==0&&path[root.size()]==L'\\',"Sheet escapes root"); return path;
}
struct FileHandle {
    HANDLE h=INVALID_HANDLE_VALUE;
    explicit FileHandle(HANDLE value):h(value) {} ~FileHandle(){ if(h!=INVALID_HANDLE_VALUE) CloseHandle(h); }
    FileHandle(const FileHandle&)=delete;
};
std::string ReadFileBytes(const std::wstring& path,size_t max,const std::wstring& root={}) {
    FileHandle f(CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN,nullptr));
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
    Require(ReadFile(f.h,bytes.data(),static_cast<DWORD>(bytes.size()),&read,nullptr)&&read==bytes.size(),"Incomplete read");
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
struct Furniture { std::wstring ordinal;std::string id,kind,asset,state,owner;int x=0,y=0,w=0,h=0,rotation=0;Variant variant;Frame frame;bool hasFrame=false; };
struct Agent { std::wstring ordinal;std::string id,parent,provider,name,role,model,project,task,tool,toolKind,state,action,facing,reaction,partner,character;
    std::string displayName,workLocation="desk",providerIcon;bool queued=false;
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
    s.schema=static_cast<int>(j.At("schemaVersion").Int(3,4));s.sequence=j.At("sequence").Int(1,INT64_MAX);s.generated=Timestamp(j.At("generatedAt").Str());
    const auto& atlas=j.At("atlas");atlas.At("version").Int(2,2);
    const auto& m=j.At("assets");const auto& o=j.At("office");
    if(!reuse){m.At("version").Int(1,1);
    for(const auto& v:m.At("sheets").Items(128)) {
        Sheet sh;sh.id=v.At("id").Str();sh.file=v.At("file").Str();sh.sha=v.At("sha256").Str();
        Require(!sh.id.empty()&&sh.sha.size()==64&&sh.sha.find_first_not_of("0123456789abcdef")==std::string::npos,"Invalid sheet identity/hash");
        // Validate relative path even before a configured root is known.
        SheetPath(L"C:\\Vibedotcom",sh.file);
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
    }
    if(!j.At("furnitureSprites").array.empty())for(const auto& f:s.furniture)Require(f.variant.sheet.empty()||f.hasFrame,"Missing authoritative furniture frame");
    if(!reuse&&o.Optional("zones").kind!=Json::Null)for(const auto& v:o.At("zones").Items(256)) { Zone z;z.name=v.At("name").Str();z.x=static_cast<int>(v.At("x").Int(0,s.w-1));z.y=static_cast<int>(v.At("y").Int(0,s.h-1));z.w=static_cast<int>(v.At("width").Int(1,s.w));z.h=static_cast<int>(v.At("height").Int(1,s.h));Require(z.x+z.w<=s.w&&z.y+z.h<=s.h,"Zone bounds");s.zones.push_back(z); }
    seen.clear();
    for(const auto& v:j.At("agents").Items(2048)) {
        const auto& a=v.At("agent");const auto& p=v.At("pose");Agent t;
        t.id=a.At("id").Str();t.ordinal=Wide(t.id);Require(!t.id.empty()&&seen.insert(t.id).second,"Duplicate/empty agent ID");t.parent=a.Optional("parentId").StrOr();t.provider=a.At("provider").Str();Require(t.provider=="codex"||t.provider=="claude"||t.provider=="antigravity","Unknown provider");
        t.name=a.At("name").Str();t.role=a.Optional("role").StrOr();t.model=a.Optional("model").StrOr("unknown");t.project=a.Optional("cwd").StrOr("unknown");t.task=a.At("task").Str();t.tool=a.Optional("tool").StrOr("none");t.toolKind=a.Optional("toolKind").StrOr();t.state=a.At("state").Str();t.sub=a.At("isSubagent").Bool();t.since=Timestamp(a.At("stateSince").Str());Timestamp(a.At("lastSeen").Str());
        t.x=p.At("x").Num(-1,s.w);t.y=p.At("y").Num(-1,s.h);t.dx=static_cast<int>(p.At("desk").At("x").Int(s.schema==4?-1:0,s.w-1));t.dy=static_cast<int>(p.At("desk").At("y").Int(s.schema==4?-1:0,s.h-1));
        t.action=p.At("action").Str();t.facing=p.At("facing").Str();t.visible=p.At("visible").Bool();t.opacity=p.Optional("opacity").NumOr(1,0,1);t.carrying=p.Optional("carryingPaper").BoolOr(false);t.paper=p.Optional("reportOnDesk").BoolOr(false);t.reaction=p.Optional("reaction").StrOr();t.partner=p.Optional("chatPartnerId").StrOr();t.character=p.Optional("characterId").StrOr();
        t.displayName=p.Optional("displayName").StrOr(t.name);t.providerIcon=p.Optional("providerIcon").StrOr();t.workLocation=p.Optional("workLocation").StrOr("desk");t.queued=p.Optional("queued").BoolOr(false);
        t.frame=ParseFrame(p.At("sprite"),s);s.agents.push_back(t);
    }
    if(!reuse){s.rawAssets=bytes.substr(m.begin,m.end-m.begin);s.rawOffice=bytes.substr(o.begin,o.end-o.begin);s.staticKey=Hash(s.rawOffice+s.rawAssets);}return s;
}
struct Bounds {double x=0,y=0,w=0,h=0;bool Contains(double px,double py) const{return px>=x&&py>=y&&px<x+w&&py<y+h;}};
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
std::string Sha256(const std::string& bytes) {
    BCRYPT_ALG_HANDLE alg=nullptr;Require(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)==0,"SHA256 provider");
    std::array<UCHAR,32> digest{};auto status=BCryptHash(alg,nullptr,0,reinterpret_cast<PUCHAR>(const_cast<char*>(bytes.data())),static_cast<ULONG>(bytes.size()),digest.data(),static_cast<ULONG>(digest.size()));
    BCryptCloseAlgorithmProvider(alg,0);Require(status==0,"SHA256 failed");std::string out;constexpr char hex[]="0123456789abcdef";for(auto b:digest){out+=hex[b>>4];out+=hex[b&15];}return out;
}
std::unique_ptr<Bitmap> Decode(const std::string& bytes,const Sheet& sheet) {
    Require(bytes.size()>=33&&bytes.compare(0,8,"\x89PNG\r\n\x1a\n",8)==0&&bytes.compare(12,4,"IHDR")==0,"Expected PNG");
    auto be=[&](size_t p){uint32_t n=0;for(size_t i=p;i<p+4;++i)n=(n<<8)|static_cast<unsigned char>(bytes[i]);return n;};
    Require(be(16)==static_cast<unsigned>(sheet.w)&&be(20)==static_cast<unsigned>(sheet.h)&&bytes[24]==8&&bytes[26]==0&&bytes[27]==0&&bytes[28]==0,"PNG metadata mismatch");
    Require(Sha256(bytes)==sheet.sha,"PNG hash mismatch");
    HGLOBAL memory=GlobalAlloc(GMEM_MOVEABLE,bytes.size());Require(memory!=nullptr,"PNG memory allocation");
    void* dest=GlobalLock(memory);if(!dest){GlobalFree(memory);throw Error("PNG memory lock");}memcpy(dest,bytes.data(),bytes.size());GlobalUnlock(memory);
    IStream* stream=nullptr;auto hr=CreateStreamOnHGlobal(memory,TRUE,&stream);if(FAILED(hr)){GlobalFree(memory);throw Error("PNG stream");}
    std::unique_ptr<Bitmap> image;
    { Bitmap decoded(stream,FALSE);
      if(decoded.GetLastStatus()==Ok&&decoded.GetWidth()==static_cast<unsigned>(sheet.w)&&decoded.GetHeight()==static_cast<unsigned>(sheet.h))image.reset(decoded.Clone(0,0,sheet.w,sheet.h,PixelFormat32bppPARGB)); }
    stream->Release();Require(image&&image->GetLastStatus()==Ok,"PNG decode failed");return image;
}
Color HexColor(const std::string& s,BYTE alpha=255) {unsigned value=0;std::from_chars(s.data()+1,s.data()+s.size(),value,16);return Color(alpha,(value>>16)&255,(value>>8)&255,value&255);}
Color ProviderColor(const Agent& a,BYTE alpha=255) {return HexColor(a.provider=="codex"?"#329b91":a.provider=="claude"?"#d57e4e":"#827ed1",alpha);}
struct Camera {int zoom=1;double x=0,y=0;};
Camera ViewCamera(const Snapshot& s,int width,int height,int requested) {
    int z=requested?std::clamp(requested,1,6):std::clamp(static_cast<int>(std::floor(std::min(std::max(1,width-20)/double(s.w*16),std::max(1,height-16)/double(s.h*16)))),1,6);
    return {z,std::nearbyint((width-s.w*16*z)/2.0),std::nearbyint((height-s.h*16*z)/2.0)};
}
class Renderer {
    struct Image {std::wstring path;std::string sha;std::unique_ptr<Bitmap> pixels;};
    std::map<std::string,Image> images;
    using CropKey=std::tuple<std::string,int,int,int,int>;
    std::map<CropKey,std::unique_ptr<Bitmap>> crops;
    uint64_t cropBytes=0;
    std::map<CropKey,int> tops;
    std::map<CropKey,Bounds> content;
    std::unique_ptr<Bitmap> room;
    std::string loadedManifest;
    std::wstring loadedRoot;
    uint64_t roomKey=0;bool roomTransparent=false;
    FontFamily family{L"Segoe UI"};
    std::map<int,std::unique_ptr<Font>> fonts;
    static void Configure(Graphics& g) {g.SetSmoothingMode(SmoothingModeNone);g.SetInterpolationMode(InterpolationModeNearestNeighbor);g.SetPixelOffsetMode(PixelOffsetModeHalf);g.SetCompositingQuality(CompositingQualityHighSpeed);}
    static BYTE Alpha(double opacity) {return static_cast<BYTE>(std::clamp(opacity,0.0,1.0)*255+.5);}
    void Rect(Graphics& g,const std::string& color,double x,double y,double w,double h,double opacity=1) {
        if(w<=0||h<=0)return;SolidBrush b(HexColor(color,Alpha(opacity)));g.FillRectangle(&b,static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(w),static_cast<REAL>(h));
    }
    void Text(Graphics& g,const std::string& value,double x,double y,int size,const std::string& color="#394940",bool bold=false,double width=500) {
        int key=size*2+(bold?1:0);if(!fonts.contains(key))fonts[key]=std::make_unique<Font>(&family,static_cast<REAL>(size),bold?FontStyleBold:FontStyleRegular,UnitPixel);
        SolidBrush brush(HexColor(color));StringFormat format(StringFormat::GenericTypographic());format.SetFormatFlags(StringFormatFlagsNoWrap);format.SetTrimming(StringTrimmingEllipsisCharacter);
        auto text=Wide(value);g.DrawString(text.c_str(),static_cast<INT>(text.size()),fonts.at(key).get(),RectF(static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(std::max(1.0,width)),static_cast<REAL>(size*2+4)),&format,&brush);
    }
    Bitmap& Crop(const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=crops.find(key);if(it!=crops.end())return *it->second;
        uint64_t bytes=uint64_t(f.w)*f.h*4;
        if(crops.size()>=512||cropBytes+bytes>64*1024*1024){crops.clear();tops.clear();content.clear();cropBytes=0;}
        auto bitmap=std::unique_ptr<Bitmap>(images.at(f.sheet).pixels->Clone(f.x,f.y,f.w,f.h,PixelFormat32bppPARGB));Require(bitmap&&bitmap->GetLastStatus()==Ok,"Crop failed");
        auto* ptr=bitmap.get();crops.emplace(key,std::move(bitmap));cropBytes+=bytes;return *ptr;
    }
    void Sprite(Graphics& g,const Frame& f,const Bounds& r,double opacity=1) {
        ImageAttributes attributes;ColorMatrix matrix={{{1,0,0,0,0},{0,1,0,0,0},{0,0,1,0,0},{0,0,0,static_cast<REAL>(opacity),0},{0,0,0,0,1}}};
        attributes.SetColorMatrix(&matrix);attributes.SetWrapMode(WrapModeTileFlipXY);auto& bitmap=Crop(f);
        g.DrawImage(&bitmap,RectF(static_cast<REAL>(r.x),static_cast<REAL>(r.y),static_cast<REAL>(r.w),static_cast<REAL>(r.h)),0,0,static_cast<REAL>(f.w),static_cast<REAL>(f.h),UnitPixel,&attributes);
    }
    double HeadY(const Agent& a,const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=tops.find(key);
        if(it==tops.end()) {
            auto& bitmap=Crop(f);BitmapData data{};Gdiplus::Rect rect(0,0,f.w,f.h);int top=0;
            Require(bitmap.LockBits(&rect,ImageLockModeRead,PixelFormat32bppPARGB,&data)==Ok,"Read sprite alpha");
            bool found=false;for(int y=0;y<f.h&&!found;++y){auto* p=static_cast<BYTE*>(data.Scan0)+y*data.Stride;for(int x=0;x<f.w;++x)if(p[x*4+3]>32){top=y;found=true;break;}}
            bitmap.UnlockBits(&data);it=tops.emplace(key,top).first;
        }return AgentBounds(a,f).y+it->second*f.scale;
    }
    Bounds ContentBounds(const Agent& a,const Frame& f) {
        CropKey key{f.sheet,f.x,f.y,f.w,f.h};auto it=content.find(key);
        if(it==content.end()) {
            auto& bitmap=Crop(f);BitmapData data{};Gdiplus::Rect rect(0,0,f.w,f.h);Require(bitmap.LockBits(&rect,ImageLockModeRead,PixelFormat32bppPARGB,&data)==Ok,"Read sprite content");
            int l=f.w,t=f.h,r=0,b=0;for(int y=0;y<f.h;++y){auto* row=static_cast<BYTE*>(data.Scan0)+y*data.Stride;for(int x=0;x<f.w;++x)if(row[x*4+3]>32){l=std::min(l,x);t=std::min(t,y);r=std::max(r,x+1);b=std::max(b,y+1);}}
            bitmap.UnlockBits(&data);Bounds bounds=r>l?Bounds{double(l),double(t),double(r-l),double(b-t)}:Bounds{0,0,double(f.w),double(f.h)};it=content.emplace(key,bounds).first;
        }
        auto bounds=AgentBounds(a,f);return {bounds.x+it->second.x*f.scale,bounds.y+it->second.y*f.scale,it->second.w*f.scale,it->second.h*f.scale};
    }
    void Room(const Snapshot& s,bool transparent) {
        if(room&&roomKey==s.staticKey&&roomTransparent==transparent)return;
        room=std::make_unique<Bitmap>(s.w*16,s.h*16,PixelFormat32bppPARGB);Require(room->GetLastStatus()==Ok,"Room buffer allocation");Graphics g(room.get());Configure(g);g.Clear(Color(0,0,0,0));
        if(!transparent) {
            for(int y=0;y<s.h;++y)for(int x=0;x<s.w;++x){const auto& t=s.types.at(s.tiles[y*s.w+x]);if(t.kind=="void")continue;
                if(!t.sheet.empty()&&!t.animation.empty()){auto f=SelectFrame(s.sheets.at(t.sheet),t.animation,"any",0);Sprite(g,f,{double(x*16),double(y*16),16,16});}
                else {Rect(g,t.color,x*16,y*16,16,16);if(t.kind=="floor"){Rect(g,t.id==2?"#c9d1b8":"#d9c6a8",x*16,y*16+15,16,1);if((x+y)%3==0)Rect(g,"#d9c6a8",x*16,y*16,1,15);}}
            }
            if(!s.cozy)for(const auto& z:s.zones)if(z.name=="workspace"||z.name=="pantry") {Rect(g,"#eee5d2",z.x*16+24,24,std::min(72,z.w*16-24),11);std::string label=z.name;for(auto& c:label)c=static_cast<char>(toupper(static_cast<unsigned char>(c)));Text(g,label,z.x*16+26,25,7,"#59654c",true,z.w*16-28);}
        }
        if(std::none_of(s.furniture.begin(),s.furniture.end(),[](const Furniture& f){return f.kind=="door";})) {
            Rect(g,"#58675b",s.doorX*16-16,s.doorY*16-18,15,46);Rect(g,"#88a38e",s.doorX*16-14,s.doorY*16-16,11,42);Rect(g,"#f1d293",s.doorX*16-6,s.doorY*16+8,2,2);
        }roomKey=s.staticKey;roomTransparent=transparent;
    }
    void FallbackFurniture(Graphics& g,const Furniture& f) {
        int x=f.x*16,y=f.y*16,w=f.w*16,h=f.h*16;
        auto r=[&](const char* c,int dx,int dy,int rw,int rh){Rect(g,c,x+dx,y+dy,rw,rh);};
        if(f.kind=="desk") {
            r("#cbb898",2,27,w,7);r("#806b55",3,25,3,9);r("#806b55",w-6,25,3,9);r("#ba8e65",0,15,w,13);r("#e2b784",0,13,w,11);r("#f5d5a6",0,13,w,2);
            r("#414e51",11,1,25,15);r("#82b9a6",13,3,21,11);r("#414e51",22,16,3,3);r("#f8f0d9",13,20,19,3);r("#bca47e",14,21,1,1);r("#bca47e",18,21,1,1);r("#bca47e",22,21,1,1);r("#f7ecd5",3,17,5,5);r("#ae784e",4,17,3,1);r("#516768",13,37,21,6);r("#749390",15,34,17,5);r("#516768",22,42,3,5);
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
        double o=a.opacity;Rect(g,"#392b24",x-7,y-5,14,7,o);SolidBrush accent(ProviderColor(a,Alpha(o)));g.FillRectangle(&accent,static_cast<REAL>(x-6),static_cast<REAL>(y-4),12.0f,5.0f);
        if(!a.sub){Rect(g,"#392b24",x-5,y-10,10,6,o);Rect(g,"#f4c764",x-4,y-7,8,3,o);Rect(g,"#f4c764",x-4,y-9,2,2,o);Rect(g,"#f4c764",x-1,y-10,2,3,o);Rect(g,"#f4c764",x+2,y-9,2,2,o);}else Rect(g,"#fff2d2",x-2,y-3,4,3,o);
    }
    void Bubble(Graphics& g,const Snapshot& s,const Agent& a,const Frame& f,int ambient) {
        if(s.schema==4&&a.queued){Rect(g,"#172b30",(a.x+.5)*16+9,HeadY(a,f)-12,15,11,a.opacity);Text(g,"...",(a.x+.5)*16+11,HeadY(a,f)-13,8,"#f5edce");return;}
        if(s.sheets.contains("icons")) {
            std::string clip=!a.reaction.empty()?"bubble-reaction":a.action=="napping"?"bubble-napping":a.action=="waiting"?"bubble-waiting":a.action=="thinking"?"bubble-thinking":a.action=="reporting"?"bubble-done":a.action=="typing"?"bubble-working":"";
            if(clip.empty())return;auto b=SelectFrame(s.sheets.at("icons"),clip,"any",0);Sprite(g,b,{std::nearbyint((a.x+.5)*16+8),HeadY(a,f)-(s.schema==4?24:30),s.schema==4?24:b.w*b.scale,s.schema==4?24:b.h*b.scale},a.opacity);return;
        }
        int bubble=!a.reaction.empty()?5:a.action=="napping"?6:a.action=="chatting"&&!a.partner.empty()?7:a.action=="coffee"&&ambient%4==0?8:a.action=="waiting"?5:a.action=="typing"&&!a.toolKind.empty()?a.toolKind=="shell"?0:a.toolKind=="file"?1:a.toolKind=="search"?2:a.toolKind=="agent"?3:4:-1;
        if(bubble<0)return;Frame b{"fallback",6,bubble,bubble*16,144,16,24,8,24,1,0,0};Sprite(g,b,{(a.x+.5)*16-8,AgentBounds(a,f).y-17,16,24},a.opacity);
    }
    void Tag(Graphics& g,const Snapshot& s,const Agent& a,double x,double y,std::vector<Bounds>& plates) {
        auto name=Wide(a.displayName.empty()?a.name:a.displayName);if(name.size()>16){name.resize(15);if(name.back()>=0xD800&&name.back()<=0xDBFF)name.pop_back();name+=L"…";}
        double width=std::clamp(name.size()*4.5+18,32.0,92.0);Bounds plate{std::clamp(x-width/2,2.0,std::max(2.0,s.w*16-width-2)),y,width,15};
        auto overlaps=[&](const Bounds& p){return p.x<=plate.x+plate.w&&p.x+p.w>=plate.x&&p.y<=plate.y+plate.h&&p.y+p.h>=plate.y;};
        for(int attempt=0;attempt<8&&std::any_of(plates.begin(),plates.end(),overlaps);++attempt)plate.y-=16;plates.push_back(plate);
        Rect(g,a.sub?"#738b84":"#c99a52",plate.x-1,plate.y-1,plate.w+2,plate.h+2);Rect(g,"#172b30",plate.x,plate.y,plate.w,plate.h);
        if(s.sheets.contains("provider-icons")){auto f=SelectFrame(s.sheets.at("provider-icons"),a.provider,"any",0);Sprite(g,f,{plate.x+1,plate.y+1,12,12});}
        else {SolidBrush b(ProviderColor(a));g.FillRectangle(&b,static_cast<REAL>(plate.x+3),static_cast<REAL>(plate.y+5),6.0f,6.0f);}
        Text(g,Utf8Text(name),plate.x+15,plate.y+3,7,"#f5edce",false,plate.w-16);
    }
public:
    void Load(const Snapshot& s,const std::wstring& root) {
        if(!s.rawAssets.empty()&&loadedManifest==s.rawAssets&&loadedRoot==root)return;
        std::map<std::string,Image> added;uint64_t pixels=0;
        for(const auto& [id,sh]:s.sheets) {
            pixels+=uint64_t(sh.w)*sh.h;Require(pixels<=32*1024*1024,"Decoded pack exceeds 128 MiB");auto path=SheetPath(root,sh.file);
            auto old=images.find(id);if(old!=images.end()&&old->second.path==path&&old->second.sha==sh.sha)continue;
            auto bytes=ReadFileBytes(path,32*1024*1024,root);auto image=Decode(bytes,sh);added.emplace(id,Image{path,sh.sha,std::move(image)});
        }
        // Commit only after every referenced sheet has decoded and matched SHA256.
        bool changed=!added.empty()||images.size()!=s.sheets.size();
        for(auto& [id,image]:added)images.insert_or_assign(id,std::move(image));
        std::erase_if(images,[&](const auto& item){return !s.sheets.contains(item.first);});
        if(changed){crops.clear();tops.clear();content.clear();cropBytes=0;room.reset();}
        loadedManifest=s.rawAssets;loadedRoot=root;
    }
    std::unique_ptr<Bitmap> Render(const Snapshot& s,int width,int height,int zoom,bool transparent,bool offline,double time,const std::string& hovered={}) {
        Require(width>=1&&height>=1&&width<=4096&&height<=4096&&int64_t(width)*height<=8388608,"Viewport bounds");
        auto output=std::make_unique<Bitmap>(width,height,PixelFormat32bppPARGB);Require(output->GetLastStatus()==Ok,"Render buffer allocation");
        Graphics g(output.get());Configure(g);if(s.schema==4)g.SetTextRenderingHint(TextRenderingHintSingleBitPerPixelGridFit);g.Clear(transparent?Color(0,0,0,0):HexColor(s.schema==4?"#10282b":"#302d2b"));
        const auto camera=ViewCamera(s,width,height,zoom);g.TranslateTransform(static_cast<REAL>(camera.x),static_cast<REAL>(camera.y));g.ScaleTransform(static_cast<REAL>(camera.zoom),static_cast<REAL>(camera.zoom));Room(s,transparent);
        g.DrawImage(room.get(),0,0,s.w*16,s.h*16);
        std::map<std::string,const Agent*> byId;for(const auto& a:s.agents)byId.emplace(a.id,&a);
        Pen line(s.schema==4?HexColor("#5cbbb5",64):HexColor("#9bab9e"),s.schema==4?.6f:.7f);line.SetDashStyle(DashStyleDot);
        if(!offline)for(const auto& a:s.agents)if(a.visible&&byId.contains(a.parent)) {
            const auto& p=*byId.at(a.parent);if(!p.visible)continue;
            if(s.schema==4){if(a.state!="working"&&a.state!="reporting"&&hovered!=a.id&&hovered!=p.id)continue;double x=(a.x+.5)*16,y=(a.y+1)*16-5,dx=(p.x-a.x)*16,dy=(p.y-a.y)*16,distance=std::hypot(dx,dy);if(distance>64||distance<12)continue;double scale=std::min(1.0,40/distance);g.DrawLine(&line,static_cast<REAL>(x),static_cast<REAL>(y),static_cast<REAL>(x+dx*scale),static_cast<REAL>(y+dy*scale));}
            else if(a.action=="entering"||a.action=="walking"||a.action=="pickup"||a.action=="reporting")g.DrawLine(&line,static_cast<REAL>((a.x+.5)*16),static_cast<REAL>((a.y+.3)*16),static_cast<REAL>((p.dx+.5)*16),static_cast<REAL>((p.dy+.3)*16));
        }
        for(const auto& d:Scene(s)) {
            if(!d.agent){const auto& f=s.furniture[d.index];if(f.hasFrame)Sprite(g,f.frame,{f.x*16-f.frame.ax*f.frame.scale+f.frame.ox,f.y*16-f.frame.ay*f.frame.scale+f.frame.oy,f.frame.w*f.frame.scale,f.frame.h*f.frame.scale});else FallbackFurniture(g,f);}
            else {const auto& a=s.agents[d.index];auto f=DisplayFrame(s,a,offline,time);auto bounds=AgentBounds(a,f);if(offline&&static_cast<int>(std::fmod(time,2)))bounds.y+=1;Sprite(g,f,bounds,a.opacity);
                if(!offline&&s.schema==4&&a.workLocation=="overflow"&&(a.action=="typing"||a.action=="thinking"||a.action=="waiting")){double x=std::nearbyint((a.x+.5)*16-10),y=std::nearbyint((a.y+1)*16-10);if(s.sheets.contains("office-extras")){auto laptop=SelectFrame(s.sheets.at("office-extras"),"laptop","any",0);Sprite(g,laptop,{x,y,20,14},a.opacity);}else{Rect(g,"#19292d",x,y,19,11,a.opacity);Rect(g,"#8fd7c4",x+2,y+2,15,6,a.opacity);Rect(g,"#bec9be",x-1,y+10,21,3,a.opacity);}}
                if(!offline&&a.carrying&&(a.action=="walking"||a.action=="entering"))Rect(g,"#fff9e7",(a.x+.5)*16+(a.facing=="left"?-10:4),(a.y+1)*16-12,6,8,a.opacity);}
        }
        for(const auto& desk:s.furniture)if(byId.contains(desk.owner)) {
            const auto& a=*byId.at(desk.owner);int x=desk.x*16,y=desk.y*16;SolidBrush b(ProviderColor(a));g.FillRectangle(&b,x,y+desk.h*16-2,5,2);
            if(a.paper){Rect(g,"#fff9e7",x+desk.w*16-12,y+18,9,7);Rect(g,"#a19a7e",x+desk.w*16-11,y+20,6,1);}
            if(s.schema==3){double labelY=(a.dy+1)*16+5;Rect(g,"#f4e2bd",x-2,labelY,std::min(desk.w*16+8,static_cast<int>(Wide(a.name).size())*4+8),10);Text(g,a.name,x+1,labelY,7,"#392b24",false,desk.w*16+8);}
        }
        for(const auto& a:s.agents)if(a.visible){auto f=DisplayFrame(s,a,offline,time);if(s.schema==3)Identity(g,a,f,offline,time);if(!offline)Bubble(g,s,a,f,static_cast<int>(std::fmod(time*2,120)));}
        if(s.schema==4){std::vector<std::pair<double,const Agent*>> labels;for(const auto& a:s.agents)if(a.visible)labels.emplace_back(HeadY(a,DisplayFrame(s,a,offline,time)),&a);std::sort(labels.begin(),labels.end(),[](const auto& a,const auto& b){return a.first!=b.first?a.first<b.first:a.second->ordinal<b.second->ordinal;});std::vector<Bounds> plates;
            for(const auto& [head,a]:labels)Tag(g,s,*a,std::nearbyint((a->x+.5)*16),head-17,plates);
            for(const auto& desk:s.furniture)if(byId.contains(desk.owner)){const auto& a=*byId.at(desk.owner);bool besideDesk=std::abs(a.x-a.dx)+std::abs(a.y-a.dy)<=1.5;if(besideDesk&&(a.state=="working"||a.state=="thinking"||a.state=="waiting"))continue;Tag(g,s,a,desk.x*16+desk.w*8,desk.y*16+desk.h*16-13,plates);}
        }
        if(s.agents.empty()){Text(g,"A quiet moment.",30,132,10,"#394940",true);Text(g,"Your agents will walk in.",30,151,7,"#394940",false,95);}
        g.ResetTransform();if(offline){Rect(g,"#303d40",8,8,144,24);Rect(g,"#e6ad66",15,14,10,10);Rect(g,"#303d40",19,16,2,4);Rect(g,"#303d40",19,21,2,1);Text(g,"tracker offline",32,11,12,"#fff4df");}
        Require(g.GetLastStatus()==Ok,"GDI+ render failed");return output;
    }
    const Agent* Hit(const Snapshot& s,int width,int height,int zoom,double px,double py,bool offline,double time) {
        auto c=ViewCamera(s,width,height,zoom);double x=(px-c.x)/c.zoom,y=(py-c.y)/c.zoom;auto scene=Scene(s);
        for(auto it=scene.rbegin();it!=scene.rend();++it)if(it->agent){const auto& a=s.agents[it->index];auto f=DisplayFrame(s,a,offline,time);auto bounds=s.schema==4?ContentBounds(a,f):AgentBounds(a,f);if(offline&&static_cast<int>(std::fmod(time,2)))bounds.y+=1;if(a.opacity>0&&bounds.Contains(x,y))return &a;}return nullptr;
    }
};
std::string Tooltip(const Agent& a,double now) {
    auto task=a.task.empty()?"No task recorded":a.task;auto w=Wide(task);if(w.size()>150){w.resize(147);if(w.back()>=0xD800&&w.back()<=0xDBFF)w.pop_back();w+=L"...";int n=WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),nullptr,0,nullptr,nullptr);task.resize(n);WideCharToMultiByte(CP_UTF8,0,w.data(),static_cast<int>(w.size()),task.data(),n,nullptr,nullptr);}
    return a.provider+" · "+a.name+"\n"+(a.sub?"Sub-agent":"Supervisor")+(a.role.empty()?"":" · "+a.role)+"\nModel   "+a.model+"\nProject   "+a.project+"\nTask   "+task+"\nTool   "+a.tool+"\n"+a.state+" · "+std::to_string(static_cast<int64_t>(std::max(0.0,now-a.since)))+"s · "+a.action;
}
void SavePng(Bitmap& image,const std::wstring& path) {
    UINT count=0,size=0;Require(GetImageEncodersSize(&count,&size)==Ok,"PNG encoders");std::vector<BYTE> buffer(size);auto* encoders=reinterpret_cast<ImageCodecInfo*>(buffer.data());Require(GetImageEncoders(count,size,encoders)==Ok,"PNG encoder list");
    for(UINT i=0;i<count;++i)if(wcscmp(encoders[i].MimeType,L"image/png")==0){Require(image.Save(path.c_str(),&encoders[i].Clsid,nullptr)==Ok,"Save PNG");return;}throw Error("PNG encoder missing");
}
// ===== END SHARED GDI+ RENDERER =====

uint64_t VisualKey(const Snapshot& s,bool offline,double time) {
    uint64_t key=s.staticKey;auto add=[&](const auto& n){const auto* p=reinterpret_cast<const unsigned char*>(&n);for(size_t i=0;i<sizeof(n);++i){key^=p[i];key*=1099511628211ULL;}};
    auto str=[&](const std::string& v){add(Hash(v));};
    auto frame=[&](const Frame& f){str(f.sheet);add(f.x);add(f.y);add(f.w);add(f.h);add(f.ax);add(f.ay);add(f.scale);add(f.ox);add(f.oy);};
    add(offline);add(s.schema);for(const auto& f:s.furniture){add(f.hasFrame);if(f.hasFrame)frame(f.frame);}
    for(const auto& a:s.agents){str(a.id);str(a.name);str(a.displayName);str(a.workLocation);str(a.providerIcon);add(a.queued);str(a.provider);str(a.parent);add(a.x);add(a.y);add(a.dx);add(a.dy);add(a.opacity);add(a.visible);add(a.sub);add(a.paper);add(a.carrying);str(a.action);str(a.toolKind);str(a.facing);str(a.reaction);str(a.partner);frame(DisplayFrame(s,a,offline,time));}
    if(offline&&std::any_of(s.agents.begin(),s.agents.end(),[](const Agent& a){return a.visible;})){int tick=static_cast<int>(std::fmod(time,2));add(tick);}
    else if(!s.sheets.contains("icons")&&std::any_of(s.agents.begin(),s.agents.end(),[](const Agent& a){return a.visible&&a.action=="coffee";})){int tick=static_cast<int>(std::fmod(time*2,4));add(tick);}
    return key;
}
} // namespace vdc

#ifndef VIBEDOTCOM_TEST
// ===== BEGIN WINDHAWK WINDOW HOST (never compiled into the offscreen tests) =====
namespace {
using namespace vdc;
constexpr wchar_t kViewClass[]=L"Vibedotcom.Windhawk.Office";
constexpr wchar_t kHelperClass[]=L"Vibedotcom.Windhawk.Helper";
constexpr wchar_t kTipClass[]=L"Vibedotcom.Windhawk.Tooltip";
HINSTANCE g_module=nullptr;HANDLE g_stop=nullptr,g_thread=nullptr;
struct Settings {std::wstring mode,state,pack;int x=80,y=180,w=720,h=520,zoom=0,opacity=100,fps=30;bool through=false,tooltip=true,transparent=false;};
Settings g_settings; // Written before thread starts, immutable until it is joined.
struct Host {HWND parent=nullptr,icons=nullptr,wallpaper=nullptr,iconHost=nullptr;bool operator==(const Host&) const=default;};
bool InThisProcess(HWND w){DWORD pid=0;return w&&GetWindowThreadProcessId(w,&pid)&&pid==GetCurrentProcessId();}
BOOL CALLBACK ClassicHost(HWND top,LPARAM value) {
    if(!InThisProcess(top)||!FindWindowExW(top,nullptr,L"SHELLDLL_DefView",nullptr))return TRUE;
    // Search subsequent siblings, not an arbitrary first WorkerW in the process.
    for(HWND next=GetWindow(top,GW_HWNDNEXT);next;next=GetWindow(next,GW_HWNDNEXT)) {
        wchar_t name[64]{};GetClassNameW(next,name,64);
        if(wcscmp(name,L"WorkerW")==0&&InThisProcess(next)&&IsWindowVisible(next)&&!FindWindowExW(next,nullptr,L"SHELLDLL_DefView",nullptr)) {*reinterpret_cast<Host*>(value)={next,nullptr,next,top};return FALSE;}
    }return TRUE;
}
Host FindHost() {
    HWND progman=FindWindowW(L"Progman",nullptr);if(!InThisProcess(progman))return {};
    HWND icons=FindWindowExW(progman,nullptr,L"SHELLDLL_DefView",nullptr),worker=FindWindowExW(progman,nullptr,L"WorkerW",nullptr);
    if(InThisProcess(icons)&&InThisProcess(worker)&&(GetWindowLongPtrW(progman,GWL_EXSTYLE)&WS_EX_NOREDIRECTIONBITMAP))return {progman,icons,worker,progman};
    Host host;EnumWindows(ClassicHost,reinterpret_cast<LPARAM>(&host));return host;
}
struct Ui {
    HWND window=nullptr,helper=nullptr,tip=nullptr,requestedProgman=nullptr;
    Host host;Renderer renderer;Snapshot snapshot;bool hasState=false,dirty=true,running=true,tipShown=false;
    std::unique_ptr<Bitmap> buffer;uint64_t renderedKey=0;bool rendered=false;
    FILETIME acceptedTime{};DWORD acceptedSize=0;bool accepted=false;
    ULONGLONG nextRead=0,nextProbe=0,nextHover=0,lastRender=0,hoverSince=0,lastTip=0;int failures=0;
    std::string hovered;std::wstring tipText;HFONT tipFont=nullptr;UINT taskbarCreated=0;
    int width=720,height=520,zoom=0;std::wstring packRoot;
    void HideTip(){if(tipShown&&tip)ShowWindow(tip,SW_HIDE);tipShown=false;hovered.clear();}
};
LRESULT CALLBACK ViewProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE){ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));ui->window=w;}
    if(ui)switch(msg) {
        case WM_ERASEBKGND:return 1;
        case WM_PAINT: {
            PAINTSTRUCT paint{};HDC dc=BeginPaint(w,&paint);
            {Graphics g(dc);g.SetInterpolationMode(InterpolationModeNearestNeighbor);g.SetPixelOffsetMode(PixelOffsetModeHalf);g.Clear(Color(255,255,0,255));
                if(ui->buffer)g.DrawImage(ui->buffer.get(),0,0,ui->width,ui->height);
                else {SolidBrush b(HexColor("#302d2b"));g.FillRectangle(&b,0,0,ui->width,ui->height);SolidBrush icon(HexColor("#e6ad66"));g.FillRectangle(&icon,15,15,10,10);g.FillRectangle(&b,19,17,2,4);g.FillRectangle(&b,19,22,2,1);Font font(L"Segoe UI",14,FontStyleRegular,UnitPixel);SolidBrush text(HexColor("#fff4df"));g.DrawString(L"tracker offline — waiting for a valid snapshot",-1,&font,PointF(32,12),&text);}}
            EndPaint(w,&paint);return 0;
        }
        case WM_SIZE: {
            RECT r{};GetClientRect(w,&r);ui->width=std::clamp<int>(r.right,1,4096);ui->height=std::clamp<int>(r.bottom,1,2048);ui->dirty=true;ui->HideTip();return 0;
        }
        case WM_GETMINMAXINFO:{auto* m=reinterpret_cast<MINMAXINFO*>(lp);m->ptMinTrackSize={240,160};m->ptMaxTrackSize={4096,2048};return 0;}
        case WM_DPICHANGED: {
            if(!ui->host.parent){auto* r=reinterpret_cast<RECT*>(lp);SetWindowPos(w,nullptr,r->left,r->top,r->right-r->left,r->bottom-r->top,SWP_NOZORDER|SWP_NOACTIVATE);}
            ui->dirty=true;ui->nextProbe=0;ui->HideTip();return 0;
        }
        case WM_MOUSEWHEEL:ui->zoom=std::clamp((ui->zoom?ui->zoom:ui->hasState?ViewCamera(ui->snapshot,ui->width,ui->height,0).zoom:1)+(GET_WHEEL_DELTA_WPARAM(wp)>0?1:-1),1,6);ui->dirty=true;ui->HideTip();return 0;
        case WM_KEYDOWN:
            if(wp==VK_ADD||wp==VK_OEM_PLUS||wp==VK_SUBTRACT||wp==VK_OEM_MINUS){ui->zoom=std::clamp((ui->zoom?ui->zoom:1)+(wp==VK_ADD||wp==VK_OEM_PLUS?1:-1),1,6);ui->dirty=true;ui->HideTip();return 0;}break;
        case WM_MOUSEACTIVATE:if(g_settings.through||g_settings.mode==L"desktop-level")return MA_NOACTIVATE;break;
        case WM_NCHITTEST:if(g_settings.through)return HTTRANSPARENT;break;
        case WM_CLOSE:ui->HideTip();DestroyWindow(w);ui->running=false;return 0;
        case WM_NCDESTROY:if(ui->window==w)ui->window=nullptr;ui->HideTip();SetWindowLongPtrW(w,GWLP_USERDATA,0);break;
    }
    return DefWindowProcW(w,msg,wp,lp);
}
LRESULT CALLBACK HelperProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE){ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));}
    if(ui&&(msg==ui->taskbarCreated||msg==WM_DISPLAYCHANGE||msg==WM_SETTINGCHANGE||msg==WM_THEMECHANGED)){ui->nextProbe=0;ui->dirty=true;ui->HideTip();}
    if(msg==WM_NCDESTROY)SetWindowLongPtrW(w,GWLP_USERDATA,0);
    return DefWindowProcW(w,msg,wp,lp);
}
LRESULT CALLBACK TipProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    auto* ui=reinterpret_cast<Ui*>(GetWindowLongPtrW(w,GWLP_USERDATA));
    if(msg==WM_NCCREATE){ui=static_cast<Ui*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);SetWindowLongPtrW(w,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(ui));}
    if(msg==WM_MOUSEACTIVATE)return MA_NOACTIVATE;if(msg==WM_NCHITTEST)return HTTRANSPARENT;if(msg==WM_ERASEBKGND)return 1;
    if(msg==WM_PAINT&&ui){PAINTSTRUCT paint{};HDC dc=BeginPaint(w,&paint);RECT r{};GetClientRect(w,&r);HBRUSH b=CreateSolidBrush(RGB(48,61,64));FillRect(dc,&r,b);DeleteObject(b);
        HGDIOBJ old=SelectObject(dc,ui->tipFont);SetBkMode(dc,TRANSPARENT);SetTextColor(dc,RGB(255,244,223));InflateRect(&r,-12,-10);DrawTextW(dc,ui->tipText.c_str(),static_cast<int>(ui->tipText.size()),&r,DT_LEFT|DT_WORDBREAK|DT_NOPREFIX);SelectObject(dc,old);EndPaint(w,&paint);return 0;}
    return DefWindowProcW(w,msg,wp,lp);
}
void ProbeHost(Ui& ui) {
    HWND shell=GetShellWindow();if(!shell)shell=FindWindowW(L"Progman",nullptr);
    if(!shell)return;if(!InThisProcess(shell)){ui.running=false;return;}
    if(!ui.helper)ui.helper=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,kHelperClass,L"",WS_POPUP,0,0,0,0,nullptr,nullptr,g_module,&ui);
    Host host;
    if(g_settings.mode==L"desktop-level") {
        HWND progman=FindWindowW(L"Progman",nullptr);
        if(InThisProcess(progman)&&progman!=ui.requestedProgman){DWORD_PTR result=0;
            SendMessageTimeoutW(progman,0x052C,0xD,0,SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT,250,&result);
            SendMessageTimeoutW(progman,0x052C,0xD,1,SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT,250,&result);ui.requestedProgman=progman;}
        host=FindHost();
    }
    if(ui.window&&!(host==ui.host)){DestroyWindow(ui.window);ui.host={};}
    if(!ui.window) {
        auto context=host.parent?GetWindowDpiAwarenessContext(host.parent):DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2;
        auto previous=SetThreadDpiAwarenessContext(context);if(!previous){Wh_Log(L"Cannot set UI DPI context: %lu",GetLastError());return;}
        DWORD ex=WS_EX_LAYERED|WS_EX_TOOLWINDOW;
        if(g_settings.through)ex|=WS_EX_TRANSPARENT;
        if(g_settings.through||g_settings.mode==L"desktop-level")ex|=WS_EX_NOACTIVATE;
        if(host.parent)ex|=WS_EX_NOPARENTNOTIFY;
        POINT p{g_settings.x,g_settings.y};if(host.parent)MapWindowPoints(nullptr,host.parent,&p,1);
        DWORD style=host.parent?WS_CHILD:g_settings.mode==L"desktop-level"?WS_POPUP:WS_OVERLAPPEDWINDOW;
        HWND w=CreateWindowExW(ex,kViewClass,L"Vibedotcom",style,p.x,p.y,g_settings.w,g_settings.h,host.parent,nullptr,g_module,&ui);
        SetThreadDpiAwarenessContext(previous);
        if(!w)return;
        if(!SetLayeredWindowAttributes(w,RGB(255,0,255),static_cast<BYTE>(g_settings.opacity*255/100),LWA_COLORKEY|LWA_ALPHA)){DestroyWindow(w);return;}
        ui.host=host;ui.dirty=true;
        HWND order=host.parent?(host.icons?host.icons:HWND_TOP):g_settings.mode==L"topmost"?HWND_TOPMOST:g_settings.mode==L"desktop-level"?HWND_BOTTOM:HWND_NOTOPMOST;
        SetWindowPos(w,order,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
        Wh_Log(L"Office created; desktop embedded=%d, fallback=%d",host.parent!=nullptr,g_settings.mode==L"desktop-level"&&!host.parent);
    } else if(g_settings.mode==L"desktop-level") {
        // Repair sibling order after shell changes; never modify shell windows.
        HWND order=host.parent?(host.icons?host.icons:HWND_TOP):HWND_BOTTOM;
        if(!host.parent||(host.icons&&GetWindow(ui.window,GW_HWNDPREV)!=host.icons))SetWindowPos(ui.window,order,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
    }
}
void ReadState(Ui& ui,ULONGLONG tick) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if(GetFileAttributesExW(g_settings.state.c_str(),GetFileExInfoStandard,&data)&&ui.accepted&&data.nFileSizeHigh==0&&data.nFileSizeLow==ui.acceptedSize&&CompareFileTime(&data.ftLastWriteTime,&ui.acceptedTime)==0){ui.nextRead=tick+200;return;}
    try {
        auto bytes=ReadFileBytes(g_settings.state,4*1024*1024);auto candidate=ParseSnapshot(bytes,ui.hasState?&ui.snapshot:nullptr);
        ui.renderer.Load(candidate,ui.packRoot);ui.snapshot=std::move(candidate);ui.hasState=true;ui.failures=0;ui.accepted=false;
        // Record the stamp only if it still describes the version just read.
        WIN32_FILE_ATTRIBUTE_DATA after{};
        if(GetFileAttributesExW(g_settings.state.c_str(),GetFileExInfoStandard,&after)&&after.nFileSizeLow==bytes.size()&&data.nFileSizeLow==after.nFileSizeLow&&CompareFileTime(&data.ftLastWriteTime,&after.ftLastWriteTime)==0){ui.acceptedTime=after.ftLastWriteTime;ui.acceptedSize=after.nFileSizeLow;ui.accepted=true;}
        ui.nextRead=tick+200;
    } catch(const std::exception& e) {
        ++ui.failures;ui.nextRead=tick+(ui.failures==1?40:ui.failures==2?100:200);
        if(ui.failures==1||ui.failures==50)Wh_Log(L"State/pack unavailable; retaining last valid scene: %s",Wide(e.what()).c_str());
    }
}
void Hover(Ui& ui,ULONGLONG tick,double now) {
    if(!g_settings.tooltip||!ui.hasState||!ui.window||!IsWindowVisible(ui.window)||IsIconic(ui.window)){ui.HideTip();return;}
    POINT cursor{};if(!GetCursorPos(&cursor)){ui.HideTip();return;}
    POINT local=cursor;ScreenToClient(ui.window,&local);if(local.x<0||local.y<0||local.x>=ui.width||local.y>=ui.height){ui.HideTip();return;}
    HWND under=WindowFromPoint(cursor);bool visible=under==ui.window||IsChild(ui.window,under);
    if(g_settings.through&&!ui.host.parent) {
        // WindowFromPoint may pass through a layered transparent window. Compare
        // its real z-order with the underlying hit window instead of losing hover.
        HWND underRoot=GetAncestor(under,GA_ROOT);
        for(HWND top=GetTopWindow(nullptr);top;top=GetWindow(top,GW_HWNDNEXT)){if(top==ui.window){visible=true;break;}if(top==underRoot&&top!=ui.tip)break;}
    }
    if(g_settings.mode==L"desktop-level") {auto root=GetAncestor(under,GA_ROOT);visible|=root==ui.host.parent||root==ui.host.wallpaper||root==ui.host.iconHost||root==FindWindowW(L"Progman",nullptr);}
    HWND defView=ui.host.icons?ui.host.icons:ui.host.iconHost?FindWindowExW(ui.host.iconHost,nullptr,L"SHELLDLL_DefView",nullptr):nullptr;
    if(visible&&defView) {
        HWND list=FindWindowExW(defView,nullptr,L"SysListView32",nullptr);
        if(list&&(under==list||IsChild(list,under))){LVHITTESTINFO hit{};hit.pt=cursor;ScreenToClient(list,&hit.pt);DWORD_PTR result=0;
            if(!SendMessageTimeoutW(list,LVM_HITTEST,0,reinterpret_cast<LPARAM>(&hit),SMTO_ABORTIFHUNG|SMTO_ERRORONEXIT,40,&result)||(hit.flags&LVHT_ONITEM))visible=false;}
    }
    if(!visible){ui.HideTip();return;}
    auto* a=ui.renderer.Hit(ui.snapshot,ui.width,ui.height,ui.zoom,local.x,local.y,Offline(ui.snapshot,now),now);
    if(!a){ui.HideTip();return;}if(ui.hovered!=a->id){ui.HideTip();ui.hovered=a->id;ui.hoverSince=tick;}
    if(tick-ui.hoverSince<220)return;if(ui.tipShown&&tick-ui.lastTip<200)return;
    if(!ui.tip)ui.tip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_LAYERED|WS_EX_TRANSPARENT,kTipClass,L"",WS_POPUP,0,0,0,0,ui.helper,nullptr,g_module,&ui);
    if(!ui.tip)return;SetLayeredWindowAttributes(ui.tip,0,255,LWA_ALPHA);ui.tipText=Wide(Tooltip(*a,now));
    HDC dc=GetDC(ui.tip);HGDIOBJ old=SelectObject(dc,ui.tipFont);RECT measure{0,0,340,0};DrawTextW(dc,ui.tipText.c_str(),static_cast<int>(ui.tipText.size()),&measure,DT_CALCRECT|DT_WORDBREAK|DT_NOPREFIX);SelectObject(dc,old);ReleaseDC(ui.tip,dc);
    int w=364,h=std::min<int>(440,measure.bottom+20);MONITORINFO monitor{};monitor.cbSize=sizeof(monitor);GetMonitorInfoW(MonitorFromPoint(cursor,MONITOR_DEFAULTTONEAREST),&monitor);
    int x=std::clamp<LONG>(cursor.x+18,monitor.rcWork.left,std::max<LONG>(monitor.rcWork.left,monitor.rcWork.right-w)),y=std::clamp<LONG>(cursor.y+22,monitor.rcWork.top,std::max<LONG>(monitor.rcWork.top,monitor.rcWork.bottom-h));
    SetWindowPos(ui.tip,HWND_TOPMOST,x,y,w,h,SWP_NOACTIVATE|SWP_SHOWWINDOW);InvalidateRect(ui.tip,nullptr,FALSE);ui.tipShown=true;ui.lastTip=tick;
}
DWORD WINAPI UiThread(void*) {
    auto previousDpi=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    ULONG_PTR token=0;GdiplusStartupInput startup;if(GdiplusStartup(&token,&startup,nullptr)!=Ok)return 1;
    bool viewRegistered=false,helperRegistered=false,tipRegistered=false;
    { Ui ui; // All C++ graphics objects are destroyed before GdiplusShutdown.
      try {
        ui.zoom=g_settings.zoom;ui.packRoot=g_settings.pack.empty()?Parent(g_settings.state):g_settings.pack;ui.taskbarCreated=RegisterWindowMessageW(L"TaskbarCreated");
        auto registerClass=[&](const wchar_t* name,WNDPROC proc){WNDCLASSW c{};c.hInstance=g_module;c.lpszClassName=name;c.lpfnWndProc=proc;c.hCursor=LoadCursorW(nullptr,IDC_ARROW);return RegisterClassW(&c)!=0;};
        viewRegistered=registerClass(kViewClass,ViewProc);helperRegistered=registerClass(kHelperClass,HelperProc);tipRegistered=registerClass(kTipClass,TipProc);Require(viewRegistered&&helperRegistered&&tipRegistered,"Window class registration failed");
        ui.tipFont=CreateFontW(-13,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");Require(ui.tipFont!=nullptr,"Tooltip font allocation");
        while(ui.running&&WaitForSingleObject(g_stop,0)==WAIT_TIMEOUT) {
            auto tick=GetTickCount64();double now=Now();
            if(tick>=ui.nextProbe){ui.nextProbe=tick+2000;ProbeHost(ui);}
            if(ui.window&&tick>=ui.nextRead)ReadState(ui,tick);
            if(ui.window&&ui.hasState&&!IsIconic(ui.window)&&IsWindowVisible(ui.window)&&tick-ui.lastRender>=static_cast<ULONGLONG>((1000+g_settings.fps-1)/g_settings.fps)) {
                bool offline=Offline(ui.snapshot,now);double visualTime=now;
                auto key=VisualKey(ui.snapshot,offline,visualTime);if(ui.snapshot.schema==4)key^=Hash(ui.hovered);
                if(ui.dirty||!ui.rendered||key!=ui.renderedKey){ui.buffer=ui.renderer.Render(ui.snapshot,ui.width,ui.height,ui.zoom,g_settings.transparent,offline,visualTime,ui.hovered);ui.renderedKey=key;ui.rendered=true;ui.dirty=false;ui.lastRender=tick;InvalidateRect(ui.window,nullptr,FALSE);}
            }
            if(g_settings.tooltip&&tick>=ui.nextHover){ui.nextHover=tick+100;Hover(ui,tick,now);}
            // No unconditional animation timer: 5Hz file stamps, 10Hz optional
            // hover, paints only on visual changes, and stop-aware message waits.
            auto due=std::min(ui.nextProbe,ui.window?ui.nextRead:ui.nextProbe);
            if(g_settings.tooltip&&ui.window)due=std::min(due,ui.nextHover);
            if(ui.hasState&&Offline(ui.snapshot,now))due=std::min(due,tick+100);
            DWORD delay=static_cast<DWORD>(due>tick?std::min<ULONGLONG>(due-tick,2000):1);
            DWORD result=MsgWaitForMultipleObjectsEx(1,&g_stop,delay,QS_ALLINPUT,MWMO_INPUTAVAILABLE);if(result==WAIT_OBJECT_0||result==WAIT_FAILED)break;
            MSG msg;while(WaitForSingleObject(g_stop,0)==WAIT_TIMEOUT&&PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){if(msg.message==WM_QUIT){ui.running=false;break;}TranslateMessage(&msg);DispatchMessageW(&msg);}
        }
      } catch(const std::exception& e){Wh_Log(L"Office UI stopped: %s",Wide(e.what()).c_str());}catch(...){Wh_Log(L"Office UI stopped after an unexpected exception");}
      ui.HideTip();if(IsWindow(ui.tip))DestroyWindow(ui.tip);if(IsWindow(ui.window))DestroyWindow(ui.window);if(IsWindow(ui.helper))DestroyWindow(ui.helper);if(ui.tipFont)DeleteObject(ui.tipFont);
    }
    if(tipRegistered)UnregisterClassW(kTipClass,g_module);if(helperRegistered)UnregisterClassW(kHelperClass,g_module);if(viewRegistered)UnregisterClassW(kViewClass,g_module);
    GdiplusShutdown(token);if(previousDpi)SetThreadDpiAwarenessContext(previousDpi);Wh_Log(L"Office thread, windows and graphics released");return 0;
}
std::wstring StringSetting(const wchar_t* name){PCWSTR s=Wh_GetStringSetting(name);std::wstring value=s?s:L"";if(s)Wh_FreeStringSetting(s);return value;}
} // namespace
BOOL Wh_ModInit() {
    HWND shell=GetShellWindow();if(!shell)shell=FindWindowW(L"Progman",nullptr);if(shell&&!InThisProcess(shell))return FALSE;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(UiThread),&g_module))return FALSE;
    try {
        g_settings.mode=StringSetting(L"mode");if(g_settings.mode!=L"normal"&&g_settings.mode!=L"topmost"&&g_settings.mode!=L"desktop-level")g_settings.mode=L"normal";
        g_settings.state=LocalPath(StringSetting(L"statePath"));auto pack=StringSetting(L"packPath");g_settings.pack=pack.empty()?L"":LocalPath(pack);
        g_settings.x=std::clamp(Wh_GetIntSetting(L"x"),-32000,32000);g_settings.y=std::clamp(Wh_GetIntSetting(L"y"),-32000,32000);g_settings.w=std::clamp(Wh_GetIntSetting(L"width"),240,4096);g_settings.h=std::clamp(Wh_GetIntSetting(L"height"),160,2048);
        g_settings.zoom=std::clamp(Wh_GetIntSetting(L"zoom"),0,6);g_settings.opacity=std::clamp(Wh_GetIntSetting(L"opacity"),10,100);g_settings.fps=std::clamp(Wh_GetIntSetting(L"fpsCap"),1,60);
        g_settings.through=Wh_GetIntSetting(L"clickThrough")!=0;g_settings.tooltip=Wh_GetIntSetting(L"showTooltip")!=0;g_settings.transparent=Wh_GetIntSetting(L"transparentBackground")!=0;
        g_stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);return g_stop!=nullptr;
    }catch(const std::exception& e){Wh_Log(L"Invalid viewer settings: %s",Wide(e.what()).c_str());return FALSE;}
}
void Wh_ModAfterInit(){g_thread=CreateThread(nullptr,0,UiThread,nullptr,0,nullptr);if(!g_thread)Wh_Log(L"Office thread creation failed: %lu",GetLastError());}
BOOL Wh_ModSettingsChanged(BOOL* reload){*reload=TRUE;return TRUE;}
void Wh_ModUninit(){if(g_stop)SetEvent(g_stop);if(g_thread){WaitForSingleObject(g_thread,INFINITE);CloseHandle(g_thread);g_thread=nullptr;}if(g_stop){CloseHandle(g_stop);g_stop=nullptr;}g_settings={};}
// ===== END WINDHAWK WINDOW HOST =====
#endif
