# Antigravity 활동 감지 조사

조사일: 2026-10-03. Windows 11, 이 머신의 Antigravity **2.19.1** 기준. `SPEC.md`를 읽고 저장 파일, 설치된 프로그램의 정적 코드, 실행 프로세스, localhost의 조회 API를 조사했다. 프로젝트에서는 이 문서만 작성했다. `~/.gemini`와 `%APPDATA%/Antigravity`에는 직접 쓰기, SQLite 연결, 체크포인트, 설정 변경을 하지 않았다. SQLite는 기존 WAL/SHM과 함께 `%TEMP%`로 복사한 뒤 복사본만 열었다. 예시의 대화 ID, 제목, 프롬프트, 사용자 경로는 익명화했다.

**권장: 실행 중인 언어 서버의 Connect 조회/구독 API를 우선하고, SQLite 요약과 대화별 변경을 보조 신호로 사용한다.** 이 설치본에서 실제 HTTP 200과 초기 구독 메시지를 확인했다. 파일 mtime만으로 작업 상태를 판정하는 것보다 직접적인 상태 필드가 있다. 다만 내부 API여서 버전 호환성은 보장할 수 없다.

**중요한 측정 제한:** 조사 중 확인한 IDE 대화는 모두 idle이었다. mtime을 약 3분, 요약 스트림을 별도로 3분 관찰했지만 실제 작업 중인 에이전트는 확인하지 못했다. 작업을 시작하거나 사용자 세션에 메시지를 보내지 않았다. 따라서 **활성 작업 중 저장 주기, working→idle 전이 지연, 하위 에이전트 생성 지연은 미측정**이다. 아래에는 관측한 사실과 스키마에서만 확인한 기능을 구분했다.

## 1. 이 머신에서 확인한 데이터 범위

`AG = %USERPROFILE%/.gemini/antigravity`, `CLI = %USERPROFILE%/.gemini/antigravity-cli`.

| 위치 | 확인한 내용 | 적합한 용도 |
|---|---|---|
| `AG/conversation_summaries.db` | SQLite, `user_version=3`, WAL, 요약 65행 | 대화 ID·제목·workspace·저장 상태·부모 필드 |
| `AG/conversations/<conversation_id>.db` | DB 65개 및 각 WAL/SHM, 조사한 3개는 `user_version=1`, WAL | step 상태, 모델, 도구, protobuf 메타데이터 |
| `AG/brain/<conversation_id>/` | 대화 디렉터리 65개, `transcript.jsonl` 65개 | 대화별 활동 및 내용 보조 신호 |
| `AG/annotations/<conversation_id>.pbtxt` | 65개, 제목·열람·보관 필드 | 표시 제목 및 보관 여부; 작업 신호로는 부적합 |
| `AG/sidecar_data/` | 예약 작업 2개, JSON 이벤트 45개, 로그 39개 | 예약 작업이 만든 대화 ID 연결 |
| `AG/antigravity_state.pbtxt` | 온보딩·마이그레이션·설치 ID | 현재 작업 상태를 제공하지 않음 |
| `AG/agyhub_summaries_proto.pb` | 별도 집계 protobuf 파일 존재 | 존재만 확인; 이번 알고리즘에는 불필요 |
| `CLI/conversation_summaries.db` | SQLite, `user_version=3`, WAL, 요약 60행 | IDE 대화의 오래된 중복 카탈로그였음 |
| `CLI/conversations/`, `CLI/brain/` | 조사 시 파일 0개 | 이 머신에서는 별도 CLI 대화 근거 없음 |
| `CLI/log/cli-YYYYMMDD_HHMMSS.log` | 약 1분마다 새 실행 로그 | CLI 프로세스 시작·workspace·모델 설정 참고 |
| `%APPDATA%/Antigravity/logs/language_server.log` | 언어 서버 포트, 요청 ID, 백그라운드 경고 | 포트 발견 및 제한적인 요청 활동 |
| `%APPDATA%/Antigravity/logs/main.log` | Electron 수명주기, 포트 변경, 업데이트 확인 | 서버 재시작/포트 발견 |

65개의 저장 대화와 현재 로드된 대화를 구분해야 한다. 대화 DB의 존재, WAL/SHM의 존재, 프로세스의 존재만으로 현재 작업 중인 에이전트라고 판단하면 안 된다.

## 2. 실행 중인 서버와 조회 API — 실제 확인

### 발견 방법과 프로세스

읽기 전용 `Win32_Process` 조회에서 다음을 확인했다. 기본 샌드박스의 WMI는 접근이 거부됐고, 읽기 전용 권한 확장으로 확인했다. 대상 앱 자체에서는 같은 권한 제한이 발생할 수 있으므로 로그를 우선적인 발견 수단으로 둔다.

| PID / 부모 PID | 역할 | 관측 |
|---|---|---|
| `11404 / 1176` | `Antigravity.exe` 메인 | Electron; host bridge `127.0.0.1:62919` |
| `20676 / 11404` | `language_server.exe` | `--standalone --subclient_type hub --app_data_dir antigravity` |
| `24424 / 20676`, `31212 / 20676` | 같은 executable의 예약 작업 | `multicall schedule ... agentapi new-conversation <prompt>` |
| 그 외 예약 프로세스 4개 | 이전 서버의 예약 작업 | 조사 당시 별도로 남아 있었음 |
| renderer/GPU/utility 프로세스 | Electron 구성 요소 | 에이전트/하위 에이전트로 세면 안 됨 |

현재 서버는 **HTTPS 62920**, **HTTP 62921**에서 loopback으로 수신했다. 포트는 재시작할 때 바뀐다. `--https_server_port 0`이므로 실행 인수의 0을 실제 포트로 쓰면 안 된다. 이전 예약 프로세스의 부모 PID 관계 역시 대화의 부모 관계가 아니다.

정확한 발견 로그 예시:

