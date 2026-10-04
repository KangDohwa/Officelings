# 공개 전 보안 검토

## 위협 모델

- **보호 자산:** 로그와 `state.json`의 프롬프트·대화 제목·프로젝트 경로, 사용자 프로필의 파일과 설정, Explorer 프로세스의 가용성·사용자 권한, 빌드·로그온 실행 경로의 무결성.
- **공격자:** 낯선 사람이 공유한 악성 `layout.json`·`assets.json`·`--asset-dir` 팩/아트 매니페스트, 손상되거나 악의적으로 수정된 세션 로그·SQLite DB, 공유 폴더에 쓰기 권한이 있는 다른 로컬 사용자, 변조된 다운로드, 특수문자 또는 reparse point를 포함한 프로젝트 경로.
- **신뢰 경계:** 외부 JSON/PNG/DB → 관리 코드 파서·WPF; 로그/loopback 응답 → 추적 상태; 출력 경로 → 사용자 파일 시스템; `state.json`/PNG/설정 → Explorer 내부 네이티브 코드; 매니페스트 → 아트 도구의 읽기·쓰기; 다운로드 → 이미지 디코더; 실행 파일·스크립트·의존성 → 빌드와 로그온 작업.
- **전제:** 앱은 일반 사용자 권한으로 실행한다. 같은 사용자 권한을 이미 완전히 장악한 공격자가 임의 EXE를 실행하는 것은 새 권한 상승으로 세지 않는다. 그러나 데이터 파일만 전달해 의도하지 않은 파일 접근·덮어쓰기·과도한 작업을 유발하는 것은 범위 안이다. 관리자 권한과 OS/이미지 디코더 자체의 미확인 취약점은 추정하지 않는다.

## 검토 기준과 요약

검토일: 2026-10-04. 소스 기준은 **`280e7a61b0d3c049de75967892debac15627fbcc`**, 그 커밋의 조상은 154개다. 요청한 `git worktree add --detach %TEMP%\vd-sec HEAD`로 분리한 뒤 이 worktree의 소스만 분석했다. 이후 다른 작업자가 만든 커밋과 작업 트리 변경은 제품 취약점 판단에 섞지 않았다.

범위: `src/Vibedotcom.Core`, `src/Vibedotcom.App`, `src/Vibedotcom.Tracker`, `windhawk/vibedotcom.wh.cpp`, `tools/*.ps1`, `tools/*.py`, `tools/release/*`, `.gitignore`, 에셋 매니페스트. `docs`, fixtures, 아트 QA 기록과 Git 이력은 공개 시 민감정보·브랜드·바이너리 점검 목적으로 추가 확인했다.

| Critical | High | Medium | Low | Info | 합계 |
|---:|---:|---:|---:|---:|---:|
| 0 | 2 | 12 | 3 | 3 | 20 |

조건부 발견도 포함한 수다. 각 항목에 전제와 재현 수준을 명시했다. Critical, 검증된 권한 상승, 검증된 네이티브 메모리 손상은 발견하지 못했다. **현재 커밋은 낯선 사람의 데이터 팩을 안전하게 처리한다고 보증할 상태가 아니다.** 우선 파일 경계와 Explorer 내부 작업량 제한을 보완해야 한다.

저장소에 쓰는 결과물은 이 문서 하나다. 테스트 소스·빌드·표식 파일은 별도 `%TEMP%`에만 생성했다. 실제 사용자 로그/DB를 실행 입력으로 사용하지 않았으며 실제 `~/.codex`, `~/.claude`, `~/.gemini`에 쓰지 않았다. 다운로드, 작업 등록, Explorer 주입, 실제 앱 실행, 패키지 설치, 커밋·업로드·이력 재작성은 수행하지 않았다. 네트워크 사용은 공개 공식 문서·보안 권고 조회뿐이다.

## 발견 사항

### S01 — High: seated 매니페스트의 파일명이 출력 경계를 벗어난다

**위치:** `tools/seated.py:239`, `:269`, `:358`, `:369`; 쓰기 구현 `:77`.

**공격:** 사용자가 공유받은 원본 아트 매니페스트를 seated 도구로 처리한다. 공격자가 기존 `blink` 항목의 `file`에 `../`를 넣으면 그 이름을 `OUT / cid / name`에 다시 사용한다. 입력 폴더와 출력 폴더의 깊이가 달라 입력에서 읽은 바이트를 다른 위치에 게시할 수 있다. 빌드 스크립트 등 사용자가 쓸 수 있는 파일을 덮어쓰는 후속 공격이 가능하다. 일반 런타임 팩 로드만으로 이 도구가 자동 실행되지는 않는다.

**근거/재현:** 합성 `workspace/assets/art/roster/cat-hood/locked`와 `workspace/assets/art/seated/cat-hood`를 만들었다. `../../../../../marker.txt`의 입력은 합성 workspace 내부였지만 실제 `publish()`는 workspace 부모의 `marker.txt`에 같은 바이트를 썼다. `relative_to(ROOT)`로 출처를 기록하는 코드도 lexical `..`가 포함된 경로의 실제 탈출을 차단하지 않는다. 전체 이미지 변환은 실행하지 않았고 파일명 전달식과 실제 게시 함수를 검증했다.

**최소 수정:** 매니페스트의 파일명은 읽기 전에 허용된 상대 경로로 검증하고, 출력 이름은 basename 또는 도구가 생성한 고정 이름으로 정한다. 입력과 출력의 최종 경로를 각각 허용 루트 안으로 제한하고 ancestor reparse point도 거부한다.

### S02 — High, 조건부: Explorer의 로컬 파일 제한을 reparse point로 우회할 수 있다

**위치:** `windhawk/vibedotcom.wh.cpp:229`, `:254`, `:257`, `:260`, `:263`, `:1121`, `:1126`.

**공격:** 로컬처럼 보이는 `statePath` 또는 pack root의 ancestor가 원격 공유로 향하는 링크다. `GetDriveTypeW`는 표시 경로의 드라이브만 검사한다. `CreateFileW`는 링크를 따라가며, state 읽기는 최종 경로를 확인하지 않는다. pack root 전체가 같은 원격 트리로 해석되면 root와 sheet의 최종 경로 prefix 비교도 통과할 수 있다. 하위 sheet만 원격으로 향해 나중에 거부되어도 원격 open은 이미 시도된다.

**영향/근거:** Explorer 사용자 문맥에서 의도하지 않은 SMB I/O가 발생한다. NTLM이 허용된 환경에서는 인증 요청 노출·relay 위험이 조건부로 생긴다. 파일 열기와 `GetFileAttributesExW`는 `ReadFile` 이후의 5초 wait로 제한되지 않아 원격 지연은 worker와 종료 대기에도 영향을 줄 수 있다. 링크를 따르는 open 동작과 SMB 인증 정책은 [CreateFileW 문서](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew), [SMB NTLM 차단 문서](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-ntlm-blocking)를 확인했다. 실제 SMB 연결·인증 유출은 테스트하지 않았다.

