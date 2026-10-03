# Core / Tracker 읽기 전용 코드 리뷰

검토일: 2026-10-03, Windows 11 / .NET SDK 10.0.400. `SPEC.md`, `docs/state-schema.md`, `docs/antigravity-notes.md`를 기준으로 `AgentTracker`, `JsonlTail`, `LogParser`, Tracker 진입점과 기존 테스트를 읽었다. 상태 전이를 이해하는 데 필요한 `Models.cs`도 읽었다. 저장소에서는 이 문서만 작성했다. `src/`, `tests/`, 실제 공급자 데이터에는 쓰지 않았다.

재현은 소스 복사본과 합성 로그를 `%TEMP%`에서 사용했다. 아래에서 **재현**은 구현의 실패를 실제로 실행해 확인했다는 뜻이며, 해당 입력 형태를 설치된 공급자가 현재 생성하는지까지 확인했다는 뜻은 아니다. 특히 Claude 혼합 sidechain 및 병렬 질문 입력은 합성 사례다. Antigravity의 실제 관측 범위는 기존 조사 문서의 범위를 넘겨 주장하지 않는다.

## 발견 사항

P1: 로그/개체 식별이 지속적으로 잘못될 수 있어 우선 수정. P2: 특정 조건의 상태·관계 오류 또는 규모에 따른 자원 문제.

| ID | 심각도 | 위치 | 문제 |
|---|---|---|---|
| R1 | P1 | `src/Vibedotcom.Core/JsonlTail.cs:22`, `src/Vibedotcom.Core/AgentTracker.cs:75` | 같은 크기 재작성, 축소 후 재성장, metadata를 보존한 교체를 놓침 |
| R2 | P2 | `src/Vibedotcom.Core/JsonlTail.cs:37` | bootstrap suffix가 행 경계에서 시작해도 첫 완성 행을 버림 |
| R3 | P2 | `src/Vibedotcom.Core/AgentTracker.cs:86`, `src/Vibedotcom.Core/LogParser.cs:133` | 한 Claude 파일의 부모·자식 레코드를 하나의 개체로 합침 |
| R4 | P2 | `src/Vibedotcom.Core/AgentTracker.cs:40`, `src/Vibedotcom.Core/AgentTracker.cs:87` | 개체 prune 후 source offset이 남으면 재개한 worker의 관계/metadata가 사라짐 |
| R5 | P2 | `src/Vibedotcom.Core/LogParser.cs:77` | 문자열형 Codex subagent source를 supervisor로 분류 |
| R6 | P2 | `src/Vibedotcom.Core/LogParser.cs:90`, `src/Vibedotcom.Core/LogParser.cs:120` | 오래된 이벤트가 상태는 못 바꿔도 task/metadata를 되감음 |
| R7 | P2 | `src/Vibedotcom.Core/LogParser.cs:59` | 5분 이내 미래 timestamp가 이후 정상 이벤트를 막음 |
| R8 | P2 | `src/Vibedotcom.Core/LogParser.cs:158` | 여러 Claude tool_use 중 마지막 도구만 보면 사용자 질문을 놓침 |
| R9 | P2 | `src/Vibedotcom.Core/AgentTracker.cs:76`, `src/Vibedotcom.Core/AgentTracker.cs:93` | 읽기 공유를 거부하는 live writer 때문에 최근 쓰인 기존 agent도 gone이 됨 |
| R10 | P2 | `src/Vibedotcom.Core/AgentTracker.cs:118`, `src/Vibedotcom.Core/Models.cs:46` | Antigravity 유지 관리 mtime이 종료 상태를 덮고 working↔idle 및 elapsed 오류를 만듦 |
| R11 | P2 | `src/Vibedotcom.Core/JsonlTail.cs:61` | 완료된 큰 행의 2MiB 버퍼가 source마다 장시간 남음 |
| R12 | P2 | `src/Vibedotcom.Core/AgentTracker.cs:53`, `src/Vibedotcom.Core/AgentTracker.cs:61` | 오래된 세션까지 기본 3초마다 전체 metadata 열거·배열 할당 |
| R13 | P2 | `src/Vibedotcom.Tracker/Program.cs:33` | 출력 경로 끝의 구분자 때문에 동일 디렉터리에 두 writer를 허용 |

### R1 — 교체/재작성 감지와 offset 리셋이 불충분

**심각도:** P1. **위치:** `JsonlTail.cs:21–23`, `AgentTracker.cs:75–77`.

**실패 시나리오 / 재현:** `{"old":1}\n`을 읽어 offset=10이 된 뒤 같은 파일에 `{"new":2}\n`을 재작성하면 생성 시각과 크기가 같아 `WasReset=false`, 반환 행=0이다. 다음 재작성에서 `{"new":2}\n{"next":3}\n`으로 20 bytes까지 성장하면 `next`만 반환한다. 짧아진 중간 상태가 poll 사이에 끝나면 `file.Length < Offset`으로는 감지할 수 없다. 미완성 행이 남아 있었다면 새 파일의 suffix와 이전 pending bytes가 결합할 수도 있다.

별도 교체 실험에서는 생성 시각까지 보존한 10-byte 새 파일도 `WasReset=false`로 누락됐다. 더 상위의 문제도 있다. Tracker에 `first1` metadata를 읽힌 뒤 같은 length/mtime을 가진 `second` 파일로 교체하고 생성 시각을 **다르게** 설정해도 `Read`의 fast path가 tail 호출 자체를 건너뛰어 `codex:first1`이 남았다.