```text
[info] Starting app (v2.19.1) with dynamic port…
[info] Host bridge server listening on http://127.0.0.1:62919
[info] [Auto-Restart] Port changed! Reloading all windows with URL: https://127.0.0.1:62920/
I1003 20:10:44.595573 ... server.go:631] Language server listening on random port at 62920 for HTTPS (gRPC)
I1003 20:10:44.596094 ... server.go:639] Language server listening on random port at 62921 for HTTP
```

로그 벽시계는 KST, 아래 측정과 DB 예시는 UTC다. prefix의 `ERROR: logging before google.Init:` 뒤에 `I...`, `W...`도 나오므로 prefix 자체를 실패나 에이전트 오류로 해석하지 않는다.

### 인증과 전송

`GET http://127.0.0.1:62921/`는 HTML을 반환했다. `window.__APP_CONFIG__`의 필드는 정확히 `productName`, `csrfToken`, `appVersion`, `releaseChannel`, `devMode`였다. **`csrfToken` 값은 메모리에서만 사용하고 문서/상태 JSON/로그에 기록하지 않는다.** 프런트엔드도 `x-codeium-csrf-token` 헤더를 사용한다. 프로세스 인수의 `--csrf_token`, `--host_bridge_token` 값도 민감하며 출력하면 안 된다.

검증한 서비스 prefix:

```text
http://127.0.0.1:<HTTP port>/exa.language_server_pb.LanguageServerService/
```

unary 조회는 `POST`, `Content-Type: application/json`, `Connect-Protocol-Version: 1`, CSRF 헤더를 사용했다. 스트림은 `Content-Type: application/connect+json`이었다. HTTP 조회/구독만 사용했으며 `Send`, `Start`, `Write`, `Delete`, `Cancel` 계열 메서드는 호출하지 않았다.

이 머신에는 정상 동작하는 HTTP listener가 있어 HTTPS 인증서 우회가 필요 없다. HTTPS를 사용하는 다른 설치본에서는 해당 loopback 서버만 검증하는 별도 처리가 필요하며 전역 인증서 검증 해제를 권장하지 않는다.

### 확인한 메서드와 결과

| 메서드 | 요청 | 관측 결과 |
|---|---|---|
| `GetAllCascadeTrajectories` | `{"excludeSubtrajectories":false}` | HTTP 200, 806 bytes, `trajectorySummaries` map에 **1개** |
| `JetboxSubscribeToSummaries` | `{}`의 Connect streaming envelope | HTTP 200, 초기 `updates` map에 **65개**; 213,746-byte JSON |
| `StreamAgentStateUpdates` | 아래 예시 | HTTP 200, 초기 `update`; 상태와 범위 지정 step/모델 설정 확인 |

`GetAllCascadeTrajectories`와 요약 구독은 같은 카탈로그가 아니다. 이 설치본에서 전자는 **현재 서버에 로드된 대화 목록에 가까운 신호**였고, 후자는 과거 저장 대화 전체를 포함했다. 전자의 1개가 UI에서 열린 모든 탭을 정확히 뜻하는지는 검증하지 않았다. 로드된 idle 대화도 실제로 반환되므로 `GetAll...`에 들어 있다는 이유로 working으로 표시하면 안 된다.

익명화한 unary 응답의 주요 필드:

```json
{
  "trajectorySummaries": {
    "11111111-1111-4111-8111-111111111111": {
      "summary": "[redacted conversation title]",
      "stepCount": 24,
      "lastModifiedTime": "2026-10-03T11:33:59.807451500Z",
      "trajectoryId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      "status": "CASCADE_RUN_STATUS_IDLE",
      "lastUserInputTime": "2026-10-03T11:33:25.796789600Z",
      "lastUserInputStepIndex": 16,
      "annotations": {
        "title": "[redacted conversation title]",
        "lastUserViewTime": "2026-10-03T11:50:36.881Z"
      },
      "trajectoryMetadata": {
        "rootConversationId": "11111111-1111-4111-8111-111111111111",
        "projectId": "outside-of-project"
      }
    }
  }
}
```

이 map의 key가 conversation/cascade ID다. `trajectoryId`와는 별개이며, 개체 식별에는 map key를 쓴다. `rootConversationId`가 자기 자신인 최상위 대화를 부모가 있는 대화로 처리하지 않는다.

스트림 요청의 필드명은 JSON에서는 camelCase, protobuf에서는 snake_case다.

```json
{
  "conversationId": "11111111-1111-4111-8111-111111111111",
  "subscriberId": "officelings-<unique instance>",
  "initialStepsPageBounds": { "startIndex": 22, "endIndexExclusive": 24 },
  "initialGeneratorMetadatasPageBounds": { "startIndex": 11, "endIndexExclusive": 12 },
  "initialExecutorMetadatasPageBounds": { "startIndex": 4, "endIndexExclusive": 5 },
  "trajectoryVerbosity": "CLIENT_TRAJECTORY_VERBOSITY_PROD_UI",
  "disableRehydration": true,
  "enableLatencyTelemetry": false
}
```

`disableRehydration=true`로 이번에 로드된 대화의 상태를 얻었다. 관찰하려고 오래된 대화를 다시 로드하여 live 집합을 늘리지 않는다. 예시의 page bounds는 실측한 step 24개, generator record 12개, executor record 5개에 해당하며 다른 대화에 고정값을 재사용하지 않는다.

실측 stream 응답:

```json
{
  "update": {
    "conversationId": "11111111-1111-4111-8111-111111111111",
    "trajectoryId": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
    "status": "CASCADE_RUN_STATUS_IDLE",
    "executableStatus": "CASCADE_RUN_STATUS_IDLE",
    "executorLoopStatus": "CASCADE_RUN_STATUS_IDLE",
    "fullyIdle": true,
    "mainTrajectoryUpdate": {
      "stepsUpdate": {
        "indices": [22, 23],
        "totalLength": 24,
        "steps": ["[omitted: GENERIC/DONE and PLANNER_RESPONSE/DONE]"]
      },
      "generatorMetadatasUpdate": {
        "indices": [11],
        "generatorMetadatas": [{
          "stepIndices": [23],
          "plannerConfig": {
            "planModel": "MODEL_PLACEHOLDER_M318",
            "modelName": "gemini-3.8-flash-high"
          }
        }]
      }
    }
  }
}
```