**최소 수정:** state, pack root, sheet의 ancestor와 최종 항목에 대해 reparse를 거부하거나 검증된 로컬 directory handle을 기준으로 연다. 최종 파일에만 `FILE_FLAG_OPEN_REPARSE_POINT`를 붙이면 ancestor 우회가 남는다. 원격 open 자체를 예방하고, I/O가 Explorer 종료를 무기한 막지 않게 한다.

### S03 — Medium: 관리 코드의 팩·브랜드 경로 검사는 링크 탈출을 막지 못한다

**위치:** `src/Vibedotcom.Core/Assets.cs:39`, `:71`, `:104`; `src/Vibedotcom.App/BrandIcons.cs:31`; `src/Vibedotcom.Core/LayoutLoader.cs:9`.

**공격:** 팩의 `linked/sheet.png`가 팩 외부의 PNG를 가리키는 junction 아래에 있다. `Path.GetFullPath`와 prefix 검사는 통과하고 실제 파일 읽기는 외부로 나간다. 팩 root 자체와 `assets.json`, `manifest.json`, 브랜드의 고정 파일명에도 최종 대상 검사가 없다. 읽은 이미지는 tracker export로 복제될 수 있다. 팩만으로 임의 텍스트를 state에 공개하는 공격은 입증하지 않았다; sheet는 PNG 검사를 받아야 한다.

**재현:** 합성 팩에 외부 합성 PNG를 향하는 junction을 만들었다. 실제 `AssetPack` 결과는 `custom=True`, `warnings=0`이었다. 동일 입력의 lexical `../outside/sheet.png`는 거부됐다. 이는 일반 `..` 검사는 작동하지만 filesystem identity 경계는 없는 경우다. 브랜드·layout 링크 경로는 정적 검토이며 `--layout`로 사용자가 명시한 일반 외부 파일을 읽는 행위 자체는 취약점으로 세지 않았다.

**최소 수정:** 팩 루트·매니페스트·모든 ancestor와 sheet/브랜드 파일의 reparse 정책을 통일한다. 파일을 연 handle의 최종 대상이 검증된 로컬 루트에 속하는지 확인한 뒤 읽는다. 검사와 사용 사이의 교체도 막을 수 있도록 루트 쓰기 권한을 제한한다.

### S04 — Medium: `--output`의 보호 디렉터리 차단을 junction으로 우회한다

**위치:** `src/Vibedotcom.Core/Models.cs:93`, `:100`, `:114`; `src/Vibedotcom.Core/Assets.cs:262`; `src/Vibedotcom.Tracker/Program.cs:37`, `:42`, `:67`; `src/Vibedotcom.Core/OfficeEditor.cs:421`.

**공격:** `--output`이 정상 폴더 이름이지만 실제 대상은 보호하려는 provider 디렉터리다. root 또는 `sheets` 같은 하위 디렉터리를 공격자가 교체할 수도 있다. 경로 문자열만 검사하므로 atlas/state/manifest 등의 생성·교체가 의도하지 않은 대상에서 이루어진다. 이 문제는 모든 임의 파일 이름을 쓰는 원격 primitive가 아니라 프로그램이 게시하는 고정/해시 파일과 편집기 저장 경로에 적용된다.

**재현:** 실제 프로필 대신 임시 `fakehome/.codex`와 그곳으로 향하는 `output-alias`를 만들었다. `StateJson.OutputPath(alias, fakeHome)`를 통과한 뒤 실제 `WriteAtomic()`의 합성 표식이 보호 폴더에 생겼다. 직접 지정한 `fakehome/.codex`는 거부됐다.

**최소 수정:** 출력 루트와 하위 쓰기 대상의 최종 경계를 검사하고 reparse ancestor를 거부한다. 보호 경로 비교는 표시 경로뿐 아니라 실제 대상에도 적용한다. 검증된 사용자 전용 폴더의 ACL을 유지한다.

### S05 — Medium: 팩 전체의 메모리·디코딩 작업량 상한이 없다

**위치:** `src/Vibedotcom.Core/Assets.cs:62`, `:71`, `:76`, `:118`, `:144`; `src/Vibedotcom.App/OfficeView.cs:63`.

**공격:** sheet당 제한 안에 있는 PNG 128개를 넣는다. 파일당 32 MiB, 이미지당 16,777,216 pixels는 있지만 모든 compressed bytes와 decoded pixels의 합계는 제한하지 않는다. 수 GiB의 보관/디코딩과 장시간 해시·압축 해제 작업을 유발할 수 있다. WPF는 보관한 이미지 각각을 디코딩한다.

**재현/계산:** 같은 유효 PNG를 가리키는 sheet ID 128개를 실제 `AssetPack`이 수락했고 fallback을 포함해 약 8.78 MB를 별도 byte 배열로 보관했다. 최대 파일 크기와 개수를 곱한 이론적 compressed 보관량은 4 GiB다. 최대치로 OOM을 일으키는 테스트는 하지 않았다. Explorer 모드에는 별도의 총 pixel 제한이 있으므로 이 발견은 관리 코드 로더/WPF 쪽이다.

**최소 수정:** 읽기 전 팩 전체 byte budget, PNG 검증 후 전체 pixel budget을 누적해 초과하면 fallback으로 전환한다. 동일 파일/해시의 중복 데이터는 필요하면 재사용하되 재사용만으로 budget 검사를 대신하지 않는다.

### S06 — Medium: 제한된 JSONL 한 파일로 에이전트 수를 무제한 늘릴 수 있다

**위치:** `src/Vibedotcom.Core/AgentTracker.cs:161`, `:177`, `:180`, `:185`; `src/Vibedotcom.Core/LogParser.cs:33`; `src/Vibedotcom.Tracker/Program.cs:59`, `:67`.

**공격:** 최근 시각의 Codex `session_meta`를 서로 다른 ID로 반복한다. 각 행은 작은 정상 JSON이지만 `agents`와 `source.Cached`에 새 항목이 계속 추가된다. 단일 파일의 읽기 byte 상한과 행 상한은 총 에이전트 수·메타데이터 문자열·파일 수의 상한이 아니다. UI 계산과 state 직렬화도 커지고 Explorer의 2,048-agent/4 MiB 계약과 불일치한다.

**재현:** 합성 `--home` 아래의 377,030-byte 로그 한 개를 실제 `AgentTracker.Poll()`에 넣어 에이전트 **3,000개**를 얻었다. 마지막 합성 프롬프트와 합성 cwd가 snapshot에 저장되는 것도 확인했다. 실제 사용자 로그는 열지 않았다.

**최소 수정:** 파일당·전체 에이전트 수, source 수, ID/이름/cwd/model 길이와 poll당 처리량을 제한한다. source가 다른 새 session ID를 생성할 때 기존 identity 정책을 명확히 하고, 게시할 state에도 크기 상한을 둔다.

### S07 — Medium: SQLite 복사와 조회는 hostile DB의 증폭을 제한하지 않는다

**위치:** `src/Vibedotcom.Core/Providers/SqliteSnapshot.cs:29`, `:34`, `:47`, `:48`, `:59`, `:62`, `:65`; `src/Vibedotcom.Core/Providers/AntigravityProvider.cs:158`, `:183`.