**국소 수정안:** Tracker의 metadata fast path가 교체 검사를 우회하지 않게 한다. tail이 연 handle의 Windows volume/file ID를 비교하고, 변경되면 기존 reset 경로로 보낸다. 생성 시각을 file ID 대신 사용하지 않는다. 동일 파일의 축소·재성장은 변경된 metadata 및 저장해 둔 작은 prefix/checkpoint byte 영역을 비교하여 reset한다. reset 때 offset/pending/skipping을 함께 초기화하는 기존 코드는 유지한다.

```csharp
// JsonlTail.Read의 리셋 판정에 필요한 형태. helper 구현을 생략한 제안이다.
var identity = GetFileIdentity(file.SafeFileHandle);
WasReset = initialized && (identity != previousIdentity ||
    file.Length < Offset || CheckpointChanged(file));
```

file ID 조회는 표준 Win32 handle API의 작은 helper로 충분하다. checkpoint 비교는 수백 bytes의 제한된 재읽기로 구현할 수 있다. 다만 같은 file ID/metadata를 유지하며 비교 영역 밖만 수정한 경우까지 이 방법이 보장하지는 않는다. Antigravity in-place step 수정은 아래 SQLite/runtime 상태로 보완해야 한다. 파일 전체 재읽기를 해결책으로 삼지 않는다.

**누락 테스트:** 같은 크기 overwrite, 축소 후 offset 이상으로 재성장, 미완성 UTF-8을 남긴 교체, 생성 시각 보존 교체, length/mtime 보존·file ID 변경 교체, reset 후 다음 append의 정확한 offset.

### R2 — suffix 시작이 완성 행인데도 버림

**심각도:** P2. **위치:** `JsonlTail.cs:34–37`.

**실패 시나리오 / 재현:** 512KiB suffix의 첫 byte를 완성된 `{"recent":"only-event"}\n`의 시작에 정확히 맞췄다. 그 앞 byte는 LF다. 그럼에도 `skipping=true`이므로 이 이벤트가 누락되고 뒤의 padding 행만 읽힌다. 유일한 final/task/tool 이벤트라면 예전 상태를 유지한다.

**작은 수정안:** suffix 앞 byte가 LF인지 확인한 뒤, 행 중간일 때만 skip한다. position은 다시 suffix 시작으로 복구한다.

```csharp
Offset = end - BootstrapBytes;
file.Position = Offset - 1;
skipping = file.ReadByte() != '\n';
file.Position = Offset;
pending.SetLength(0);
```

**누락 테스트:** suffix가 정확한 LF 다음, CRLF 다음, JSON 내부, 한글/emoji 내부에서 각각 시작하는 경우. 마지막 경우에는 첫 불완전 행만 버리고 다음 완성 행은 읽어야 한다.

### R3 — Claude inline sidechain이 부모를 worker로 바꿈

**심각도:** P2. **위치:** `AgentTracker.cs:86–88`, `LogParser.cs:128–138`.

**실패 시나리오 / 재현:** 부모 파일에 `sessionId=parent`인 부모 user 행을 먼저 쓰고, `sessionId=parent, agentId=child, isSidechain=true`인 자식 assistant 완료 행을 덧붙였다. 결과는 두 개체가 아니라 한 개체였다: `Id=claude:parent`, `ParentId=claude:parent`, `IsSubagent=true`, `Reporting`. 부모가 자신을 부모로 가리킨다. `Identity`는 자식 ID를 계산할 수 있지만 파일의 첫 ID로 고정한 `source.AgentId ??=` 때문에 호출되지 않는다.