예시는 필드를 일부 생략한 projection이다. 처음 128,246-byte JSON을 받고 이후 5초 동안 추가 메시지는 없었다. `PROD_UI`에서 해당 generator record의 `chatModel`은 생략됐으나 `plannerConfig.modelName`을 얻었다. enum `MODEL_PLACEHOLDER_M318`을 사람이 읽는 모델명으로 쓰지 않는다.

### 스키마에서 확인한 live 필드 — 실제 미출현 필드 포함

정적 스키마의 출처는 localhost `/main.js` 안의 base64 `FileDescriptorProto`다. JS를 실행하지 않고 분석했다. 설치된 `resources/app.asar`의 `dist/languageServer.js`, `dist/main.js`도 읽어 서버 시작 방식과 동적 포트를 확인했다.

`exa.jetski_cortex_pb.CascadeTrajectorySummary`:

- `summary`、`stepCount`、`lastModifiedTime`、`trajectoryId`、`status`、`createdTime`。
- `waitingSteps[]`、`lastUserInputTime`、`lastUserInputStepIndex`。
- `latestTaskBoundaryStep.step.taskBoundary.taskName / taskStatus / taskSummary`。
- `latestNotifyUserStep`、`annotations`、`workspaces[].workspaceFolderAbsoluteUri`。
- `trajectoryMetadata`、`hasActiveChildren`、`notFullyIdle`、`killed`、`interrupted`、`hasActivity`。
- `trajectoryType`、`source`、`battleModeInfo`、`forkParentConversationId`。

`exa.jetski_cortex_pb.AgentStateUpdate`:

- `conversationId`、`trajectoryId`、`status`、`executableStatus`、`executorLoopStatus`、`fullyIdle`。
- `mainTrajectoryUpdate`、`subtrajectoryUpdates`、`stepScopedSubtrajectoryUpdates`、`hasActiveChildren`。
- `queuedStepsUpdate`、`backgroundTasksUpdate`、`backgroundCommandsUpdate`、`pendingAgentMessagesUpdate`。
- `executionStatus`, `activeHook`, `stepHooks`, `executorMetadata` 등.

`mainTrajectoryUpdate`에는 `stepsUpdate`, `generatorMetadatasUpdate`, `executorMetadatasUpdate`, `metadata`, `waitingSteps`, `fullyIdle`, `killed`, `parentReferences`, `lastStepType`이 있다.

이번 65개 초기 요약에서 `waitingSteps`, `latestTaskBoundaryStep`, `latestNotifyUserStep`, `hasActiveChildren`, `notFullyIdle`, `killed`은 **모든 행에서 JSON key 자체가 생략**됐다. protobuf JSON의 기본값 생략을 처리해야 한다. 스트림 차분에서 생략된 필드를 무조건 false/빈 값으로 초기화하지 말고, 초기 snapshot과 이후 부분 update를 구분한다. 활성 차분의 정확한 생략 규칙은 미검증이다.

## 3. SQLite의 정확한 스키마

### `conversation_summaries.db`

유일한 애플리케이션 테이블은 `conversation_summaries`다. 아래 스키마는 `sqlite_master.sql`에서 얻었다.

```sql
CREATE TABLE `conversation_summaries` (
  `conversation_id` text,
  `title` text NOT NULL DEFAULT "",
  `preview` text NOT NULL DEFAULT "",
  `step_count` integer NOT NULL DEFAULT 0,
  `last_modified_time` datetime NOT NULL,
  `workspace_uris` text NOT NULL,
  `status` text NOT NULL DEFAULT "",
  `source` text NOT NULL DEFAULT "",
  `project_id` text NOT NULL DEFAULT "",
  `agent_name` text NOT NULL DEFAULT "",
  `parent_conversation_id` text NOT NULL DEFAULT "",
  `nesting_depth` integer NOT NULL DEFAULT 0,
  `battle_id` text NOT NULL DEFAULT "",
  `winning_conversation_id` text NOT NULL DEFAULT "",
  `not_fully_idle` numeric NOT NULL DEFAULT false,
  `killed` numeric NOT NULL DEFAULT false,
  `last_user_input_time` datetime NOT NULL,
  `last_user_input_step_index` integer NOT NULL DEFAULT -1,
  `app_data_dir` text NOT NULL DEFAULT "",
  `raw_summary` blob,
  `group_id` text NOT NULL DEFAULT "",
  PRIMARY KEY (`conversation_id`)
);
CREATE INDEX `idx_conversation_summaries_last_modified_time`
  ON `conversation_summaries`(`last_modified_time`);
CREATE INDEX `idx_conversation_summaries_last_user_input_time`
  ON `conversation_summaries`(`last_user_input_time`);
```

실측 행을 익명화하고 필요한 필드만 남긴 예시:

```json
{
  "conversation_id": "11111111-1111-4111-8111-111111111111",
  "title": "[redacted conversation title]",
  "preview": "[redacted conversation title]",
  "step_count": 24,
  "last_modified_time": "2026-10-03 11:33:59.8074515+00:00",
  "workspace_uris": "",
  "status": "CASCADE_RUN_STATUS_IDLE",
  "source": "",
  "project_id": "outside-of-project",
  "agent_name": "",
  "parent_conversation_id": "",
  "nesting_depth": 0,
  "not_fully_idle": 0,
  "killed": 0,
  "last_user_input_time": "2026-10-03 11:33:25.7967896+00:00",
  "last_user_input_step_index": 16,
  "app_data_dir": "antigravity",
  "raw_summary_bytes": 306
}
```

workspace가 있는 다른 행:

```json
{
  "conversation_id": "22222222-2222-4222-8222-222222222222",
  "step_count": 40,
  "workspace_uris": "[\"file:///c%3A/Example-Workspace/Project\"]",
  "status": "CASCADE_RUN_STATUS_IDLE",
  "last_modified_time": "2026-10-03 00:16:19.5745667+00:00",
  "last_user_input_step_index": -1,
  "app_data_dir": "antigravity"
}
```