**공격:** DB/WAL/SHM/journal을 매우 크게 만들거나, `conversation_summaries`를 연산이 많은 VIEW로 만든다. `SQLITE_OPEN_READONLY`, 안정성 stamp, `PRAGMA quick_check`, `user_version` 검사도 VIEW의 연산량·반환 크기·복사량을 제한하지 않는다. 요약 SELECT는 row 제한이 없으며 column text 전체를 관리 문자열로 복사한다. step 쿼리의 최종 `LIMIT 24`도 앞선 scan/sort/VIEW 비용을 제한하지 않는다.

**재현:** 합성 4,096-byte DB에 recursive CTE와 `hex(zeroblob(...))` VIEW를 만들었다. 실제 `SqliteSnapshot`의 quick_check/open을 통과했고 실제 요약 SELECT는 256행, **16,777,216 문자열 문자**를 반환했다. 더 큰 반복·무한 쿼리·disk exhaustion은 실행하지 않았다.

**최소 수정:** 복사할 DB와 sidecar의 합계 크기를 제한한다. 예상 table/schema를 확인하고 필요한 행·column 길이만 반환한다. `sqlite3_limit`, progress handler 또는 interrupt 기반 deadline으로 prepare/quick_check/step 전체를 제한한다. `trusted_schema=OFF`, defensive 설정은 추가 방어로 고려하되 CPU budget을 대신하지 않는다. [SQLite의 공식 보안 지침](https://www.sqlite.org/security.html), [연결별 제한](https://www.sqlite.org/limits.html)에 해당 방어가 설명되어 있다.

### S08 — Medium, 조건부: Antigravity loopback 응답을 과도하게 신뢰한다

**위치:** `src/Vibedotcom.Core/Providers/AntigravityRuntime.cs:27`, `:38`, `:42`, `:52`, `:54`, `:70`; `src/Vibedotcom.Core/Providers/AntigravityProvider.cs:55`, `:57`, `:61`.

**공격:** 종료된 서버의 재사용된 포트에 다른 로컬 사용자가 서버를 띄우거나, 언어 서버/발견 로그가 변조된다. 프로세스 이름의 존재와 응답의 `productName`은 endpoint 소유자 검증이 아니다. 본문을 문자열로 전부 읽고 map 전체를 clone한다. 응답 key를 검증 없이 DB/brain 경로에 결합하므로 rooted path 또는 `..`로 provider 폴더 밖의 DB·전사 파일 조회까지 유도할 수 있다.

**근거/검증:** 응답 크기·map 수·ID 경로 문법 검사가 없다. 실제 코드와 동일한 `Path.Combine(root,"conversations",id+".db")` 식에 합성 `..` ID를 넣어 root 외부로 정규화되는 것을 확인했다. 실제 local server나 네트워크 요청은 만들지 않았다. CSRF는 fake endpoint가 자기 token을 주는 경우 그 서버의 신뢰성을 보장하지 않는다. 다만 proxy·redirect가 비활성화되어 있고 응답/token은 diagnostics에 기록하지 않는다.

**최소 수정:** bounded streaming response와 map/문자열 상한을 적용한다. conversation ID는 예상 UUID/허용 문법으로 검증하고 경로 separator·rooted form을 거부한다. endpoint가 해당 사용자의 예상 서버인지 확인할 수 없으면 저장 파일 fallback을 선택한다.

### S09 — Medium: Explorer 이름 줄임 처리가 이차 작업을 유발한다

**위치:** `windhawk/vibedotcom.wh.cpp:405`, `:568`, `:579`, `:600`, `:695`, `:697`.

**공격:** 보이는 agent의 `name`에 최대 허용 길이인 ASCII 65,536자를 넣고 `displayName`을 생략한다. 파서는 `displayName`을 name으로 채우므로, 빈 displayName에만 적용되는 16자 제한을 건너뛴다. `Text`는 한 글자씩 제거하면서 전체 문자열을 복사하고 DirectWrite layout을 반복한다.

**근거:** 한 이름에서 약 65,000개의 감소하는 prefix 측정과 이차 규모의 문자열 처리가 가능하다. native JSON 크기·문자열 상한을 충족하는 입력이다. 작은 offscreen/알고리즘 검증의 결과는 아래 검증 기록에 구분하며, 실제 Explorer를 정지시키는 테스트는 하지 않았다. read 취소 경로가 있어 종료 불가능한 무한 루프로 단정하지 않는다.

**최소 수정:** default로 채운 값까지 포함해 표시 이름을 측정 전에 제한한다. ellipsis prefix는 이진 검색 또는 renderer의 bounded trimming으로 결정한다. 이름 폭 cache에도 총 문자량을 제한한다.

### S10 — Medium: 큰 sprite crop을 교대하면 캐시 제한이 작업량 증폭을 막지 못한다

**위치:** `windhawk/vibedotcom.wh.cpp:326`, `:327`, `:518`, `:612`, `:613`, `:746`.

**공격:** 허용된 `8192×2048` PNG에서 `8191×2048` crop 두 개를 x=0과 x=1로 교대한다. `ParseFrame`은 sprite를 cell 크기 대신 sheet 전체까지 허용한다. crop 하나는 67,100,672 bytes로 개별 캐시에 들어가지만 두 개는 64 MiB를 넘는다. 다음 crop에서 cache 전체를 비우고 다시 복사한다.

**근거/계산:** 2,048개의 교대 sprite는 그리기 단계만으로 약 127.98 GiB의 누적 crop 생성·복사량을 만들 수 있다. 이는 최대 동시 메모리 사용량이나 실측 시간이 아니다. 팩 총 pixel 제한 안에서 가능한 반복 작업이다. 실제 최악 크기 2,048개를 렌더링하지 않았다.

**최소 수정:** frame의 width/height를 선언된 cell과 일치시키거나 별도 sprite 면적 상한을 둔다. render당 누적 crop 생성량·새 crop 수를 제한하고 초과 snapshot을 거부한다. 캐시 메모리 상한만으로 처리량을 보장하지 않는다.

### S11 — Medium: 팩 빌더의 하위 junction은 외부 쓰기·삭제를 유도한다

**위치:** `tools/build_pack.py:424`, `:443`, `:880`, `:883`, `:885`.

**공격:** `--output` root는 정상 경로지만 그 안의 `sheets`가 다른 폴더를 향하는 junction이다. root의 `resolve()`와 art_dir 비교는 이 하위 링크를 검사하지 않는다. PNG 게시와 stale `pack-<hash>.png` 삭제가 외부에서 실행된다. 임의 이름의 모든 파일을 삭제하는 것은 아니며 hash 이름 문법과 glob 조건이 적용된다.

**재현:** 임시 `pack/sheets`를 임시 `outside`로 연결했다. 실제 `atomic_write()`가 outside에 표식을 만들었고, 실제 stale-cleanup 식은 outside의 합성 `pack-<64 hex>.png`를 삭제했다. 전체 art build는 실행하지 않았다.

**최소 수정:** 출력과 source의 ancestor·하위 디렉터리에 reparse point가 없는지 확인하고, 게시 및 cleanup 모두 최종 출력 경계를 검사한다. 쓰기 가능한 shared output에서는 경로 교체 race를 막기 위해 전용 directory/ACL을 사용한다.

### S12 — Medium: 브랜드 다운로드에 크기·redirect·콘텐츠 검증이 없다

**위치:** `tools/fetch_brand_icons.py:25`, `:38`, `:42`, `:43`, `:45`.

**공격:** 공식 서버/CDN 또는 redirect 응답이 변조되어 과도한 body나 다른 호스트/HTTP의 콘텐츠를 돌려준다. 시작 URL 네 개는 HTTPS지만 최종 전송 경로가 HTTPS라고 보장하지 않는다. timeout=20은 전체 byte 수나 총 작업 시간 제한이 아니다. Content-Type, 예상 magic/format, SHA-256 pin 검사가 없이 원본을 저장한다.

**재현:** 네트워크 대신 memory-only response를 넣었다. 실제 `download()`는 `read(-1)`로 2 MiB를 제한 없이 저장했다. 표준 `HTTPRedirectHandler.redirect_request()`도 HTTPS에서 다른 호스트의 HTTP URL로 변경을 수락했다. HTTP redirect 허용 범위는 [Python 공식 문서](https://docs.python.org/3.13/library/urllib.request.html#httpredirecthandler-objects)와 대조했다. 실제 브랜드 URL은 요청하지 않았다.

**최소 수정:** 명시적 byte 상한을 둔 streaming read와 총 deadline을 적용한다. 매 redirect마다 HTTPS와 승인된 호스트를 검사한다. MIME와 실제 형식을 함께 확인하고, 재현 가능한 원본에는 내용 hash를 pin한다. 실패한 부분 파일은 publish하지 않는다.

### S13 — Medium: PNG 비교 도구는 길이 검사 전에 압축 폭탄을 해제한다

**위치:** `tools/windhawk_parity.py:72`, `:94`, `:100`, `:101`, `:102`.

**공격:** `--pair` 등에 작은 IHDR과 과도한 zlib stream을 포함한 PNG를 준다. CRC가 맞아도 `zlib.decompress()`가 전부 할당한 뒤 scanline 길이를 검사한다. 파일 크기, 치수, pixel 수의 사전 상한도 없다. 이 도구의 입력이 Core PNG 검사를 먼저 통과하는 것은 아니다.

**재현:** IHDR 1×1, CRC 정상인 **2,112-byte PNG**의 IDAT가 **2,097,152 bytes**로 해제된 뒤에야 길이 mismatch가 발생했다. 실제 `read_png()`와 계측 wrapper를 사용했으며 큰 메모리 고갈은 실행하지 않았다.

**최소 수정:** input bytes와 치수/pixel 상한을 먼저 검사하고, `decompressobj().decompress(..., max_length)` 등으로 예상 scanline 수 + 1 byte까지만 해제한다. 여분 compressed/pixel data는 거부한다.

### S14 — Medium, 일부 조건부: 다운로드 이미지를 자동 형식 감지·미고정 SVG 렌더러에 넘긴다

**위치:** `tools/fetch_brand_icons.py:50`, `:51`, `:55`, `:58`, `:61`, `:63`.

**공격:** `.png`/`.ico` URL에서 받은 바이트가 EPS 등 다른 형식이거나 악성 SVG다. `Image.open()`의 자동 format 선택은 외부 Ghostscript handler까지 도달할 수 있고 변환에 별도 deadline이 없다. SVG도 설치된 CairoSVG 버전을 검사하지 않은 채 렌더링한다. JavaScript/shell 실행 코드는 찾지 못했지만 다운로드가 항상 수동적인 PNG 데이터 처리에만 머문다고 말할 수는 없다.

**재현/외부 근거:** 합성 EPS를 `fake.png`로 저장하고 실제 `load_raster()`를 호출했다. Ghostscript 함수를 실행 전에 차단하는 stub에 도달했다(12×12). 실제 외부 프로세스는 실행하지 않았다. Pillow의 `-dSAFER` 같은 라이브러리 보호가 곧 도구의 형식·시간 제한은 아니다. [Pillow 보안 지침](https://pillow.readthedocs.io/en/stable/handbook/security.html)은 이미지 형식 allowlist와 decompression warning의 error 처리를 권고한다.

CairoSVG **<2.9.0**의 recursive `<use>` DoS는 [CVE-2026-31899 / GHSA-f38f-5xpm-9r7c](https://github.com/Kozea/CairoSVG/security/advisories/GHSA-f38f-5xpm-9r7c), **<2.7.0**의 외부 resource 요청 문제는 [CVE-2023-27586 / GHSA-rwmf-w63j-p7gv](https://github.com/Kozea/CairoSVG/security/advisories/GHSA-rwmf-w63j-p7gv)에 명시되어 있다. 이는 설치 버전에 따른 조건부 위험이다. 검증 환경에는 CairoSVG가 없었고 해당 exploit은 실행하지 않았다. 프로젝트가 그 취약 버전을 실제 배포한다는 주장도 아니다.

**최소 수정:** raster는 `formats=("PNG","ICO")` 등 필요한 디코더만 허용하고 크기·pixel 상한을 변환 전에 적용한다. SVG는 현재 PNG fallback을 사용하는 것이 가장 작은 공격 표면 축소다. SVG를 유지하면 패치된 버전을 hash와 함께 고정하고 외부 resource를 차단하며 별도 deadline을 둔다.

### S15 — Low, 조건부: case-sensitive 폴더에서 네이티브 root prefix 비교가 틀린다

**위치:** `windhawk/vibedotcom.wh.cpp:243`, `:263`.

**공격/근거:** NTFS case sensitivity가 켜진 부모 안에 서로 다른 `pack`과 `PACK`이 있고, 내부 링크가 이웃 폴더를 가리킨다. 최종 handle 경로가 서로 달라도 `_wcsnicmp`가 같은 prefix로 취급한다. 일반 NTFS default에서 별도 폴더 두 개를 만들 수 있는 경우로 확대하지 않았다. [Windows의 디렉터리별 case sensitivity](https://learn.microsoft.com/en-us/windows/wsl/case-sensitivity)는 실제 지원되는 조건이다. 입증되는 영향은 팩 밖 PNG 읽기이며 hash 일치도 공격자가 고른 이미지의 출처 경계를 보장하지 않는다.

**최소 수정:** 검증된 최종 handle 경로/디렉터리 identity를 비교할 때 실제 파일 시스템의 case semantics를 적용한다. 정상화한 최종 경로의 정확한 비교와 separator 경계 검사, 또는 reparse 전체 거부를 사용한다.

### S16 — Low: parity 도구의 audited-build 검사에서 실제 실행 EXE가 빠져 있다

**위치:** `tools/windhawk_parity.py:211`, `:212`, `:218`.

**공격:** audited App/Core DLL은 보존하고 같은 폴더의 App.exe만 교체한다. DLL hash 검사 후 그 EXE를 실행한다. 이는 이미 실행 폴더에 쓰기 가능한 공격자라는 전제가 있으며 일반 runtime pack에서 EXE 교체까지 자동으로 이어지는 것은 아니다. 다만 도구가 제공하는 audited executable 보증은 성립하지 않는다.

**재현:** 실제 `run_wpf()`의 hash 함수에는 allowlist와 일치하는 DLL 결과를, launcher에는 아무것도 실행하지 않는 stub을 넣었다. 검사한 이름은 App.dll/Core.dll 두 개뿐이었고, 합성 교체 EXE 경로를 launcher에 전달했다. 실제 EXE는 실행하지 않았다.

**최소 수정:** 실행하는 EXE도 hash allowlist에 넣고 검사 결과에 포함한다. native renderer 실행도 그 출처·빌드 identity를 검증한다. 교체 가능한 폴더에서 audit 이후 파일 교체가 가능하면 ACL/handle 기반 실행 경계도 고려한다.

### S17 — Low: 개인 절대 경로가 공개 문서와 이력에 남아 있다

**위치:** `docs/asset-format.md:19`, `docs/anim-normalize.md:9`, `docs/facelock.md:8`, `docs/seated.md:8`, `assets/art/env3/MANIFEST.md:60`, `assets/art/env4/MANIFEST.md:121`.

**공격/근거:** 공개 저장소 열람만으로 작성자의 Windows 계정 이름과 로컬 도구 설치 위치가 드러난다. 실행 명령의 이식성도 떨어진다. 값 자체는 이 문서에서 다시 게시하지 않는다. 해당 path 패턴은 현재 커밋과 과거 blob에 존재한다.

민감정보용 현재 작업 트리 검색에서도 추가 문서/도구 및 `assets/art/roster/afro-sweater/qa-notes.md:19` 등에서 같은 종류의 path를 발견했다. 이 추가 파일들은 이 검토 커밋의 제품 분석 대상이 아니며, 공개 전 검사에서만 처리해야 한다.

**최소 수정:** 현재 문서는 `%USERPROFILE%`, 가상의 계정/경로 또는 환경 설정으로 치환한다. 이미 공개할 이력까지 익명화하려면 별도 승인된 history rewrite 또는 clean export가 필요하다. 사용자 신원을 유지할지 작성자 이메일을 noreply로 바꿀지도 별도로 결정한다.

### S18 — Info: state의 프롬프트·경로는 평문이며 출력 ACL은 상속에 의존한다

**위치:** `src/Vibedotcom.Core/Models.cs:17`, `:27`, `:59`, `:116`; `src/Vibedotcom.Core/LogParser.cs:24`, `:130`, `:160`; `src/Vibedotcom.Tracker/Program.cs:67`.

**상황:** `Task`는 공백 정리 후 최대 240 UTF-16 code units인 실제 사용자 프롬프트다. cwd, model, ID, 이름/제목도 state에 포함된다. 로컬에만 생성되더라도 공유 output, 동기화 폴더, 백업·지원 첨부에서 노출될 수 있다. `Directory.CreateDirectory`/파일 생성에 사용자 전용 DACL을 명시하지 않는다.

**검증/판정:** 합성 프롬프트와 cwd가 실제 tracker snapshot에 보존되는 것을 확인했다. 임시 출력 ACL은 parent에서 상속됐다. **일반 Windows 사용자 프로필이 Everyone에게 읽히거나 다른 표준 사용자가 기본 `%LOCALAPPDATA%`를 읽을 수 있다는 증거는 없다.** 실제 개인 output ACL과 실제 프로필 데이터는 확인하지 않았다. default world-readable 취약점으로 세지 않는다.

**최소 개선:** task/cwd 게시를 선택하거나 숨길 수 있게 하고 private output 폴더의 유효 ACL을 검증한다. 느슨한 기존/shared 폴더는 경고 또는 거부하고, state·SQLite 임시 복사본·지원 첨부의 민감성을 공개 안내에 설명한다. local ciphertext만 추가해서 같은 사용자 프로세스로부터 보호된다고 주장하지 않는다.

### S19 — Info: 의존성·실행 경로의 공급망 재현성이 부족하다

**위치:** `Directory.Build.props:3`, `:7`; `tests/Vibedotcom.Tests/Vibedotcom.Tests.csproj:6`; `tools/fetch_brand_icons.ps1:2`, `:26`; `tools/fetch_brand_icons.py:15`, `:61`; `tools/windhawk_install_tracker.ps1:12`, `:27`; `tools/release/build-release.ps1:121`.

**상황/근거:** production Core/App/Tracker에는 직접 NuGet PackageReference가 없다. 테스트 직접 버전은 Microsoft.NET.Test.Sdk **17.14.1**, xunit **2.9.3**, runner **3.1.5**로 지정되어 있지만 transitive lock/locked restore, SDK `global.json`, Python 버전·hash lock은 없다. Python 도구는 설치된 Pillow/numpy와 optional CairoSVG를 사용한다. 검증 환경의 Pillow **12.3.0**, numpy **2.5.3**는 프로젝트의 배포 버전 보증이 아니다. 관련 코드에 자동 `pip install`, 원격 스크립트 실행 또는 build-time 브랜드 다운로드는 찾지 못했다. 알려진 조건부 CairoSVG CVE는 S14에만 집계했다. NuGet 전수 advisory audit을 했다는 주장은 하지 않는다.

로그온 작업은 현재 SID, `Interactive`, `RunLevel Limited`, 절대 System32 PowerShell 경로를 사용한다. `-ExecutionPolicy Bypass`가 권한 상승을 의미하지는 않는다. 그러나 task는 등록할 때의 사용자 지정 EXE와 저장소 runner 경로를 이후 로그온마다 실행한다. 다른 사용자가 쓸 수 있는 shared installation/PATH의 프로그램·스크립트 교체는 그 사용자 권한의 실행으로 이어진다. installer는 서명/hash나 파일 ACL을 확인하지 않는다. 정상 사용자 소유의 private install 폴더를 다른 로컬 사용자가 수정할 수 있다고 가정하지 않는다.

**최소 개선:** 검증한 SDK와 Python 의존성을 lock/hash로 기록하고 업데이트 경로를 둔다. 로그온 설치는 사용자 전용 안정된 폴더에 놓인 검증된 EXE/runner를 사용하고 effective ACL을 확인한다. release의 self-contained runtime도 패치 버전·hash를 기록하고 갱신한다.

### S20 — Info: 삭제된 공식 로고와 생성 바이너리가 Git 이력에 남아 있다

**위치:** `.gitignore:23`, `:25`, `:34`, `:42`, `:43`; 과거 `d76f81b:assets/brand/originals/*`, 과거 `3549902:assets/art/sheets/icons.png`.

**상황/근거:** 공식 원본 5개를 추적 해제한 `25ea90a`와 ignore 규칙은 이전 Git blob을 없애지 않는다. 예: ChatGPT favicon/logo, Claude apple-touch PNG, Gemini favicon/sparkle SVG. 과거 provider icon sheet와 이를 포함한 생성 팩도 추가 확인 대상이다. 이는 라이선스 법률 판단이나 현재 HEAD에 원본 로고가 다시 포함됐다는 주장이 아니다. 공개 배포 방침과 Git source archive 사이의 차이다.

HEAD 조상의 생성 바이너리에는 `tools/__pycache__/facelock.cpython-312.pyc`, `facelock_qa.cpython-312.pyc`도 남아 있다. 1 MiB 이상 blob 12개는 roster의 `base.png`이며 최대 약 1.29 MB다. 조회한 이력에서 EXE/DLL/DB/ZIP 같은 해당 확장자의 committed object는 발견하지 못했다. 이 이미지들이 비밀 또는 악성 파일이라는 증거는 없으며 크기만으로 취약점으로 세지 않는다.

**최소 개선:** 공개할 ref 전체에서 금지 원본 및 파생 로고 이미지·bytecode를 제거할지 결정한다. 필요한 원본 아트는 출처와 보존 필요성을 확인한 뒤 유지한다. 구체적인 purge 체크리스트는 마지막 부록에 둔다.

## 검증 기록과 발견하지 못한 문제

실제 커밋의 Core 소스를 별도 임시 .NET 10 console harness에 포함했다. framework-only restore는 빈 로컬 package source와 `NuGetAudit=false`를 사용했고 `DOTNET_CLI_HOME`/package cache도 임시 폴더였다. Python 테스트는 기존 환경을 `-B`로 사용했다. 저장소에 테스트를 추가하지 않았다.

| 검사 | 실제 결과 | 한계 |
|---|---|---|
| managed sheet junction | 외부 합성 PNG 수락, 경고 0 | 실제 사용자 파일은 사용하지 않음 |
| output junction | fake provider 폴더에 합성 표식 생성 | 실제 provider 폴더에는 접근하지 않음 |
| lexical `../` / 직접 보호 output | 둘 다 거부 | reparse 우회는 별도 재현됨 |
| JSON 100단 깊이 / PNG width 100,000 | 둘 다 거부 | 다른 malformed image의 전수 fuzz는 아님 |
| PNG sheet 128개 | 수락, fallback 포함 8,775,870 bytes 보관 | 최대 메모리 고갈은 실행하지 않음 |
| JSONL 2 MiB 초과 incremental record | dropped=1, records=0, retained=65,536 | 초기 bootstrap의 중간 구간 생략과 구분 |
| 작은 로그의 session ID fanout | 3,000 agents / 377,030 bytes | 장기 무한 append는 실행하지 않음 |
| SQLite hostile VIEW | 4 KB DB → 256행, 16,777,216 문자 | native engine exploit·무한 query는 실행하지 않음 |
| seated publication / builder junction | TEMP 밖이 아닌 합성 경계 밖 쓰기·삭제 확인 | 전체 art pipeline은 실행하지 않음 |
| 브랜드 memory response / redirect handler | `read(-1)` 2 MiB 저장, HTTP downgrade 수락 | 네트워크 연결 없음 |
| PNG parity inflation | 2,112 bytes → 2 MiB 후 거부 | 실제 OOM 없음 |
| EPS decoding dispatch | Ghostscript 함수 stub 진입 | 외부 interpreter는 실행 전에 차단 |
| parity audited-build 경로 | DLL 두 개만 검사, 교체 EXE launcher 전달 | launcher/hash 결과 stub, 실제 EXE 실행 없음 |
| `winsqlite3.dll` search | 격리 assembly 폴더의 무해한 stub 대신 system DLL 사용 | 실제 single-file publish 동작은 미검증; sideload 성공으로 보고하지 않음 |

추가로 확인한 보호 장치와 범위별 판단:

- **Core JSON/PNG:** 외부 layout/asset JSON에 4 MiB 사전 길이 검사, collection/geometry 상한이 있다. System.Text.Json의 기본 최대 깊이는 64이며 [공식 문서](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.maxdepth) 및 재현과 일치한다. PNG는 8-bit/non-interlaced 형식, 8,192 치수/16M pixels, chunk length, scanline filter와 예상 압축 해제 길이를 검사한다. 정상적인 giant-dimension/decompression overflow를 그대로 통과시키는 문제는 확인하지 못했다. 길이 검사는 path stat 이후 사용하므로 공유 writer가 파일을 바꾸거나 키우는 TOCTOU는 남는다; 열린 handle과 bounded read에 제한을 다시 적용해야 한다.
- **WPF와 settings:** 앱 manifest는 `asInvoker`, `uiAccess=false`다. settings JSON의 깊이와 수치 clamp는 있지만 `ReadSettings()` (`src/Vibedotcom.App/Program.cs:369`)에는 파일 byte 상한이 없다. private settings를 수정할 권한이 있다는 전제가 필요한 작은 hardening gap으로, 악성 pack만으로 settings를 바꿀 수 있다고 주장하지 않는다. 크기 제한을 추가할 때 외부 JSON 정책을 재사용하면 된다.
- **Windhawk:** JSON 4 MiB/depth 64/node 250,000/string 65,536 bytes/agent 2,048개, PNG 파일당 32 MiB와 pack 전체 32M pixels 제한이 있다. duplicate key·잘못된 UTF-8/surrogate·비유한 숫자·상대 sheet의 절대 경로/ADS/`..`를 거부한다. final handle root 포함 검사와 SHA-256, metadata/decoded dimension 검사가 있으며 새 pack은 전체 로드 성공 후 교체한다. 일반적인 하위 junction 이탈은 막지만 S02/S15 예외가 있다. 128 MiB sheet pixel budget은 Explorer 총 메모리·CPU의 상한이 아니다.
- **네이티브 정확성:** 정적 검토에서 입력으로 도달하는 buffer overrun, integer overflow, format-string 입력 사용, dangling pointer, 잘못된 wide/narrow lifetime을 발견하지 못했다. 취소 I/O 완료까지 `OVERLAPPED`와 버퍼를 유지하고 UI/worker 전달 객체는 공유 소유권을 쓴다. native PNG decoder의 내부 취약점이나 실제 Explorer에서의 안정성을 fuzz/통합 검증한 것은 아니다.
- **명령·인자:** installer/runner의 quote/newline 거부, runner의 trailing backslash 거부, argument array/subprocess list 사용을 확인했다. 검토한 `&`, `;`, `$()`가 들어간 경로에서 새 shell 명령을 구성하는 경로를 찾지 못했다. release의 Version/Configuration은 검증되며 dotnet/Git 인자는 분리해 전달된다. response file의 일반 경로와 MSBuild 호출에서 실제 사용자 pack 문자열이 평가되는 주입 primitive를 발견하지 못했다. 사용자가 명시적으로 선택하는 compiler options와 Python/EXE 실행 경로는 실행 코드의 신뢰 입력이다.
- **아카이브:** 범위 내 ZIP/TAR extraction 코드는 찾지 못했다. release는 `Compress-Archive`로 생성한다. 따라서 ZIP entry의 zip-slip을 발견했다고 보고하지 않는다. package guard는 ancestor와 tree의 reparse를 거부하며 cleanup 대상의 절대 parent를 검사한다. 이는 runtime/build_pack의 경로 경계를 대신하지 않는다.
- **원자적 쓰기·race:** `StateJson.WriteAtomic`과 editor save는 GUID + `CreateNew` + flush + same-directory rename/replace, builder는 `NamedTemporaryFile`을 사용한다. 단순히 예측 가능한 이름을 먼저 만드는 race는 이 경로들에서 확인하지 못했다. parent가 공격자에게 writable이면 close 이후 rename 대상·directory junction 교체는 여전히 문제다. `seated.publish()`는 고정 `.tmp` 이름을 사용하므로 private output이 전제되어야 하며 unique exclusive temp로 바꾸는 것이 작다. 어떤 경로도 관리자 권한으로 실행해 검증하지 않았다.
- **native DLL:** `SqliteSnapshot.cs:88`의 system DLL import에 명시적인 `DllImportSearchPath.System32`는 없다. 격리 테스트에서 sideload 성공은 없었으므로 취약점 수에 넣지 않았다. system import를 System32로 좁히는 것은 [dotnet 공식 interop 지침](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/interop-guidelines.md)의 방어책이며 publish 형태별 확인이 필요하다.

## 민감정보·Git 이력 조사 결과

추가 secret 검색은 작업 트리를 변경하지 않고 Git refs의 unique blob과 현재 tracked/untracked 비무시 파일에 적용했다. 동시 편집 때문에 현재 트리와 ref metadata 조회는 고정 snapshot이 아니며 제품 분석 기준을 바꾸지 않는다. 검색에서 읽은 unique history blob은 **1,400개 / 36,108,812 bytes**, 그중 binary는 789개였고, 현재 트리 파일은 **3,971개**였다. 접근 거부된 `.generations` 디렉터리와 ignored/generated local data는 완전 검색했다고 주장하지 않는다.

- OpenAI/GitHub/AWS/Google/Slack token 패턴, private-key header, 길고 실제 값처럼 보이는 credential assignment에서 확인된 secret은 **0건**이다. 문자열 signature 검색으로 모든 비밀 부재를 보증하지 않는다. token을 원격 서비스에 보내 유효성을 시험하지 않았다.
- 이메일 후보는 공식 연락처, `@2x.png` 같은 파일명, 로컬 Windhawk mod ID, binary 압축 바이트의 오탐이었다. Git 작성자 metadata에는 고유 이메일 1개가 있다. 공개 의도는 확인하지 않았고 주소를 이 문서에 복제하지 않았다.
- HEAD fixture 3개는 `C:/Example`, `example-model`, `Inspect the example file.` 등 합성 데이터다. `docs/antigravity-notes.md`의 title/prompt/ID/path 예시는 redacted 또는 가상 값이며 인증 token 값은 찾지 못했다. 실제 조사 시각·프로세스/포트·환경 버전은 남아 있으므로 공개할 조사 기록의 범위를 별도로 결정한다.
- `assets/art/roster/*/qa-notes.md`를 읽어 generation/review 설명과 경로를 확인했다. HEAD 기록에서 실제 세션 프롬프트·token은 확인하지 못했다. 현재 트리의 추가 QA 파일에는 S17의 개인 경로가 있다. 아트 생성 요구사항은 사용자 세션 비밀과 구분한다.
- HEAD 이미지 경로에서 docs/screenshots나 실사용 화면 캡처라고 명명된 파일은 찾지 못했다. 대표 `A-contact.png`, `assets/art/preview.png`를 직접 보았으며 캐릭터/사무실 아트였다. image blob에서 PNG text/EXIF marker 후보는 찾지 못했다. 모든 이미지 픽셀을 OCR/수작업 전수 확인한 결과는 아니므로 공개 전 나머지 이미지를 사람이 확인해야 한다.
- 공식 원본 blob은 `1634901…`, `4005d2d…`, `2b8a58c…`, `bb41f52…`, `4545c83…`다. 현재 ignore 규칙은 과거 logo sheet·생성 pack·bytecode와 공개 ref를 정리하지 않는다.

## 수정 우선순위

1. **공개 전 차단:** S01의 데이터 파일명 기반 쓰기와 S02의 Explorer 원격 open을 막는다. 낯선 pack/art를 검증 전 설치·변환하거나 mod에 연결하는 경로에 이 경계를 적용한다.
2. **파일 보존:** S03/S04/S11을 같은 경로 정책으로 해결한다. input/output root, ancestor, 열린 handle, cleanup에 모두 적용하고 기존 사용자 데이터를 대상으로 테스트하지 않는다.
3. **Explorer 작업량:** S09/S10의 표시 길이·sprite 면적·frame budget을 제한한다. S15 case semantics도 함께 수정한다. 제한은 renderer가 실제로 작업하기 전에 적용한다.
4. **추적기 가용성:** S05/S06/S07/S08의 전체 bytes/pixels/agents/response budget, SQLite schema·query deadline, conversation ID 문법을 적용한다.
5. **다운로드·도구:** S12/S13/S14의 bounded read/decompression, HTTPS redirect/host 정책, 형식 allowlist와 SVG 축소를 적용한다. S16은 실행하는 바이너리 hash를 검사한다.
6. **공개 운영 준비:** S17의 익명화, S18의 평문 state 안내/ACL 확인, S19의 의존성·설치 경로 기록, S20의 승인된 이력 정리를 완료한다. private vulnerability reporting을 실제로 활성화한다.

각 수정 후 해당 경계의 작은 회귀 재현만 먼저 실행한다. 이 보고서 작성 중 제품 수정이나 광범위한 빌드/테스트를 대신 수행하지 않았다.

## 부록 A — SECURITY.md 초안

아래는 후속 승인으로 별도 `SECURITY.md`에 옮길 수 있는 초안이다. 이 검토에서는 파일을 생성하지 않았고 GitHub 설정도 변경하지 않았다. 보안 정책 파일만으로 private reporting이 활성화되지는 않는다. 공개 repository의 관리자가 [GitHub private vulnerability reporting 설정](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository)을 켜야 한다.

```markdown
# Security Policy

## Supported versions

Before the first public release, security reports against the current main
branch are accepted. Development snapshots are not production releases.

After public release, only the latest published release is supported for
security fixes. Older releases, forks, and locally modified binaries are
not maintained. Please also report a vulnerability in a supported release
when it has already been fixed on main.

## Reporting a vulnerability

Use this repository's GitHub Security Advisories page and select
"Report a vulnerability" to submit a private vulnerability report.
Please do not disclose exploit details, private prompts, or credentials
in public issues, pull requests, or discussions.

If the private reporting button is unavailable, open an issue asking only
for a private reporting channel. Do not include the vulnerability or any
sensitive data in that issue.

Include the affected version/commit, Windows and runtime versions,
affected component, attack prerequisites, expected and actual behavior,
impact, and a minimal reproducer using synthetic data. Sanitise logs,
paths, state files and screenshots. Coordinate publication with the
maintainers while the report is investigated and a fix is prepared.
This project does not promise a bounty or a fixed response deadline.

## In scope

- Path traversal, reparse-point escapes, unintended file changes and
  disclosure caused by layouts, asset packs, brand overrides or outputs.
- Malformed or hostile logs, JSON, PNG and SQLite files causing
  unintended execution, disclosure or resource exhaustion.
- Memory safety, lifetime and availability defects in the Windhawk
  renderer running inside Explorer.
- Command/argument injection, unsafe executable loading, logon task
  behavior and compromised-download handling in the shipped tools.
- Dependency vulnerabilities reachable through these components,
  exposed credentials, and unintended publication of private data.

## Out of scope

- Attacks that require an already fully compromised account/admin/OS,
  unless the report demonstrates an additional project trust-boundary
  violation such as a data file causing unintended execution.
- Unsupported forks/versions, unrelated third-party vulnerabilities
  without a demonstrated project impact, and purely visual defects.
- Trademark/licensing questions without a security impact; use an
  ordinary issue for those and include no private information.

## Safe research

Use your own disposable files, temporary profiles and isolated test
processes. Do not access another person's prompts or credentials,
damage user data, test denial of service in their Explorer session,
or send traffic to third-party systems without authorisation.
Vibedotcom's local state can contain prompt text and project paths;
never attach an unredacted real state.json to a report.
```

private report 제출 경로는 [GitHub의 공식 절차](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/report-privately)를 따른다. maintainer가 첫 release 이후 실제 지원 범위를 확정할 때 이 초안도 함께 확인해야 한다.

## 부록 B — 공개 전 Git 이력 체크리스트

이 목록은 수행 지시나 자동 purge 결과가 아니다. 이력 재작성, force push, 기존 tag 교체는 별도 승인이 필요한 후속 작업이다.

- [ ] 공개 대상으로 정한 **모든 branch/tag/ref**와 source archive를 목록화하고 freeze한다. 작업 중인 다른 agent의 commit이 추가된 뒤에는 최종 공개 snapshot을 다시 검사한다.
- [ ] `assets/brand/originals/`의 위 5개 원본을 공개 정책에 따라 모든 공개 이력에서 제거한다. `assets/brand-overlay/`의 historical tracked blob이 있는지도 최종 refs에서 확인한다.
- [ ] 이전 `assets/art/sheets/icons.png`, `assets/pack/sheets/icons.png` 및 hashed sheet, embedded pack, preview/contact image에 공식 glyph가 포함된 버전을 확인해 정책상 금지된 **파생 이미지도** 제거한다. 파일 이름만 지우거나 최신 중립 아이콘으로 교체하는 것으로 과거 blob은 없어지지 않는다.
- [ ] `tools/__pycache__/*.pyc` 등 재생성 바이너리를 purge한다. EXE/DLL/PDB/DB/WAL/SHM/ZIP, log/state/settings, provider cache가 새 commit에 들어가지 않았는지 확인한다.
- [ ] S17의 개인 계정 경로와 원하지 않는 작성자 이메일을 현재 파일·commit metadata·이력에서 익명화할지 결정한다. 본 보고서에도 실제 이메일/개인 계정 값이 복제되지 않았는지 확인한다.
- [ ] fixture, `docs/antigravity-notes.md`, 모든 `qa-notes.md`, 요청 JSON, generation 보고서와 남은 image pixels를 확인한다. 실제 prompts, 대화 제목/UUID, 프로젝트 이름, token, machine/user 식별자, screenshot 속 계정 정보를 제거하거나 합성 값으로 바꾼다. 합법적인 아트 제작 지시와 실사용 세션 비밀을 구분한다.
- [ ] 1 MiB 이상 `assets/art/roster/*/base.png` 12개와 추가 large blob의 필요성·권리·metadata를 판단한다. 크기만으로 전부 삭제하지 않는다; 필요 없는 source/intermediate binary만 공개 이력에서 제거한다.
- [ ] credential이 발견되면 **먼저 revoke/rotate**하고 이력에서 제거한다. ignore 규칙과 Git 삭제만으로 이미 노출된 credential이 안전해지지 않는다.
- [ ] 승인된 rewrite/clean export 후 새 clone에서 전체 ref·unique blob을 재검색한다. 기존 clone, PR ref, release asset, CI artifact, mirror/cache에 금지 내용이 남을 수 있음을 별도로 점검한다.
- [ ] 최종 release package guard, checksum, dependency/runtime inventory, state/settings 제외, private install ACL, SECURITY.md 지원 범위와 GitHub private reporting 활성화를 확인하고 공개한다.

## Fix status

| Finding | Status | Regression test / verification |
|---|---|---|
| S01 | fixed | `test_seated_rejects_manifest_traversal_before_image_decode`, `test_seated_publication_and_character_id_cannot_escape_output` |
| S02 | fixed | Windhawk `HardeningInputTests`: ancestor junction, 열린 ancestor의 변경 차단 |
| S03 | fixed | `PackSheetAndManifestJunctionsUseFallback`, `JunctionInputsAndOutputsAreRejectedBeforeAccess` |
| S04 | fixed | `JunctionInputsAndOutputsAreRejectedBeforeAccess`, `ExcessivePathDepthIsRejectedBeforeDirectoryCreation` |
| S05 | fixed | `IndividuallyValidSheetsCannotExceedAggregatePixelBudget`, Windhawk `HardeningInputTests`의 누적 compressed bytes, `test_builder_rejects_frame_fanout_and_pixel_work_before_allocation` |
| S06 | fixed | `JsonlFanoutAndMetadataAreBoundedAcrossPolls` |
| S07 | fixed | `SqliteOversizedCopiesAndLinksAreRejected`, `SqliteViewsTextAndRecursiveWorkAreRejected` |
| S08 | fixed | `LoopbackMapRejectsPathIds`, `LoopbackMapCountIsBounded`, `LinkedProviderCatalogsDoNotEscapeOrCrashPoll`; 서버 소유권을 검증할 수 없어 저장 파일만 사용 |
| S09 | fixed | Windhawk `HardeningInputTests`: 65,000자 이름의 default 표시 길이 제한 |
| S10 | fixed | Windhawk `HardeningInputTests`: cell과 다른 frame 크기 거부; 기존 renderer/cache 검사 |
| S11 | fixed | `test_builder_output_junction_never_writes_or_deletes_outside`, `test_builder_source_links_and_directory_fanout_are_rejected`, `test_publication_lock_rejects_hard_link_without_changing_marker`, `test_write_boundary_pins_empty_directory_and_cleans_guard` |
| S12 | fixed | `test_download_rejects_http_host_redirect_mime_size_hash_without_publication`, `test_download_deadline_rejects_unpublished_body` |
| S13 | fixed | `test_parity_rejects_png_bomb_trailing_stream_dimensions_and_chunk`, `test_parity_normal_png_roundtrip` |
| S14 | fixed | `test_raster_rejects_eps_svg_and_oversized_header`; SVG를 비활성화하고 기존 PNG fallback 사용 |
| S15 | fixed | final handle 경로의 ordinal 비교와 separator 경계; Windhawk `HardeningInputTests` 및 기존 경로 검사. 실제 case-sensitive 볼륨은 미검증 |
| S16 | fixed | `test_audited_dlls_do_not_authorize_replaced_executable`, `test_render_only_marker_does_not_authorize_native_executable`; 별도 binary audit까지 EXE allowlist를 비워 자동 실행 거부 |
| S17 | mitigated | 현재 tracked 문서·JSON 기록의 개인 계정 경로를 placeholder로 치환; JSON 재파싱·경로 패턴 검사. 이력/작성자 metadata는 유지 |
| S18 | not fixed | task/cwd 평문 게시와 effective ACL 정책은 유지. 실제 개인 프로필/ACL은 조사하지 않음 |
| S19 | mitigated | `global.json`, `tools/requirements-build.txt`, System32 native imports. 패키지 hash lock·설치 ACL·release runtime audit는 미구현 |
| S20 | not fixed | 현재 tracked bytecode 검사; 과거 logo/bytecode blob의 purge 및 history rewrite는 하지 않음 |