현행 Claude 문서는 별도 `sessionId/subagents/agent-{agentId}.jsonl` 형식을 설명한다. 이 별도 파일 경로는 기존 테스트가 확인한다. 혼합 파일 실패는 재현했지만, 현재 설치본이 혼합 행을 생성한다고 확인한 것은 아니다. [Claude 공식 transcript 설명](https://code.claude.com/docs/en/sub-agents#resume-subagents)

**작은 수정안:** Claude만 레코드별 identity를 선택한다. 한 source에 참여한 IDs를 작은 집합으로 저장해 reset/mtime 처리를 맞춘다. 자식 ID를 알 수 없는 inline sidechain은 부모를 worker로 변환하지 말고 관계 미상으로 유보한다. `parentUuid`는 메시지 UUID이므로 session/agent ID로 사용하면 안 된다.

```csharp
var id = provider == Provider.Claude
    ? LogParser.Identity(provider, file.FullName, doc.RootElement)
    : source.AgentId ??= LogParser.Identity(provider, file.FullName, doc.RootElement);
```

**누락 테스트:** 부모→자식→부모 혼합, 복수 자식, `agentId` 없는 sidechain, 최초 행이 `progress`/summary인 경우, 파일명 기반 자식, 부모 파일이 없거나 오래된 경우. 자기 자신 연결이 없는지도 검사한다.

### R4 — prune 뒤 재개하면 source offset과 개체 metadata가 분리됨

**심각도:** P2. **위치:** `AgentTracker.cs:40–43`, `AgentTracker.cs:86–88`.

**실패 시나리오 / 재현:** parent가 있는 Codex child metadata와 `session_end`를 읽었다. 짧은 테스트 설정으로 GoneRetention=2s, GoneAfter=10s를 사용하고 3s 뒤 poll하여 개체를 prune했다. source는 계속 남았다. 4s 뒤 같은 rollout에 user_message만 append하면 tail은 metadata를 다시 읽지 않고 새 `TrackedAgent`를 만든다. 동일한 `codex:child`가 `ParentId=null`, `IsSubagent=false`, `Name=Codex`로 재등장했다. 기본 설정에서도 explicit gone 뒤 60s retention과 30분 source 보존 사이에 같은 조건이 생긴다.

**작은 수정안:** 표시 대상에서 제거하는 것과 source metadata 캐시를 폐기하는 것을 분리한다. source가 살아 있는 동안 identity/parent/role/model/cwd를 보관하고 새 개체에 복원한다. 또는 해당 source의 bounded metadata prefix만 다시 해석한다. offset을 무조건 0으로 되감아 과거 상태 이벤트까지 replay하지 않는다. Claude R3와 함께 고치면 source별 개체 캐시 하나로 해결할 수 있다.

**누락 테스트:** explicit 종료→prune→동일 파일 재개, worker reporting 이후 재개, 부모만 먼저 prune, 자식만 먼저 prune, 실제 축소/교체 뒤에는 이전 metadata를 재사용하지 않는 경우.

### R5 — Codex의 문자열 subagent source를 놓침

**심각도:** P2. **위치:** `LogParser.cs:74–79`.

**실패 시나리오 / 재현:** parent ID가 없는 `source:{"subagent":"review"}` metadata를 입력하면 `IsSubagent=false`, 이름 `Codex`가 된다. `SubAgentSource`에는 `Review`, `Compact`, `MemoryConsolidation` 같은 unit variant도 있으므로 object만 인정하는 조건은 불완전하다. 부모 미상과 supervisor는 다른 상태이며 `state-schema.md:88–89`도 이를 구분한다. [Codex 공식 protocol source](https://github.com/openai/codex/blob/main/codex-rs/protocol/src/protocol.rs)

**작은 수정안:** `source.subagent`의 알려진 문자열과 object를 모두 worker로 분류한다. parent는 근거가 있을 때만 연결한다. 구버전 `agent_type` alias도 읽으면 작은 호환 보완이 된다.

```csharp
var subagent = Get(Get(p, "source"), "subagent");
a.IsSubagent = parent != null || subagent.ValueKind == JsonValueKind.Object ||
    subagent.ValueKind == JsonValueKind.String;
a.Role = String(p, "agent_role") ?? String(spawn, "agent_role") ?? String(spawn, "agent_type");
```

**누락 테스트:** `review`/`compact`/`memory_consolidation`, `other` object, top-level parent만 존재, source 안 parent만 존재, 둘이 함께 존재, parent 파일 미발견, role alias. 문자열 미지 값은 worker 근거로만 사용하고 이름/부모를 추측하지 않는다.

### R6 — 오래된 이벤트가 task와 정체성 metadata를 되감음

**심각도:** P2. **위치:** `LogParser.cs:90`, `LogParser.cs:120`, `LogParser.cs:141`, `LogParser.cs:154`, `LogParser.cs:169`; metadata의 경우 `LogParser.cs:72–84`.

**실패 시나리오 / 재현:** 12:00:09 user_message `Latest task` 뒤에 12:00:01 user_message `Old task`를 적용하면 `LastEvent=12:00:09`를 유지하면서 Task는 `Old task`가 된다. 시간 검사는 `State()` 안에만 있고 필드 대입은 먼저 실행된다. 현재 `ProviderFinalAnswersWaitForUserAndIgnoreOlderEvents`는 state만 검사하여 이 문제를 놓친다.

별도 실험에서 최신 worker metadata/이벤트 뒤 오래된 `source=cli` metadata를 적용하자 parent/worker/model까지 사라졌다. 여러 파일이 같은 ID를 가리키거나 오래된 prefix/재개 기록을 섞어 읽을 때 상태와 tooltip이 모순된다.

**작은 수정안:** user/task 및 변경 가능한 context를 대입하기 전에 해당 이벤트의 순서를 검사한다. static session metadata는 오래됐다는 이유로 통째로 버리지 말고, 기존 값과 모순되는 역행 갱신 및 null 덮어쓰기를 막는다. 특히 parent를 발견하지 못한 metadata가 이미 아는 parent를 지우면 안 된다.

```csharp
// 각 user/task 이벤트 branch에서, Task 대입보다 먼저 검사한다.
if (time < a.LastEvent) return;

// session metadata의 정보 누락은 기존 lineage를 지우지 않는다.
a.ParentId = parent == null ? a.ParentId : Key(Provider.Codex, parent);
var subagent = Get(Get(p, "source"), "subagent");
a.IsSubagent |= parent != null ||
    subagent.ValueKind is JsonValueKind.Object or JsonValueKind.String;
a.Model = String(p, "model") ?? a.Model;
```

마지막 block은 merge 방향을 보여주는 부분 제안이다. 모델/cwd의 최신 선택을 보장하려면 context timestamp를 별도로 보관한다. `LastSeen` 갱신도 여러 source 사이에서 감소하지 않도록 max로 병합하는 것이 맞다.

**누락 테스트:** 오래된 user/task가 state와 Task 모두 유지, 오래된 turn_context의 model/cwd 역행, metadata null 덮어쓰기, timestamp가 같은 순차 이벤트, 같은 ID의 source 열거 순서 반전.

### R7 — 작은 미래 clock skew도 상태 이벤트를 차단함

**심각도:** P2. **위치:** `LogParser.cs:57–59`, 관련 순서 검사 `Models.cs:44`.

**실패 시나리오 / 재현:** fileTime=12:00:10에서 timestamp=12:04:00인 task_started를 적용한 뒤 timestamp=12:00:10인 task_complete를 적용했다. 미래 시각이 5분 허용 범위에 들어가 그대로 LastEvent가 됐고, 정상 완료는 오래된 이벤트로 버려져 상태가 Thinking에 남았다. clock 보정 뒤 몇 분간 tool/완료/질문 전이가 차단될 수 있다.

**작은 수정안:** 순서 판정에 사용할 이벤트 시각은 읽은 파일의 관측 시각을 넘지 않게 제한한다. 파일 metadata는 읽기 뒤 refresh하여 오래된 FileInfo snapshot으로 정상 이벤트를 과도하게 clamp하지 않게 한다. 원본 timestamp가 필요하면 별도로 보존한다.

```diff
- if (time > fileTime.AddMinutes(5)) time = fileTime;
+ if (time > fileTime) time = fileTime;
```

**누락 테스트:** +1s/+4min/+6min 미래, 미래 이벤트 다음 정상 완료, 서로 다른 UTC offsets이 같은 instant인 경우, 시스템 clock의 전후 보정.

offset 없는 입력의 호환성도 명시해야 한다. 재현에서 `03/10/2026 12:00:00`은 en-US에서 3월 10일, en-GB에서 10월 3일이 됐고, offset 없는 ISO는 이 머신의 +09:00을 사용했다. 현재 fixture/Antigravity 조사 예시는 offset이 있으므로 이것을 현행 로그의 timezone 버그로 단정하지 않는다. fallback을 허용한다면 `InvariantCulture`와 공급자별 명시적인 offset 정책을 사용한다. [DateTimeOffset 공식 parsing 규칙](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.parse?view=net-10.0)

### R8 — 병렬 Claude 도구 배열에서 질문을 놓침

**심각도:** P2. **위치:** `LogParser.cs:157–159`.

**실패 시나리오 / 재현:** assistant content에 `AskUserQuestion`, `Read` 순서의 두 tool_use를 넣으면 Working/Read가 된다. 배열 순서를 뒤집으면 Waiting이다. 같은 미완료 질문을 순서에 따라 다르게 표시한다. 이 입력은 합성 재현이며 현행 설치본에서 질문+일반 도구의 동시 출현을 관찰하지 않았다.

**작은 수정안:** 배열 안의 사용자 질문을 먼저 검사하고, 없을 때 기존 last-tool 선택을 사용한다. 질문이 반환될 때까지의 정확한 처리는 `tool_use.id`/`tool_result.tool_use_id`로 미완료 질문만 유지하면 된다. 일반 결과 하나가 도착했다고 다른 미완료 질문까지 해제하면 안 된다.

```csharp
var question = tools.FirstOrDefault(t =>
    (String(t, "name") ?? "").Contains("AskUserQuestion", StringComparison.OrdinalIgnoreCase));
if (question.ValueKind != JsonValueKind.Undefined) Tool(a, String(question, "name")!, time);
else if (tools.Length > 0) Tool(a, String(tools[^1], "name") ?? "tool", time);
```

**누락 테스트:** 같은 배열의 두 순서, 질문과 병렬 도구 결과의 서로 다른 도착 순서, 실제 질문 결과 뒤 정상 진행, `Agent`/`Task` 실패 결과, background Task 완료 뒤 부모의 계속되는 작업.

### R9 — 공유 위반 때 이미 알려진 agent의 활동 시각까지 잃음

**심각도:** P2. **위치:** `AgentTracker.cs:76–96`.

**실패 시나리오 / 재현:** 최초 로그를 읽은 뒤 writer를 `FileAccess.Write, FileShare.Write`로 열어 최근 append와 mtime을 만들었다. GoneAfter=10s 설정에서 11s 뒤 poll하면 파일 mtime은 12:00:21인데 agent LastSeen은 12:00:10에 남아 Gone이 된다. warning은 IOException이다.

`JsonlTail.cs:20`의 `FileShare.ReadWrite | FileShare.Delete` 자체는 올바르다. 공유를 허용한 writer와 함께 읽기/재개는 성공했다. reader의 공유 flags로 기존 writer의 read 거부를 우회할 수는 없다. 따라서 문제는 flags 누락이 아니라 오류 경로에서 이미 관측한 mtime까지 버리는 것이다. [Windows CreateFile 공유 규칙](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)

**작은 수정안:** 기존 identity가 있는 source의 LastSeen은 신뢰할 수 있는 최근 metadata로 갱신하고, parse 실패와 별도로 유지한다. offset/length 캐시는 성공적으로 읽었을 때만 전진시켜 다음 poll에서 재시도한다. 미발견 identity를 파일명만으로 임의 spawn하지 않는다.

```csharp
// Tail.Read 전에 기존 개체의 liveness만 갱신한다. source.Modified/Length는 여기서 바꾸지 않는다.
if (source.AgentId != null && agents.TryGetValue(source.AgentId, out var known))
    known.LastSeen = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) > known.LastSeen
        ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) : known.LastSeen;
```

**누락 테스트:** compatible writer, read 거부 writer, 거부 중 여러 append 후 해제하여 미읽은 행을 정확히 한 번 소비, 일시적인 missing/access denied에서 이전 정상 상태 유지. 삭제된 파일은 새 mtime으로 갱신하지 않는다.

### R10 — Antigravity mtime 우선순위와 재활성화의 StateSince 오류

**심각도:** P2. **위치:** `AgentTracker.cs:107–118`, 관련 상태 계산 `Models.cs:46–58`.

**실패 시나리오 / 재현:** 12:00:00 DONE 최종 transcript 뒤 main DB mtime만 12:00:10으로 두면 Waiting이 Working으로 덮인다. 짧은 working window 뒤 Idle로 내려갔다가 DB mtime만 다시 갱신하면 Working으로 올라간다. 실제 새 작업 이벤트는 없었다. 기존 조사 문서의 idle 대화도 transcript 완료 뒤 main DB 시각이 약 30s 늦고, summary/SHM/annotation 시각은 더 늦었다(`antigravity-notes.md:450–465`).

재활성화 때 elapsed도 잘못된다. Idle의 StateSince=12:00:13 이후 12:00:14 mtime으로 Working에 복귀했는데 StateSince는 12:00:13 그대로였다. timeout이 DisplayState만 바꾸고 EventState는 Working으로 남기 때문에 `State(Working)`의 `EventState != state` 검사에서 새 시작 시각을 놓친다. 이 StateSince 결함은 Codex/Claude의 동일 상태 재개에도 적용된다.

**작은 수정안:** 새 parsed completion/사용자 대기 및 최신 명시 IDLE을 mtime 추정보다 우선한다. DB 활동만 있는 개체는 관측된 DB/WAL **변경**을 추정 신호로 사용하고 bestEffort를 유지한다. 다음 SQLite/runtime 통합 단락의 명시 상태가 있으면 mtime으로 덮지 않는다. 동일 semantic state의 timeout 복귀는 다음 한 줄로 바로 고칠 수 있다(제안만 하며 동시 편집 파일은 수정하지 않았다).

```diff
- if (EventState != state) StateSince = time;
+ if (EventState != state || DisplayState != state) StateSince = time;
```

DB 경로의 단기 보완은 최신 parsed Waiting/Reporting을 무조건 Working으로 덮는 `:118` 조건을 제한하는 것이다. 다만 terminal transcript가 오래된 경우 실제 새 작업을 막을 수 있으므로, 최종 해결은 신호별 시각/우선순위를 분리하는 것이다. unchanged mtime의 반복 poll만으로 flap한다는 주장은 하지 않는다. 이 재현에는 mtime 변화가 필요했다.

**누락 테스트:** explicit IDLE+최근 WAL, 최종 응답 뒤 main DB maintenance, 빈 WAL/SHM/summary/view만 변경, 새로운 USER_INPUT의 실제 재활성화, Working→timeout Idle→Working의 StateSince, 반복 unchanged poll, Reporting→Idle 뒤 새 완료의 reporting 창.

### R11 — 큰 행 처리 뒤 MemoryStream capacity가 source마다 남음

**심각도:** P2(최근 source가 많을 때). **위치:** `JsonlTail.cs:16`, `JsonlTail.cs:61`, source 보존 `AgentTracker.cs:42`.

**실패 시나리오 / 재현:** 기존 tail에 2MiB−1 bytes 행과 LF를 append해 처리했다. 반환 뒤 pending.Length=0인데 Capacity=2,097,152였다. `SetLength(0)`은 버퍼를 해제하지 않는다. 이런 source 1,000개라면 pending 버퍼만 약 2GiB가 된다(단일-source 실측의 산술 확장; 1,000개 workload 자체를 측정한 것은 아님). 기본 source 보존은 마지막 쓰기 뒤 약 31분이다.

**작은 수정안:** 큰 행을 **완료/폐기하고 Length=0이 된 시점**에만 retained capacity를 작은 수준으로 낮춘다. 현재 미완성 행의 bytes를 버리면 안 된다.

```csharp
pending.SetLength(0);
if (pending.Capacity > 64 * 1024) pending.Capacity = 64 * 1024;
```

newline/reset/oversize 폐기 경로에 적용하면 된다. 큰 partial 자체에 필요한 최대 2MiB와 한 번 반환하는 행 목록의 메모리는 별개의 유한 상한이며, 이를 무제한 누수라고 부르지 않는다.

**누락 테스트:** 큰 완성 행 뒤 retained capacity, oversize 폐기 뒤 recovery, 큰 partial은 유지, reset 후 오래된 큰 capacity 해제, source prune 뒤 객체 수.

### R12 — archive 전체를 3초마다 열거함

**심각도:** P2(많은 세션). **위치:** `AgentTracker.cs:35–37`, `AgentTracker.cs:52–65`, Antigravity brain에서도 `AgentTracker.cs:121`.

**실패 시나리오 / 재현:** 오래된 Codex 파일 10,000개를 날짜 하위 디렉터리에 만들었다. 3번 poll의 열거 비용은 76.5/50.6/40.0ms, 호출 스레드 할당량은 5,408,232 / 5,341,304 / 5,275,744 bytes였다. 빈 이력은 1.08/0.95/0.64ms 및 약 3.7KiB였다. 두 경우 모두 BytesRead=0으로 old 파일 **내용**은 제대로 건너뛴다. 문제는 각 scan의 recursive enumeration+FileInfo 배열이다. 실측을 실제 설치본의 latency 보장이나 cold disk 성능으로 해석하지 않는다.

**국소 수정안:** 우선 `.ToArray()`를 없애 최근/추적된 candidate만 materialize하여 할당을 줄인다. lazy enumeration의 IOException/UnauthorizedAccessException은 소비하는 foreach 경계에서 잡아야 한다. 현재 try 안에서 단순히 lazy enumerable만 반환하면 이후 예외가 catch 밖으로 빠진다. steady state는 알려진 source 및 작은 watcher dirty-path 집합을 poll하고, 전체 discovery는 시작/느린 rescan/overflow 복구 때로 제한한다. 오래된 날짜 폴더를 영구 제외하면 그 폴더의 resumed session을 놓치므로 날짜 cutoff만으로 해결하지 않는다.

이는 파일당 새 추상화나 DB를 요구하지 않는다. candidate 집합과 native FileSystemWatcher를 기존 tracker에 국소 추가하는 정도다. 최초 전체 발견의 metadata 비용은 남으며, 그것과 R2의 bounded content bootstrap은 다른 문제다.

**누락 테스트:** 10k archive에서 정상 poll의 candidate 수/할당, 오래된 폴더에 새 append한 재개 감지, inaccessible subtree, watcher duplicate/overflow 후 복구. 시간 임계값 테스트보다는 실제 열린 파일/열거 횟수의 검사를 우선한다.

### R13 — 출력 경로 구분자가 단일 writer 잠금을 우회함

**심각도:** P2. **위치:** `src/Vibedotcom.Tracker/Program.cs:33–36`.

**실패 시나리오 / 재현:** 동일한 temp 디렉터리 `output`과 `output\`은 `StateJson.OutputPath` 이후에도 서로 다른 문자열이다. Program의 그대로인 hash 식으로 계산하면 두 mutex 이름이 달라지고, 둘 다 `new Mutex(true, name, out created)`에서 created=true였다. 따라서 두 Tracker가 같은 atlas/state 목적지에 쓸 수 있다. 파일별 atomic move가 있어도 두 프로세스의 sequence/poses를 번갈아 읽는 문제는 막지 못한다. 두 Tracker 프로세스를 동시에 실행한 실험은 아니며, 같은 잠금 생성 코드의 두 mutex로 재현했다.

**작은 수정안:** validation 뒤, hash와 모든 실제 출력에 사용하는 경로의 끝 구분자를 정규화한다.

```diff
- output = StateJson.OutputPath(output, options.Home);
+ output = Path.TrimEndingDirectorySeparator(StateJson.OutputPath(output, options.Home));
```

**누락 테스트:** 끝 구분자 유무/대소문자/`..` 정규화가 같은 mutex, 서로 다른 디렉터리는 독립, 둘째 writer 거부. reparse/8.3 alias 전체를 이 한 줄이 해결한다고 주장하지 않는다.

## Antigravity SQLite/WAL 통합 방향

현재 `AgentTracker.cs:100–127`은 SQLite를 열지 않고 DB/WAL mtime와 brain JSONL만 읽는다. 따라서 현재 코드에 immutable 연결 또는 잘못된 SQL query가 있다고 보고하면 오진이다. 아래는 기존 조사 문서에 따라 통합할 때 필요한 경로이며, R10의 추정 상태 한계를 줄인다.

### WAL/immutable 실험

Windows `winsqlite3.dll`의 SQLite 3.51.1로 **temp의 합성 DB만** 열었다. WAL mode에서 IDLE을 checkpoint한 뒤 RUNNING/DONE을 WAL에 commit하고 writer를 열린 상태로 유지했다.

| 읽기 경로 | 관측 값 | quick_check |
|---|---|---|
| temp 원본의 일반 READONLY 연결, 첫 commit 뒤 | RUNNING | 별도 실행하지 않음 |
| 움직이는 같은 temp 원본의 `immutable=1` | IDLE | 별도 실행하지 않음 |
| 그 immutable 연결에서 두 번째 commit 뒤 조회 | IDLE | 별도 실행하지 않음 |
| main DB만 복사한 READONLY 연결 | IDLE | ok |
| main + WAL + SHM shared-read 복사본, 두 번째 commit 뒤 | DONE | ok |

immutable은 locking/change detection을 생략한다. 따라서 움직이는 원본에 적용하면 안 된다. 일반 READONLY는 이 합성 실험에서 WAL 최신 값을 읽었지만, 이것이 실제 공급자 SHM에 쓰지 않는다는 보장은 아니다. 실제 공급자에는 직접 SQLite 연결하지 않고 copy 경로를 쓴다. [SQLite URI immutable 설명](https://www.sqlite.org/uri.html#uriimmutable), [SQLite WAL/read-only 설명](https://www.sqlite.org/wal.html)

main-only 복사도 quick_check=ok였지만 최신 committed 값을 놓쳤다. DB와 WAL의 순차 복사는 atomic snapshot이 아니며 quick_check로 의미적 일관성까지 보장할 수 없다. 쓰기가 계속되는 workload의 일관성/복사 경쟁은 이번 합성 실험에서 재현하지 않았고, SQLite 공식 제약에 근거한다. [SQLite의 active transaction 중 파일 복사 설명](https://www.sqlite.org/howtocorrupt.html#_backup_or_restore_while_a_transaction_is_active)

### 작은 단계의 통합 제안

1. **요약 metadata부터 추가:** 바뀐 `conversation_summaries.db`와 존재하는 `-wal/-shm/-journal`을 동일 이름으로 전용 temp 디렉터리에 shared-read 복사한다. 복사 전후 length/mtime가 바뀌면 제한된 횟수로 재시도한다. 불일치/error이면 이전 정상 cache를 유지한다. 공급자 원본의 checkpoint, journal mode 변경, 복구 SQL은 실행하지 않는다. copied DB에 READONLY로 연결하고 `quick_check`와 schema/user_version을 확인한다. 기존 의존성이 있으면 `Mode=ReadOnly;Pooling=False`, 없으면 조사 문서와 같은 작은 native wrapper가 후보다.
2. **현재 필요한 column만 query:** `conversation_id`, `title`, `parent_conversation_id`, `nesting_depth`, `agent_name`, `workspace_uris`, `status`, `not_fully_idle`, `killed`, input/modified 시각을 읽는다. source별 관측 시각과 실제 활동 시각을 분리한다. IDLE이 확인되면 더 늦은 maintenance mtime로 Working을 덮지 않는다. 오래된 저장 RUNNING도 stale 정책을 통과해야 한다. CLI mirror는 같은 conversation ID와 `app_data_dir`로 합치고 별도 agent를 만들지 않는다.
3. **표시할 대화의 step만 보완:** DB가 실제 변경된 대화에서 끝부분과 nonterminal step을 읽는다. `idx > lastIdx`만 사용하면 같은 idx의 RUNNING→DONE을 놓친다. transcript도 conversation+step_index로 upsert하고 같은 크기 수정은 끝부분의 제한된 재읽기/DB 상태로 보완한다. 전체 trajectory/BLOB를 poll마다 읽지 않는다. SQL sentinel year 0001, 빈 workspace TEXT, 입력 idx=-1을 unknown으로 처리한다.
4. **runtime read API를 우선 신호로 추가:** 로그 끝에서 최신 loopback port를 발견하고 응답을 확인한다. CSRF는 메모리에만 둔다. `GetAllCascadeTrajectories`의 loaded map과 `JetboxSubscribeToSummaries`의 전체 카탈로그를 구분한다. 초기 catalog 수신 자체는 새 활동이 아니며 과거 대화 전부를 spawn하지 않는다. 필요한 loaded conversation에만 `StreamAgentStateUpdates`, `disableRehydration=true`, bounded page를 쓴다. 초기 snapshot과 부분 update를 구분하고 누락 필드로 cache를 무조건 비우지 않는다.
5. **Connect framing을 별도로 처리:** HTTP read가 envelope 경계라는 가정 없이 1-byte flags+4-byte big-endian length와 payload를 `ReadExactlyAsync`로 받는다. 길이 상한, end-stream/error, reconnect/cancel을 처리한다. 초기 재접속 수신을 activity로 세지 않는다. [Connect 공식 protocol](https://connectrpc.com/docs/protocol/)

`fullyIdle`, 최신 busy/step 상태, 실제 사용자 질문 근거가 우선이며 mtime은 explicit 상태가 없을 때의 추정 fallback이다. 정상 empty/deleted loaded 응답과 통신 실패를 구분하여 실패 한 번으로 전원 Gone을 만들지 않는다. DB 존재/PID/SHM/annotation view는 working 또는 영구 alive 근거가 아니다. `rootConversationId=self`, fork/battle/subtrajectory/reference도 자식 conversation과 구분한다.

Antigravity active push 주기, working/thinking/waiting/reporting 및 자식 생성 지연은 기존 문서에서도 미측정이다. 이번 리뷰는 이를 측정하거나 새로운 작업을 공급자에게 보내지 않았다. 통합 이후 실제 사용자 작업에서 state 수신/파일 변화/완료 시각을 함께 관찰해야 해당 성능을 주장할 수 있다.

## 확인된 정상 동작과 나머지 테스트 공백

- **Partial last line / UTF-8:** LF가 없으면 완성 JSON처럼 보여도 방출하지 않는 것이 live tail에 맞다. 한글을 16KiB read 경계 및 두 번의 file write 사이에서 잘랐을 때 첫 read는 0행, 다음은 정확한 원문 1행, 다음 empty read는 추가 bytes=0이었다. offset=16,392로 실제 bytes와 같았다. 매번 파일을 reopen해도 정상 append의 offset은 유지됐다.
- **20MB+ cold start:** 26,214,435-byte 합성 파일에서 첫 read는 정확히 1,048,576 bytes이며 prefix metadata와 마지막 recent 행을 모두 읽었다. 실행 시간은 이 단일 실험에서 66.9ms였다. 파일 전체를 읽는 버그는 없었다. 다만 prefix와 suffix 사이에만 존재하는 최신 user/context는 복원되지 않을 수 있다. 이를 관측하지 못한 metadata로 구분하고, 필요하면 공급자 index/요약으로 보완해야 한다. bounded bootstrap으로 모든 과거 metadata를 보장한다고 말하지 않는다.
- **기존 truncation/oversize 검사:** 눈에 보이는 작은 길이 축소는 reset했고, 기존 테스트의 oversize 후 recovery도 통과했다. 문제는 R1의 poll 사이 축소·재성장이다. 작은 파일 최초 read/큰 append의 별도 byte budget도 있으므로 모든 read를 무조건 1MiB 상한이라고 해석하지 않는다.
- **상태 안정성:** unchanged 파일 poll, Waiting의 idle timeout 면제, Reporting의 별도 창은 기존 테스트가 다룬다. 실제 새 동일 상태 이벤트의 timeout 복귀 시각은 R10의 공백이다. 단순히 poll한다고 Working↔Idle이 왕복하는 결함은 재현하지 않았다.
- **Codex spawn failure:** 현재 parser는 spawn 호출만으로 자식을 만들지 않아 transcript가 없는 실패에 phantom child를 만들지 않는다. top-level/nested metadata parent 연결은 기존 fixture로 동작한다. `spawn_agent`/`wait` 결과를 call_id로 연결하는 fallback은 구현되어 있지 않다. 이는 부모 metadata가 충분할 때 당장 버그라는 뜻은 아니다. 성공 결과의 child ID가 존재하고 metadata parent만 누락된 경우, 실패/취소/timeout 결과, child가 먼저 발견된 경우, 오래된 부모 prune 뒤 orphan, 두 부모의 같은 call ID를 테스트해야 한다. fallback을 추가한다면 성공이 확인된 ID만 연결하고 pending map의 TTL/크기를 제한한다.
- **Claude Task/Agent:** tool 이름은 agent kind로 분류되지만 호출/결과에서 role/task/parent를 복원하지 않는다. 디렉터리로 parent를 아는 worker는 동작한다. Task/Agent 실패 결과가 worker completion으로 오인되지 않는지, background task 알림이 부모의 final answer로 오인되지 않는지, user tool_result가 task를 덮지 않는지 추가로 검사해야 한다. `parentUuid`를 parent session으로 사용하는 우회는 금지한다.
- **시간 공백:** UTC/양·음 offset, 나노초 RFC3339 및 SQL offset 문자열, offset 없는 timestamp의 정책, invalid/숫자 timestamp fallback, clock 보정 후 `nextScan` scheduling을 검사해야 한다. 현재 SQL timestamp parsing은 아직 통합되지 않아 그 구현 결함을 주장할 수 없다.
- **SQLite/runtime 공백:** WAL-only commit, main-only 복사가 stale인데 quick_check가 ok인 경우, 복사 도중 변화/실패의 cache 유지, zero WAL/SHM/view-only, 동일 idx status 갱신, explicit IDLE 우선, stale RUNNING, unknown enum/schema, 초기 catalog와 loaded set의 차이, 중복 CLI mirror, Connect header/payload의 임의 chunk 분할, reconnect 시 phantom activity를 검사해야 한다.
- **Tracker output:** 기존 atomic snapshot/PNG 테스트는 통과했다. R13의 동시 writer 조건과 transient output 공유 실패 후 재시도는 CLI 테스트에 없다. output 오류 때 프로세스를 종료하는 현재 정책이 소비자의 disconnected 표시와 맞는지도 확인할 대상이다.

## 수행한 검증과 재현 위치

임시 루트:

```text
%TEMP%\Vibedotcom-core-review-c9874a1fa5094a26b542e1a565871ca6
```

- `Repro.cs` / `Review.csproj`: 실제 Core 소스 복사본을 직접 compile하여 UTF-8, bootstrap, overwrite, Claude 혼합/병렬 도구, clock, prune/reopen, mtime, 버퍼, 10k archive, 공유 모드를 검사했다.
- `ExtraRepro.cs` / `ExtraReview.csproj`: 정확히 생성 시각을 보존한 교체, Tracker fast path, live 공유 거부, 경로별 mutex, 오래된 metadata를 검사했다. 결과는 `extra-results.txt`에도 남아 있다.
- `SqliteRepro.cs` / `SqliteReview.csproj`: synthetic WAL/immutable/copy 실험. 실제 `.gemini` DB에는 연결하지 않았다.
- `ReviewTests.csproj`: 기존 `CoreTests.cs`/fixture 및 Core 복사본을 temp에서 compile하고 xUnit **19/19 통과**(failed=0, skipped=0). package는 저장소의 기존 cache를 temp로 복사하고 source를 비운 NuGet config로 사용했다.
- `TrackerReview.csproj`: Tracker 소스 복사본 build 및 `--once --home <temp empty home> --output <temp>` 성공, snapshot 1개. empty home의 provider count는 모두 0이며 실제 머신의 agent 수를 측정한 결과가 아니다.

재실행은 아래를 **순서대로** 한다(임시 프로젝트들이 같은 bin/obj를 사용한다). 스크립트의 데이터 디렉터리는 새 GUID를 사용한다.

```powershell
$reviewRoot = Join-Path $env:TEMP 'Vibedotcom-core-review-c9874a1fa5094a26b542e1a565871ca6'
dotnet run --project (Join-Path $reviewRoot 'Review.csproj') --verbosity quiet
dotnet run --project (Join-Path $reviewRoot 'ExtraReview.csproj') --verbosity quiet
dotnet run --project (Join-Path $reviewRoot 'SqliteReview.csproj') --verbosity quiet
dotnet restore (Join-Path $reviewRoot 'ReviewTests.csproj') --configfile (Join-Path $reviewRoot 'NuGet.Config') --verbosity quiet
dotnet test (Join-Path $reviewRoot 'ReviewTests.csproj') --no-restore --verbosity minimal
```

리뷰·재현 소스의 SHA-256:

| 파일 | SHA-256 |
|---|---|
| AgentTracker.cs | `008B697BB72FC60A7D35E9B611423F19378BB774E980CEEDECAC34FEF51FD484` |
| JsonlTail.cs | `6679F0D4EF0BF607B9D3E75DC4C53A6047E5AC7603520ED2C3875BDB7E1794FB` |
| LogParser.cs | `942E93B3D8428054C3C1FF8142FF0AAB20A81E8287D3A892CA1E961E72A39C16` |
| Models.cs(재현용 복사본) | `670801A445EF13CCEEA6F8A22BE0E3797D16B2B4BF69C49363E1B6222B108C0D` |
| Tracker Program.cs(재현용 복사본) | `46FB8534626BCF5AC902884CF62153A6662D1C66F2A8F98C7DCB0A47259F24EB` |
| CoreTests.cs | `B349B8E1AC1DE8B92073E6F5B0AC4670DCA057352A6EC17641604394E1CB3CBD` |

마지막 확인에서 대상 Core 파일 3개와 CoreTests의 hash는 재현 복사본과 같았다. 동시 작업으로 Models/Program/프로젝트 구성은 이후 변경됐다. Models의 State()/Snapshot()과 Program의 mutex 관련 행을 다시 읽어 해당 문제가 남아 있음을 확인했다. **19/19는 검증 시점 복사본의 결과이며 진행 중인 최신 작업 트리 전체의 build/test 성공을 뜻하지 않는다.** 최신 src에서의 build/test, WPF 화면, Windhawk, 실제 공급자 실행 검증은 이 리뷰에서 수행하지 않았다.