`workspace_uris`는 JSON 배열을 저장한 TEXT 또는 빈 문자열이다. 65행 중 52행에 workspace가 있었다. `last_user_input_step_index=-1`인 행에도 실제 user step이 있으므로 -1을 사용자 입력 없음으로 단정하지 않는다.

확인한 최신 행은 SQL `title == annotations.title`, SQL `preview == live summary`였다. 항상 같다는 보장은 없다. `raw_summary`를 protobuf `CascadeTrajectorySummary`로 해석하여 `step_count=24`, `status=1/IDLE`와 conversation ID와 별개인 `trajectory_id`를 확인했다.

IDE 65행 모두 `status=CASCADE_RUN_STATUS_IDLE`, `not_fully_idle=0`, `killed=0`, 부모 ID와 `agent_name`이 비어 있고 `nesting_depth=0`이었다. CLI는 같은 스키마의 60행 모두 `status=""`, `raw_summary=NULL`, `last_user_input_time="0001-01-01 00:00:00+00:00"`, `app_data_dir="antigravity"`였다. CLI 디렉터리 이름을 CLI 세션의 증거로 쓰지 말고 `app_data_dir`와 ID로 중복을 합친다.

### `conversations/<conversation_id>.db`

조사한 최신 DB 3개에서 동일한 테이블 7개를 확인했다.

```sql
CREATE TABLE `trajectory_meta` (
  `trajectory_id` text, `cascade_id` text,
  `trajectory_type` integer, `source` integer,
  PRIMARY KEY (`trajectory_id`)
);
CREATE TABLE `trajectory_metadata_blob` (
  `id` text DEFAULT "main", `data` blob, PRIMARY KEY (`id`)
);
CREATE TABLE `steps` (
  `idx` integer,
  `step_type` integer NOT NULL DEFAULT 0,
  `status` integer NOT NULL DEFAULT 0,
  `has_subtrajectory` numeric NOT NULL DEFAULT false,
  `metadata` blob, `error_details` blob, `permissions` blob,
  `task_details` blob, `render_info` blob, `step_payload` blob,
  `step_format` integer NOT NULL DEFAULT 0,
  PRIMARY KEY (`idx`)
);
CREATE INDEX `idx_steps_status` ON `steps`(`status`);
CREATE INDEX `idx_steps_step_type` ON `steps`(`step_type`);
CREATE TABLE `gen_metadata` (
  `idx` integer, `data` blob,
  `size` integer NOT NULL DEFAULT 0, PRIMARY KEY (`idx`)
);
CREATE TABLE `executor_metadata` (
  `idx` integer, `data` blob, PRIMARY KEY (`idx`)
);
CREATE TABLE `parent_references` (
  `idx` integer, `data` blob, PRIMARY KEY (`idx`)
);
CREATE TABLE `battle_mode_infos` (
  `idx` integer, `data` blob, PRIMARY KEY (`idx`)
);
```

익명화한 행:

```json
{"trajectory_id":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","cascade_id":"11111111-1111-4111-8111-111111111111","trajectory_type":4,"source":1}
{"idx":22,"step_type":132,"status":3,"has_subtrajectory":0,"step_format":0,"metadata_bytes":688,"payload_bytes":8553}
{"idx":23,"step_type":15,"status":3,"has_subtrajectory":0,"step_format":0,"metadata_bytes":361,"payload_bytes":13360}
{"id":"main","data_bytes":554}
{"idx":11,"size":325520,"data_bytes":325520}
```

최신 DB 3개는 각각 step 24/40/70개, generator record 12/20/35개, executor record 5/1/1개였다. `parent_references`와 `battle_mode_infos`는 모두 0행이다. 최신 DB의 step은 모두 DONE, 70-step DB에는 ERROR가 1행 있었다. 과거 ERROR의 존재로 현재 에이전트를 error/working으로 표시하지 않는다.

스키마에서 확인한 enum:

| enum | 값 |
|---|---|
| `CascadeRunStatus` | UNSPECIFIED=0, IDLE=1, RUNNING=2, CANCELING=3, BUSY=4 |
| `CortexStepStatus` | UNSPECIFIED=0, PENDING=1, RUNNING=2, DONE=3, INVALID=4, CLEARED=5, CANCELED=6, ERROR=7, GENERATING=8, WAITING=9, QUEUED=11, INTERRUPTED=12 |
| 주요 `CortexStepType` | USER_INPUT=14, PLANNER_RESPONSE=15, RUN_COMMAND=21, TASK_BOUNDARY=81, NOTIFY_USER=82, BROWSER_SUBAGENT=85, WAIT=102, INVOKE_SUBAGENT=127, GENERIC=132, ASK_QUESTION=138 |

이번 `GENERIC=132`에는 `search_web`이 포함됐다. 도구 종류를 숫자로만 결정하지 말고 `metadata.tool_call.name` / JSON `metadata.toolCall.name`을 읽는다.

### BLOB과 protobuf의 대응 및 모델 추출

protobuf wire 필드를 읽는다. 확인한 스키마와 필드 번호:

| 저장 위치 / message | 필드 번호와 이름 |
|---|---|
| `steps.metadata` / `CortexStepMetadata` | 1 `created_at`, 4 `tool_call`, 8 `completed_at`, 11 `generator_model`, 13 `requested_model`, 24 `model_info`, 30 `tool_summary`, 31 `tool_action`, 32 `started_at` |
| `gen_metadata.data` / `CortexStepGeneratorMetadata` | 1 `chat_model`, 2 `step_indices`, 3 `planner_config`, 4 `execution_id` |
| 위 레코드 내부 `ChatModelMetadata` | 3 `model`, 19 `response_model`, 21 `model_display_name`, 22 `response_model_full` |
| 위 레코드 내부 `CascadePlannerConfig` | 1 `plan_model`, 15 `requested_model`, 28 `model_name` |
| `executor_metadata.data` / `ExecutorMetadata` | 1 `termination_reason`, 3 `last_step_idx`, 9 `execution_id`, 10 `cascade_config` |
| `trajectory_metadata_blob.data` / `CortexTrajectoryMetadata` | 5 `parent_conversation_id`, 6 `root_conversation_id`, 7 `workspace_uris`, 8 `subagent_spec`, 17 `nesting_depth`, 18 `project_id` |
| `parent_references.data` / schema `CortexTrajectoryReference` | 1 `trajectory_id`, 2 `step_index`, 3 `trajectory_type`, 4 `step_type`, 5 `reference_type`, 6 `conversation_id` |

마지막 `parent_references`의 대응은 메시지 스키마에서 확인한 후보이며, 값이 있는 행의 디코딩은 미검증이다. `steps.task_details`도 조사한 DB 3개에서 값이 있는 행을 확인하지 못했으므로 payload의 의미를 추측하지 않는다.

최신 generator 행을 실제로 디코딩한 결과:

```json
{
  "gen_metadata.idx": 11,
  "planner_config.plan_model": 1318,
  "planner_config.model_name": "gemini-3.8-flash-high",
  "chat_model.model": 1318,
  "chat_model.response_model": "gemini-3.8-flash",
  "chat_model.model_display_name": "",
  "chat_model.response_model_full": ""
}
```

설정 모델과 실제 응답 모델을 구분해 보관한다. live `plannerConfig.modelName` / 저장 `planner_config.model_name`으로 설정을 표시하며, 실제 응답명은 `chat_model.response_model`에 있었다. 최근 실행 기록의 모델이므로 다음 실행 전에 UI에서 바꾼 선택 모델까지 보장하지 않는다. CLI 로그의 모델명을 별도 IDE 대화에 적용하지 않는다.

## 4. brain, annotations, state, sidecar, CLI

### brain의 JSONL

확인한 로그:

```text
brain/<conversation_id>/.system_generated/logs/transcript.jsonl
brain/<conversation_id>/.system_generated/logs/transcript_full.jsonl
brain/<conversation_id>/.system_generated/logs/chunks/transcript/00000000.jsonl
brain/<conversation_id>/.system_generated/logs/chunks/transcript_full/00000000.jsonl
brain/<conversation_id>/.system_generated/steps/<step_index>/output.txt
```

최신 대화의 normal/full은 48,423/53,419 bytes, 24행이다. chunk와 일반 파일에 같은 step이 중복되어 둘을 이중 수집하지 않는다. UTF-8 JSONL의 실측 필드는 `step_index`, `source`, `type`, `status`, `created_at`, `content`, `media`, `thinking`, `tool_calls`다. tool call에는 `name`, `args`, 예시의 args에는 `query`, `toolAction`, `toolSummary`가 있었다. args 값이 JSON 문자열로 한 번 더 인코딩된 곳도 있어 값의 타입을 가정하지 않는다.

```json
{"step_index":0,"source":"USER_EXPLICIT","type":"USER_INPUT","status":"DONE","created_at":"2026-10-03T11:11:51Z","content":"[redacted user prompt]","media":["[omitted]"]}
{"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-10-03T11:11:51Z","thinking":"[omitted]","tool_calls":[{"name":"search_web","args":{"query":"[omitted]","toolAction":"\"Searching web\"","toolSummary":"\"Web search\""}}]}
```

이 로그의 `created_at`은 초 단위 step 생성 시각이며 파일 flush/완료 시각이 아니다. 최신 step 23의 생성은 11:33:48Z, transcript mtime은 11:33:59.810Z였다. 차이를 저장 주기로 취급할 수 없다. 기존 step 재생성/수정으로 행이 바뀔 가능성이 있지만 append-only 여부는 실측하지 못했다. 증분 tail을 기본으로 축소·교체·같은 크기 재작성을 보조 감지하고 SQLite/스트림 상태를 우선한다.

일반 산출물 `implementation_plan.md`, `walkthrough.md`, `content.md` 등도 있었지만 `task.md`는 0개였다. `task.md`를 필수 조건으로 삼으면 안 된다. 산출물 제목이나 mtime은 에이전트 상태를 대신하지 못한다.

### annotations와 state

실측 annotation 예시:

```protobuf
title: "[redacted conversation title]"
last_user_view_time: { seconds: 1791028236 nanos: 881000000 }
```

65개 파일에서 발견한 최상위 필드는 `title`, `last_user_view_time`, `archived`, `archival_status_timestamp`다. 제목은 표시에 적합하지만 열람·보관만으로 mtime이 변한다. protobuf-text 문자열 escape를 처리하고, 단순한 따옴표 regex로 임의의 제목을 읽는 구현은 피한다.

`antigravity_state.pbtxt`의 최상위 필드는 `post_onboarding`, `seen_nuxs`, `agent_onboarding_completed`, `migrate_convos_into_projects`, `installation_uuid`, `migrate_retroactive_projects`, `migrations`다. 현재 conversation ID, 모델, cwd, working flag는 없었다. CLI의 `jetski_state.pbtxt`도 주로 `post_onboarding`, `installation_uuid`, `migrations`였다.

### sidecar_data

`sidecar_data/<schedule-name>/events/YYYYMMDD_HHMMSS.mmm.json`, `logs/sidecar.log` 등이 있다. 확인한 JSON 이벤트:

```json
{
  "timestampMs": "1790986315294",
  "commandInvocationTimestampMs": "1790907534686",
  "error": "",
  "payload": {
    "newConversation": {
      "prompt": "[redacted scheduled prompt]",
      "conversationId": "22222222-2222-4222-8222-222222222222"
    }
  }
}
```

실측 timestamp는 숫자가 아니라 문자열이었다. **예약 실행→새 대화**의 대응이며 parent/subagent의 대응이 아니다. 새 대화 발견을 돕지만 기존 대화의 지속적인 busy/idle이나 모델을 알려주지 않는다.

### CLI 주의점

`history.jsonl`의 필드는 `display`, `timestamp`(정수 epoch milliseconds), `workspace`, `type`이며 이번에는 1행이었다. `settings.json`에는 `colorScheme`, `trustedWorkspaces`가 있다. trusted workspace 목록을 현재 cwd로 오인하지 않는다.

CLI 로그에서 확인한 형식:

```text
server.go:323] Creating CLI server backend: product=antigravity workspaceDirs=[C:\Example-Workspace\Project] appDataDir=C:\Users\<user>\.gemini\antigravity-cli cascadeManager=true codeAssist=true
model_config_manager.go:327] Propagating selected model override to backend: label="Gemini 3.8 Flash (High)"
```

조사 중 약 1분마다 CLI 로그·빈 crash 파일·WAL/SHM이 생성됐다. `CLI/conversations`와 `CLI/brain`은 비어 있고 CLI 요약 이력도 갱신되지 않았다. 이를 새로 작업하는 에이전트로 취급하지 않는다. 반복 실행 주체와 빈 crash 파일이 실제 crash를 뜻하는지는 조사하지 않아 원인을 단정하지 않는다.

## 5. 갱신 빈도 관측

### mtime/length 관찰

UTC **12:20:32.466–12:23:34.891**(182.425초). 2초 sleep에 파일 열거 시간이 더해진 관찰로 75회 샘플링했고, 마지막 sample은 12:23:32.449Z였다. 대상 root 3개의 파일 metadata를 비교했으며 bin/builtin/cache 등은 변경 판정에서 제외했다. 2,609→2,612 files, 변경 기록 28개였다.

| 대상 | 관측 |
|---|---|
| IDE conversations DB/WAL/SHM | 변경 0회 |
| IDE summary DB, annotations, brain transcript/step files, state, sidecars | 변경 0회 |
| IDE `language_server.log` | 1회, 208,386→240,380 bytes, mtime 12:21:16.954Z, 감지 12:21:28.735Z |
| IDE `main.log` | 변경 0회 |
| CLI artifacts/logs/sidecars | 변경 기록 27개, 약 1분마다 시작 관련 이벤트 |

CLI `cli.log`는 0-byte인 채 mtime이 12:20:52.599, 12:21:52.598, 12:22:52.741Z로 변했다. 길이가 같아도 mtime이 변하는 사례다.

IDE language_server 로그의 플러그인 경고는 약 10초마다 2행씩 나타났다. 파일 metadata 변화는 관찰 중 1회였다. 로그 행 시각과 mtime 알림은 같은 주기가 아니며, 정확한 flush/cache 원인은 확인하지 못했다.

### 저장 시각과 유지 관리·열람 시각의 차이

최신 idle 대화에서 얻은 UTC 시각:

| 신호 | 시각 |
|---|---|
| 마지막 user input | 11:33:25.7967896 |
| 대화 `last_modified_time` | 11:33:59.8074515 |
| transcript mtime | 11:33:59.8101228 |
| main conversation DB mtime | 11:34:29 |
| **빈** conversation WAL mtime | 11:37:49 |
| conversation SHM mtime | 11:42:49 |
| annotation 마지막 view/mtime | 11:50:36.881 / 11:50:36 |
| summary DB mtime | 11:51:06 |

작업 종료 후에도 WAL/SHM, 열람 annotation, 집계 DB 시각이 갱신됐다. 원인을 모두 특정하지 못했지만 **mtime을 실제 작업 시각으로 쓰면 오탐이 난다.** 특히 빈 WAL, SHM, summary DB 전체의 mtime으로 대화를 working으로 바꾸지 않는다.

### 실행 중 API의 3분 관측

UTC **12:33:23.022–12:36:23.094**에 `JetboxSubscribeToSummaries`를 유지했다. 초기 65개가 담긴 envelope 1개, 총 213,751 bytes만 받았고 추가 update는 0개였다. 모두 IDLE이다. 조회 시험의 5초/180초 timeout은 초기 성공 후 조사 측에서 스트림을 종료한 결과이며 서버 오류가 아니다.

**이 idle 관측은 active 상태의 API push 간격이나 파일 저장 간격을 측정한 것이 아니다.** 1–2초 감지를 설계 목표로 잡을 수 있지만 달성을 확인했다고 보고하지 않는다.

## 6. 부모·자식, cwd, task의 신뢰성

- 우선 후보: SQL `parent_conversation_id`, `nesting_depth`, `agent_name`. live에서는 `trajectoryMetadata.parentConversationId`, `rootConversationId`, `nestingDepth`, `subagentSpec.role`, `subagentSpec.typeName`, `subagentSpec.workspaceUri`. protobuf 필드 번호는 parent=5, root=6, nesting=17, subagent spec=8이다.
- `CortexStepInvokeSubagent`에는 `conversation_id`(5), `subagents`(9), `results`(10)도 있다. 스키마 확인만 했으며 이번 실제 부모·자식 행은 0개였다. 관계 표시를 실데이터로 검증했다고 말할 수 없다.
- `subtrajectoryUpdates` / `has_subtrajectory`는 내부 trajectory의 존재이며 독립된 자식 conversation과 같다는 보장은 없다. `parent_references`도 reference 종류를 확인해야 한다. battle/fork/reference/예약 프로세스를 subagent와 혼동하지 않는다.
- live `workspaces[].workspaceFolderAbsoluteUri`, SQL `workspace_uris`는 **project/workspace root**다. 도구의 실제 cwd와 다를 수 있다. 여러 workspace는 배열로 유지하고, workspace가 없는 대화는 null을 표시한다. brain 경로를 project cwd로 추정하지 않는다.
- task 우선순위: `latestTaskBoundaryStep.step.taskBoundary.taskName` → 실행 중 step의 `metadata.toolAction` / `metadata.toolSummary` / `metadata.toolCall.name` → 최신 `USER_INPUT.content` → `annotations.title` / SQL `title`. task boundary는 이번에 출현하지 않았다. 대화 제목과 최신 작업은 다를 수 있다.
- `waitingSteps`와 `CortexStepStatus.WAITING`은 대기 상태의 근거지만 사용자 승인 대기와 도구 대기를 구분해야 한다. payload/permissions/ASK_QUESTION 등으로 원인을 특정할 수 있을 때 `waiting`으로 분류한다.

## 7. C#의 구체적인 권장 알고리즘

### 권장 경로: .NET 표준 HTTP/JSON + 파일 fallback

1. **시작 시 서버 발견.** `main.log`/`language_server.log`의 끝에서 최신 포트를 얻고 해당 프로세스의 loopback listener와 대조한다. `Process.GetProcessesByName("language_server")`는 존재 확인에 보조적으로만 쓴다. 오래된 포트가 있으므로 응답과 PID를 확인한다. root HTML의 CSRF는 메모리에 얻고 재시작·접속 실패 시 다시 발견한다.
2. **현재 로드 집합 조회.** `GetAllCascadeTrajectories`를 `excludeSubtrajectories=false`로 호출하여 처음과 약 5초마다 대조한다. map key가 conversation ID다. 정상 빈 응답과 통신 실패를 구분하고 통신 실패로 전원을 gone 처리하지 않는다.
3. **전체 metadata/활동 push.** `JetboxSubscribeToSummaries` 1개를 유지해 초기 `updates` 및 이후 `updates`/`deletes`를 cache에 반영한다. 과거 65개를 모두 spawn하지 않는다. 현재 로드 집합, 명시적 busy/waiting 또는 실제 새 활동이 있는 대화를 표시한다. 스트림은 과거 카탈로그도 포함한다.
4. **필요한 대화만 step 구독.** 표시할 로드된 대화에 `StreamAgentStateUpdates`, `disableRehydration=true`, `PROD_UI`를 쓴다. 초기 step은 `stepCount`를 기준으로 끝의 몇 개, generator/executor는 알려진 개수에서 마지막 범위로 제한한다. 개수가 불명확하면 빈 page에서 `totalLength`를 얻어 범위를 정하는 방식이 후보지만, page 갱신·재구독 운용은 미검증이다. MVP는 live 요약과 필요한 DB 복사만으로 시작해도 된다. 전체 대화의 full trajectory를 매번 얻지 않는다.
5. **새 명시 상태 우선.** 최신 state update의 `fullyIdle=true`는 idle이다. RUNNING/BUSY/CANCELING, generating/running step, `notFullyIdle=true`는 busy 근거다. `hasActiveChildren`만 참이면 부모가 직접 타이핑 중이라는 보장은 없다. 새 PLANNER_RESPONSE+GENERATING은 thinking, 도구+RUNNING은 working으로 분류한다. waiting 원인이 확인되지 않으면 유보하고 payload에 근거가 있을 때 waiting으로 둔다. 기존 step의 status도 바뀌므로 최대 idx만 tail하지 않는다.
6. **표시 metadata.** 제목, task, workspace, 부모 ID는 위 필드를 쓴다. 모델은 최근 generator의 `plannerConfig.modelName`, 실제 응답 모델은 저장 BLOB에서도 얻는다. 빈 enum/placeholder는 unknown으로 둔다. `DateTimeOffset`으로 protobuf RFC3339와 SQL의 UTC offset 포함 날짜를 parse한다. 수신 시각과 실제 활동 시각은 별도로 보관한다.
7. **접속 실패 시 파일 경로.** 다음 shared-read snapshot과 watcher를 쓴다. `working ≈ recent activity`가 추정임을 유지한다. mtime만으로 thinking/waiting/reporting을 정확히 구분하지 못한다.
8. **gone은 표시 정책.** 정상 runtime이 ID를 삭제하거나 로드 집합에서 제외했을 때, 또는 SPEC의 30분 stale 기준으로 gone 처리한다. 접속 중인 로드된 idle 대화도 30분에 퇴장시킬지는 표시 정책으로 명시한다. DB 존재는 영구 alive 증거가 아니며 저장 RUNNING도 서버 crash 후 오래된 값일 수 있다.

기본 key는 `(provider="Antigravity", conversation_id)`다. 이 머신의 CLI mirror는 `app_data_dir="antigravity"`와 동일 ID로 합친다. 별도 설치/서버에서 ID 충돌이 발견됐을 때만 installation/storage scope를 추가한다. `trajectory_id`, PID, workspace만으로 개체를 구분하지 않는다.

상태 근거를 cache에 보관하는 설계 예시:

```text
ConversationId, TrajectoryId, ParentConversationId
Title, Task, ConfiguredModel, ResponseModel, WorkspaceUris[]
IsLoaded, RuntimeStatus, FullyIdle, CurrentStepType, CurrentStepStatus
LastActivityUtc, LastRuntimeReceiptUtc, ObservationSource, IsEstimated
```

### Connect 스트림 처리의 C# 요점

`HttpClient.SendAsync(..., HttpCompletionOption.ResponseHeadersRead, ct)`와 `ReadExactlyAsync`를 쓴다. Connect는 **1-byte flags + 4-byte big-endian length + payload** 형식이며 줄 단위 JSON이 아니다. HTTP/TCP read 경계와 메시지 경계는 다르다. [Connect protocol](https://connectrpc.com/docs/protocol/)을 따른다.

```csharp
// Framing 개요. 재시도/상태 cache가 생략된 설계 예시.
byte[] header = new byte[5];
await stream.ReadExactlyAsync(header, ct);
byte flags = header[0];
int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
if (length < 0 || length > maxMessageBytes) throw new InvalidDataException();
byte[] payload = new byte[length];
await stream.ReadExactlyAsync(payload, ct);
// flags & 1: 압축. 요청하지 않았다면 거절하고 재접속한다.
// flags & 2: end-stream JSON. error/trailers를 분석하고 종료한다.
// 일반 flags=0: JsonDocument.Parse(payload)로 updates/update를 얻는다.
```

요청에 CSRF 헤더, Connect protocol 헤더와 정확한 content type을 넣는다. `{}` 요청의 streaming frame은 `00 00 00 00 02 7B 7D`다. 긴 스트림에 unary용 짧은 `HttpClient.Timeout`을 공유하지 않는다. cancel/reconnect를 관리하고 초기 snapshot 수신이나 재접속을 새 활동으로 세지 않는다.

### 파일 경로의 C# 요점

- `FileSystemWatcher`의 `NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName`을 쓴다. conversation DB/WAL, summary DB/WAL, transcript로 범위를 제한하고 이벤트를 250–500ms debounce한다. 2–5초 metadata poll로 누락을 보완한다. SHM, annotation 열람, 전체 로그, CLI 시작 파일은 working timestamp에 반영하지 않는다. 중복 event/overflow는 가능하다. [Microsoft FileSystemWatcher docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0)
- `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)`로 읽는다. JSONL byte offset, 미완성 행, UTF-8의 잘린 byte를 유지한다. 축소/rename/교체 시 offset을 다시 설정한다. conversation+step index로 upsert하고 동일 크기 변경은 끝부분 제한 재읽기 또는 DB 상태로 보완한다.
- summary snapshot에서 필요한 column을 한 번 읽고 변경 시에만 다시 query한다. 큰 DB/로그를 매 poll마다 전부 읽지 않는다. 변경된 conversation의 step DB만 `MAX(idx)`와 끝부분/비종료 상태 step을 조회한다. `SELECT ... WHERE idx > @lastIdx`만으로는 기존 step의 RUNNING→DONE을 놓친다.
- 최신 저장 IDLE을 mtime 추정보다 우선한다. 명시 상태를 얻지 못할 때만 비어 있지 않은 WAL/main DB/transcript의 실제 변화를 SPEC의 약 2분 working 기준에 적용한다. 추정 idle은 미관측 상태이며 실제 중단 확인이 아니다.
- workspace TEXT의 빈 값을 허용하고 `JsonSerializer.Deserialize<string[]>()`를 쓴다. `file:///c%3A/...`, `file:///c:/...`가 모두 있었다. `Uri`로 unescape/local path를 얻고 여러 root를 유지한다. SQL의 0001년 sentinel은 unknown으로 처리하고 입력 index -1이면 필요한 경우 실제 USER_INPUT step을 참조한다.

### SQLite snapshot의 제약

원본에 SQLite를 직접 연결하지 않는다. DB와 존재하는 `-wal`, `-shm`, `-journal`을 동일 이름으로 전용 temp 디렉터리에 shared-read 복사했다. 요약 DB 2개와 대화 DB 3개 모두 복사본의 `PRAGMA quick_check`가 `ok`였다. 읽기는 이 Windows에 존재한 `winsqlite3.dll`의 `sqlite3_open_v2(..., SQLITE_OPEN_READONLY, ...)`를 사용했다.

main DB만 복사하면 WAL의 committed data를 놓친다. [SQLite WAL docs](https://www.sqlite.org/wal.html) 참조. **DB/WAL의 순차 복사만으로 atomic snapshot을 보장하지 못하며 quick_check 성공도 단일 시점의 의미적 일관성을 보장하지 않는다.** [SQLite의 active transaction 중 복사 설명](https://www.sqlite.org/howtocorrupt.html#_backup_or_restore_while_a_transaction_is_active)에 따라 이 경로는 best-effort로 취급한다.

C#에서는 DB/WAL의 length/mtime을 복사 전후 비교하여 바뀌었다면 짧은 backoff로 몇 번만 재시도한다. 안정된 복사도 완전한 보장은 아니다. 복사본이 불일치하면 이전 정상 snapshot을 유지하고 원본 복구·checkpoint·journal mode 변경을 하지 않는다. 비atomic 복사가 문제가 되는 active workload에서는 runtime API를 우선한다.

기존 의존성에 `Microsoft.Data.Sqlite`가 있으면 temp 복사본에 `Mode=ReadOnly;Pooling=False`로 연결할 수 있다. [Microsoft connection-string docs](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings) 참조. 움직이는 원본에 immutable을 지정하지 않는다. 원본 SHM/WAL에 SQLite가 쓰는 가능성을 피하기 위해 read-only 연결이면 언제나 안전하다고 판단하지 않는다.

## 8. 미검증·불확실한 점

- 활동 중 3분 측정은 하지 못했다. active throughput, 저장 간격, push latency, CPU 상관관계는 미측정이다.
- working/thinking/waiting/reporting 전이, 부모·자식 spawn/finish는 미관측이다. 스키마 존재를 동작 검증으로 해석하지 않는다.
- runtime API는 내부 사양이다. 인증, method, 타입, 기본값, HTTP listener 존재, 카탈로그 범위는 업데이트로 바뀔 수 있다. 버전/스키마를 기록하고 unknown enum을 허용하며 fallback한다.
- `GetAll...`의 로드 집합은 확인했지만 UI 탭 목록과의 일치, 서버 시작 직후의 완전성은 미검증이다. 구독의 65개를 live agent 65명으로 표시하면 안 된다.
- transcript의 append-only 보장, in-place update/rotation 규칙은 미검증이다. 모델/관계를 문자열 grep으로 추측하지 않는다.
- workspace root와 도구 cwd의 일치는 보장하지 않는다. PID 부모·자식, 예약 sidecar, subtrajectory, battle fork를 conversation 부모·자식과 혼동하지 않는다.
- SQLite 상태도 저장 시점 snapshot이다. 오래된 RUNNING이 crash 후 남을 수 있으며 mtime·프로세스 CPU·전체 로그만으로 확실한 busy/idle을 얻지 못한다.

MVP는 **읽기 호출을 확인한 localhost summary/agent-state를 우선하고, 파일만 사용하는 동작에는 추정 표시와 stale 기한을 붙이는** 구성을 권장한다. 다음 최소 검증은 실제 Antigravity 작업 1회에서 runtime state 수신 시각·SQLite/WAL/transcript 변경 시각·종료 시각을 함께 약 3분 기록하는 것이다. 이번 조사에서는 이를 위한 작업 전송이나 src 구현을 하지 않았다.
