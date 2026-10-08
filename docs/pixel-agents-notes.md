# Pixel Agents 설계 조사

조사일: 2026-10-03. `SPEC.md`를 읽고 [pixel-agents-hq/pixel-agents](https://github.com/pixel-agents-hq/pixel-agents)를 `%TEMP%\pixel-agents-ref`에 shallow clone했다. 조사 커밋은 **`3537e140c2094761beae748592aeb92ece8edfdd`**(v1.4.1)이다. 아래 소스 링크는 이 커밋에 고정되어 있다. 소스·JSON을 읽어 확인한 조사이며 앱을 실행하거나 시각적 동작을 검증하지 않았다. 참고 코드·이미지·기존 맵은 이 저장소에 복사하지 않았다.

**권장 방향:** 16px 타일, 명시적 footprint/좌석, 상태와 시각 행동의 분리, 발 위치에 따른 겹침, 작은 생활 동작을 독자적으로 구현한다. 시뮬레이션과 색상 처리는 .NET Core에서 결정하고 WPF와 Windhawk는 동일한 레이아웃·atlas·pose를 그린다. 참고 프로젝트의 편집기·에이전트 실행 기능 전체를 이식할 필요는 없다.

## 라이선스와 재사용 범위

| 대상 | 확인한 사실 | 재사용 판단 |
| --- | --- | --- |
| 코드 | 루트 [LICENSE][license]는 MIT, copyright © 2026 Pablo De Lucca. 복제·수정·배포 시 저작권과 허가문 보존 조건이 있다. | 향후 코드를 재사용한다면 해당 notice를 포함해야 한다. 이번에는 설계만 학습했다. |
| MetroCity 원작 캐릭터 | [README.md][readme]가 JIK-A-4의 MetroCity를 출처로 밝힌다. [원작 배포 페이지](https://jik-a-4.itch.io/metrocity-free-topdown-character-pack)는 **CC0 1.0 Universal**을 명시하고, 크레딧은 선택 사항이며 작가가 댓글에서 상업적 사용도 허용했다. [CC0 설명](https://creativecommons.org/publicdomain/zero/1.0/)상 복제·수정·상업적 배포가 가능하다. | 원작은 “출처 불명/비상업 전용”이 아니다. 다만 Pixel Agents의 추가 프레임·수정본까지 CC0라고 자동 분류하지 않는다. 수정 부분에는 저장소의 MIT 조건을 보수적으로 적용하고 출처를 보존하는 편이 타당하다. |
| 가구·바닥·벽 및 펫 | 추적된 별도 LICENSE/credit 파일 검색에서는 루트 LICENSE만 발견했다. 읽은 가구·펫 manifest에는 저작자·라이선스 필드가 없다. [docs/external-assets.md][external]는 Donarg의 팩을 추천하지만, 번들 PNG 전체의 원작자라는 증거는 아니다. | **모든 PNG가 MIT 또는 CC0라고 확인한 것은 아니다.** 각 이미지의 출처와 수정 권한은 별도 확인이 필요하다. |
| Donarg Office Interior Tileset | [작가 페이지](https://donarg.itch.io/officetileset)는 상업/비상업 사용·편집을 허용하지만, 수정본 포함 tileset 재판매·다른 사람에게 배포, web3/NFT/crypto/blockchain 사용, AI/ML 학습을 금지한다. 크레딧은 선택 사항이다. | 외부 팩 로더가 지원한다고 그 팩을 공개 저장소나 배포용 atlas에 재포장할 권리가 생기지 않는다. 앱 내 배포 허용 범위도 실제 약관에 맞춰 확인해야 한다. |

우리 `SPEC.md`는 **procedural sprites, no external art**를 요구한다. 따라서 현재 계획은 원본·수정본 아트와 코드를 가져오지 않고 직접 만든 그림과 구현으로 아래 설계 원리를 적용하는 것이다.

## 1. 레이아웃 JSON과 편집기

| 요소 | 실제 형식·동작 |
| --- | --- |
| 루트 | `OfficeLayout`: `version:1`, `cols`, `rows`, `tiles`, `furniture`; 선택적으로 `tileColors`, `layoutRevision`, `pets`, `carpetTiles`, `areas`, `areaTiles`. `tiles[row*cols+col]`인 **행 우선 1차원 배열**이다. |
| 타일·크기 | 타일 16×16px. `0=wall`, `1..9=floor pattern`, `255=void`. 코드 fallback은 20×11, 번들 `default-layout-1.json`은 **21×22, 462 tiles, 36 furniture**다. 편집 확장 한도는 **64×64**다. |
| 바닥 | `floor_0.png`부터 `floor_8.png`까지 16×16 grayscale 패턴. 타일 값 1이 이미지 index 0을 가리킨다. 패턴과 색은 분리한다. |
| 색 | `tileColors`는 tiles와 평행한 배열, null은 색 override 없음. 색 객체는 `{h,s,b,c,colorize?}`: hue, saturation, brightness, contrast. 바닥·벽은 grayscale 색칠, 가구는 원래 색 조정 또는 선택적 colorize. 벽 타일에도 색 객체를 저장할 수 있다. |
| 벽 | JSON에는 wall 여부만 저장한다. 렌더러가 N/E/S/W 인접 벽을 `1/2/4/8` bitmask로 조합해 16종 조각을 선택한다. wall sheet는 4열×4행의 16×32px 조각이다. **벽 스타일 index를 저장하는 layout 필드는 없다**. `selectedWallSet`은 편집기 상태이고 실제 wall instance 생성은 기본 set 0을 사용한다. |
| 가구 인스턴스 | `{uid,type,col,row,color?}`. `type`은 manifest의 **leaf asset ID**다. 각도나 별도 `rotation` 필드가 없고 회전/상태 전환은 `type` 교체로 저장한다. 미러 side는 가상 ID `:left`를 만든다. |
| 카펫 | `carpetTiles`의 각 항목은 null 또는 `{variant,color?,accentColor?,order?}`. 바닥 위 장식으로 통행 가능. 16-case marching-squares 조각으로 모서리를 연결하며 겹침은 큰 `order`가 위다. |
| 영역 | `areas:[{label,color}]`, `areaTiles`는 label 또는 null의 배열. 영역 색은 RGB hex, 투명도는 렌더 시 적용한다. workspace folder → 영역 label 목록인 `areaMappings`는 **레이아웃과 별도의 설정**이다. 영역은 좌석 선호를 결정하며 벽·통행 제한이 아니다. |

근거: [webview-ui/src/office/types.ts][types], [webview-ui/src/constants.ts][constants], [webview-ui/public/assets/default-layout-1.json][default], [webview-ui/src/office/layout/layoutSerializer.ts][layout], [webview-ui/src/office/floorTiles.ts][floors], [webview-ui/src/office/wallTiles.ts][walls], [webview-ui/src/office/engine/renderer.ts][renderer].

편집기는 다음처럼 작동한다.

- 도구: floor paint, wall paint, furniture place/pick, select/drag, eyedropper, erase, carpet paint/pick, area paint, pet toggle. 미리보기는 footprint에 맞춰 초록/빨강으로 배치 가능 여부를 표시한다. 오른쪽 드래그는 지우기다.
- 배치는 bounds, wall/void, 기존 footprint 충돌을 검사한다. 벽 장식은 아래 행이 벽에 붙으면 위쪽이 맵 밖으로 돌출할 수 있고, surface 물건은 desk와 겹칠 수 있다. 회전 시 새 footprint로 다시 검사한다.
- 바깥쪽 ghost border를 칠하면 그 방향으로 한 칸 확장하고 새 칸은 void로 만든다. 왼쪽/위 확장 시 타일·가구·색·카펫·영역 좌표를 옮기고 character 경로도 재구성한다.
- 불변 layout을 반환하는 편집 함수와 **최대 50개 전체 layout snapshot undo/redo**를 사용한다. 새 편집은 redo를 지우고 파생 좌석/장애물/렌더 상태를 재구성한다. 카펫 stroke와 색 slider 작업은 undo를 묶는 별도 처리가 있다. 모든 도구가 동일한 stroke 단위인 것은 아니다.
- `R` 회전, `T` on/off, Delete/Backspace 삭제, Ctrl/Cmd+Z undo, Ctrl/Cmd+Y 또는 Shift+Z redo, 단계별 Escape. `P`는 카펫 pick이다.
- 변경은 500ms debounce로 `~/.pixel-agents/layout.json`에 저장한다. Save는 즉시 저장하며 checkpoint를 갱신한다. Reset은 그 checkpoint로 복원한다. 별도로 새 번들 `layoutRevision`이 높으면 저장된 맵을 default로 재설정하는 정책이 있다.
- VS Code import/export는 파일 dialog, standalone은 Blob download/FileReader로 JSON을 처리한다. export 파일명은 `pixel-agents-layout.json`. import 검사는 version/배열 중심의 느슨한 검사이며, **64×64 한도·배열 길이까지 엄격히 검증하는 범용 schema validator는 아니다**. 외부 팩 이미지를 JSON에 포함시키지 않으므로 같은 asset ID가 준비되어야 한다.

근거: [webview-ui/src/office/editor/editorActions.ts][edit-actions], [webview-ui/src/office/editor/editorState.ts][edit-state], [webview-ui/src/hooks/useEditorActions.ts][edit-hooks], [webview-ui/src/hooks/useEditorKeyboard.ts][edit-keys], [webview-ui/src/App.tsx][app], [webview-ui/src/office/layout/exportLayout.ts][export], [server/src/layoutPersistence.ts][persistence], [adapters/vscode/PixelAgentsViewProvider.ts][adapter].

## 2. 가구 manifest와 외부 팩

가구 한 종류는 `assets/furniture/<folder>/manifest.json`과 PNG들이다. 단일 sprite는 `type:"asset"`; 복합은 `type:"group"`, `groupType:"rotation"|"state"|"animation"`, 재귀적인 `members`를 쓴다.

| 범위 | 필드·의미 |
| --- | --- |
| 루트 | `id,name,category,type,canPlaceOnWalls,canPlaceOnSurfaces,backgroundTiles`; 단일 asset은 아래 leaf 필드, group은 `groupType,rotationScheme?,members`. `category:"desks"`로 `isDesk`를 파생한다. |
| Leaf | `id,file,width,height,footprintW,footprintH,orientation?,state?,frame?,mirrorSide?`. 픽셀 크기와 타일 footprint는 다르다. 단일 파일명 생략 시 server loader는 `<id>.png`를 사용한다. |
| 회전 | `rotationScheme`은 `2-way`, `3-way-mirror`, `4-way` 중 하나. orientation은 front/back/side/left/right. side를 right로 정규화하고 mirrorSide이면 virtual left를 만든다. 순환 순서는 존재하는 front → right → back → left다. |
| 상태·프레임 | 상태와 회전은 중첩 가능. PC manifest는 front의 state group 안에 `on` animation frame 0,1,2와 `off` sprite가 있고 back/side는 별도 leaf다. loader가 flat catalog로 펼치며 이름·category·placement·orientation/state를 상속한다. 런타임 toggle은 주로 on↔off 쌍이며 animation은 `frame` 순서다. 개별 FPS 필드는 없고 가구 주기는 **0.2초/frame**이다. |
| 통행·배치 | 별도 `blocking:true`나 임의 collision polygon은 없다. footprint로 결정하고 **위쪽 `backgroundTiles` 행은 실제 `getBlockedTiles`와 배치 충돌 검사 모두에서 제외**한다. 문서/일부 주석의 “still block walking” 설명과 구현이 다르다. surface item은 desk와 겹치며 desk보다 앞에 정렬한다. |
| 좌석·interaction | manifest에 `seatPoints`/커피·TV 같은 interaction anchor 필드는 없다. **category=chairs인 footprint의 background 행을 제외한 각 타일이 좌석**이다. 첫 seat ID는 chair uid, 이후 `uid:1`, `uid:2` 등. 방향은 chair orientation → 인접 desk → down 순으로 정한다. 다인용 sofa도 여러 좌석이다. |

근거: [core/src/assets/manifestUtils.ts][manifest], [webview-ui/public/assets/furniture/PC/manifest.json][pc], [webview-ui/public/assets/furniture/SOFA/manifest.json][sofa], [webview-ui/src/office/layout/furnitureCatalog.ts][catalog], [webview-ui/src/office/layout/layoutSerializer.ts][layout], [webview-ui/src/office/editor/editorActions.ts][edit-actions].

외부 디렉터리는 Settings → Add Asset Directory로 추가하고 `~/.pixel-agents/config.json`의 `externalAssetDirectories`에 저장한다. 선택 폴더 아래에 `assets/furniture`, `assets/characters`, `assets/pets`가 있어야 한다. 추가/제거 시 재로드 후 catalog와 decoded pixel 배열을 webview에 전달한다. 가구 ID 충돌은 **나중 팩이 덮어쓰고**, 캐릭터·펫은 뒤에 붙인다. 캐릭터는 `char_N.png`를 숫자순, 펫 폴더는 이름순으로 읽는다. 바닥/벽/카펫은 현재 공통 asset cache에서 **번들만** 읽는다. 존재하지 않거나 깨진 항목은 건너뛰거나 placeholder 처리한다. `scripts/asset-manager.html`은 가구 manifest 저작 도구다.

근거: [server/src/assetReload.ts][reload], [server/src/assetLoader.ts][loader], [adapters/vscode/PixelAgentsViewProvider.ts][adapter], [docs/external-assets.md][external]. 이 문서는 주로 가구를 설명하므로 캐릭터·펫 지원은 실제 loader로 확인했다.

## 3. 캐릭터 시트·다양성·펫

| 항목 | 실제 규격 |
| --- | --- |
| 인간 시트 | `assets/characters/char_0.png` … `char_5.png`, 각각 **112×96 PNG**, **16×32 frame**, 7열×3행. 위에서부터 **down / up / right**, left는 right를 수평 반전한다. |
| 열·애니메이션 | 열 0,1,2는 walk 원본이며 재생은 **0→1→2→1**; 열 3,4 typing, 열 5,6 reading. idle은 walk 열 **1**의 정지 pose. walk 0.15초/frame, typing/reading 0.3초/frame. sleeping/coffee/reporting 전용 인간 프레임은 이 규격에 없다. |
| 픽셀·tint | 이미 색칠된 PNG를 사용하며 skin/clothing palette를 런타임에 부위별 swap하지 않는다. decoded sprite는 `''` 투명 또는 `#RRGGBB[AA]` 픽셀 배열. 추가 hue shift는 sprite 전체에 HSL adjustment로 적용하므로 피부까지 변할 수 있다. 색 변환 후 alpha를 보존하고 결과를 cache한다. |
| 다양성 | 먼저 가장 적게 쓰인 base skin 중 하나를 선택: 처음 N명은 각각 다른 skin, 이후 균등하게 반복하며 **45~315°** 무작위 hue shift. 외부 character를 추가하면 N도 증가한다. palette/hue/seat는 저장·복원한다. transient sub-agent는 부모 palette/hue를 그대로 물려받는다. |
| 크기 구분 | 디코딩 셀은 **16×32**다. hit-test 높이와 일부 Matrix 상수/주석의 **24**를 sprite sheet 높이로 오해하면 안 된다. seated character는 화면에서 아래로 6px 이동한다. |
| 펫 시트 | `assets/pets/<id>/{manifest.json,pet.png}`, manifest는 `{id,name}`. 번들은 Claudio와 Gitcat. **96×96 PNG**: y=0 행에 16×32 walkDown 3장 + idleDown 3장; y=32 행은 walkUp 3장 + idleUp 3장; y=64 행은 **32×32 walkRight 3장**. |
| 펫 파생·주기 | walkLeft는 반전. 실제 `petSpriteData.ts`는 idleRight=idleDown, idleLeft=idleUp을 쓰며 별도 side idle을 만들지 않는다. walk sequence **0,1,0,2**, idle **0,1,2,1**. idle/walk/follow FSM, 32px/sec 이동, 클릭하면 2초 heart bubble. |

근거: [core/src/assets/constants.ts][asset-constants], [core/src/assets/pngDecoder.ts][decoder], [webview-ui/src/office/sprites/spriteData.ts][sprites], [webview-ui/src/office/colorize.ts][colorize], [core/src/paletteUtils.ts][palette], [webview-ui/src/office/sprites/petSpriteData.ts][pet-sprites], [webview-ui/src/office/engine/petEntity.ts][pet-engine], [server/src/assetLoader.ts][loader].

## 4. 상태·경로·좌석·자식 수명·상태 표시

- **시각 FSM은 세 가지뿐:** `idle`, `walk`, `type`. provider event/active/tool/permission 정보는 별도로 저장한다. `type`에서 reading tool이면 읽기 sprite를 선택한다. 우리 SPEC의 working/thinking/waiting/reporting/idle/gone 여섯 상태와 일대일 대응하는 원작 FSM은 없다.
- **BFS:** 상하좌우 4연결, wall/void/blocked furniture 제외, start 제외·end 포함 경로. 48px/sec로 타일 중심 사이를 보간한다. 자기 좌석만 쿼리 동안 unblock한다. 일반 이동에는 캐릭터 간 collision reservation이 없으며 spawn 때만 겹침을 피하려 한다. 업무 재개 시 배회 경로를 자기 좌석으로 다시 계산한다.
- **좌석:** 저장 seat가 유효하면 유지. 일반 agent는 folder에 매핑된 영역 → 영역 없는 좌석 → 아무 빈 좌석 순이고 각 후보군에서 PC 등 electronics를 바라보는 자리를 우선한다. teammate는 lead의 **현재 위치보다 고정된 seat**에 가까운 Manhattan-distance 빈 좌석을 우선한다. 자리가 없으면 walkable tile에서 작업한다. layout rebuild는 uid가 유지된 좌석부터 보존한다.
- **휴식:** 비활성화 후 2~20초 쉬었다가 무작위 walkable tile로 이동한다. 3~6번 배회 후 자리로 돌아가 120~240초 휴식한다. turn-end sentinel로 즉시 종료된 turn에 긴 좌석 휴식이 붙는 것을 피한다. 원작은 coffee/TV/phone/napping/chatting interaction 시스템을 구현한 사례가 아니다.
- **등장·퇴장:** top-level agent는 좌석에서 **0.3초 Matrix materialization**, sub-agent는 부모 가까이 같은 색으로 나타난다. 삭제는 좌석을 먼저 반환하고 효과가 끝난 뒤 character map에서 제거한다. 문을 걸어 들어오거나 부모에게 종이를 전달하는 동작은 이 구현에 없다.
- **Transient sub-agent:** `(parentAgentId,parentToolId)`를 중복 방지 key로 사용한다. sub-tool 이벤트를 그 character로 연결한다. 일반 parent tools-clear는 자식을 제거하지만 live background spawn과 inline team worker는 보존하고 실제 `subagentClear`까지 유지한다. 미처 없던 character는 sub-tool 이벤트에서 지연 생성할 수 있다. 부모 close는 자식도 정리한다.
- **Teammate:** named background spawn은 teammate, unnamed spawn은 transient sub-agent로 분류한다. teammate는 별도 agent identity/좌석/role을 갖고 팀 metadata를 유지한다. inline은 lead session hook을 공유해 routing이 필요하고 tmux/독립 session은 자기 session으로 연결한다. unnamed transcript는 shadow store에서 읽어 기존 sub-agent 이벤트로 변환하며 중복 character를 만들지 않는다. completion/team-switch/lead-close 때 정리하고 마지막 teammate가 사라지면 LEAD badge를 내린다. “persistent”는 부모 turn을 넘어 유지된다는 뜻이며 항상 살아 있는 것은 아니다.
- **말풍선:** 11×13 sprite의 amber `...`는 permission, green check는 turn 완료, heart는 펫 반응. permission은 clear 이벤트까지, check/heart는 2초 후 사라지며 마지막 0.5초 fade. `awaitingInput=true`에는 check를 그리지 않고 **Waiting for input** label을 사용한다. 일반 대화 text bubble과 tool별 sprite icon 전체가 구현된 것은 아니다.
- **텍스트 상태:** 별도 React DOM `ToolOverlay`가 hover/selection 또는 always-show 설정으로 tool status, Needs approval, folder, LEAD/role, context gauge를 보여준다. 완료 check가 label에 가려지지 않도록 비선택 완료 agent의 panel을 억제한다. 세부 state를 그림 하나에 모두 넣지 않는다.
- **펫 행동:** 3~15초 pause 후 배회하거나 30% 확률로 3타일 이내 character를 따라간다. follow는 5~15초 제한, 1초 간격 재탐색, target 부재/도착 시 idle 복귀다.

근거: [webview-ui/src/office/engine/characters.ts][characters], [webview-ui/src/office/layout/tileMap.ts][tilemap], [webview-ui/src/office/engine/officeState.ts][office], [webview-ui/src/office/engine/seatPlacement.ts][seats], [webview-ui/src/hooks/useExtensionMessages.ts][messages], [server/src/agentRuntime.ts][runtime], [server/src/subagentWatch.ts][subwatch], [server/src/teamUtils.ts][teams], [webview-ui/src/office/components/ToolOverlay.tsx][overlay], [webview-ui/src/office/engine/petEntity.ts][pet-engine].

## 5. 렌더링·카메라·깊이

Canvas 2D와 `requestAnimationFrame` 기반 update/render loop다. dt는 최대 0.1초로 제한한다. canvas backing store는 viewport×devicePixelRatio, zoom은 **정수 1~10 device pixels/source pixel**, 기본은 round(2×DPR)다. sprite·색 변환·zoom별 offscreen canvas를 cache하고 `imageSmoothingEnabled=false`, 정수 좌표로 그린다. 맵을 화면 중앙에 두고 pan을 더하며 DOM overlay도 같은 좌표 투영을 쓴다.

Ctrl/Cmd+wheel은 zoom, 일반 wheel/trackpad와 middle drag는 pan이다. pan은 viewport의 25% margin을 남기는 범위로 제한한다. agent camera-follow는 0.1 보간 계수로 부드럽게 추적하고 수동 pan이 취소한다. 이는 참고 앱 조작이며 우리 관찰 전용 MVP의 컨트롤 요구를 자동으로 넓히지는 않는다.

그리기 순서는 **floor/wall base → carpet → 선택적 area wash → seat indicator → Y-sort된 wall/furniture/characters/pets → bubble → area label → editor overlay**다. UI text panel은 DOM에 올린다.

| Drawable | `zY` 규칙 |
| --- | --- |
| 벽 | `(row+1)*16`; 32px tall sprite를 자기 타일보다 위로 올려 그린다. |
| 일반 가구 | `row*16 + spriteHeight`. |
| 의자 | back은 `(row+footprintH)*16+1`로 사람을 가린다. 나머지는 `(row+1)*16`로 사람이 의자 위에 보인다. |
| 책상 위 물건 | 겹치는 desk zY보다 적어도 `+0.5` 앞에 둔다. |
| 인간 | pixel-center `y+8+0.5`. **시각 seated offset 6px를 depth에는 더하지 않는다.** 발 기준 draw anchor는 bottom-center이며 device pixel로 반올림한다. |
| 펫·outline | pet=`y+8`; hover/selected outline은 character zY−0.001에 두고 사람보다 먼저 그린다. |

가구를 전부 먼저 그리고 사람을 나중에 그리는 방식보다, 이 혼합 정렬과 chair 예외가 작은 사무실의 공간감을 만든다. 근거: [webview-ui/src/office/engine/renderer.ts][renderer], [webview-ui/src/office/layout/layoutSerializer.ts][layout], [webview-ui/src/office/components/OfficeCanvas.tsx][canvas], [webview-ui/src/office/engine/gameLoop.ts][loop], [webview-ui/src/office/sprites/spriteCache.ts][sprite-cache], [webview-ui/src/constants.ts][constants].

## 6. 따라 할 만한 매력 요소 15개

아래는 확인한 구현에서 추린 **설계 해석**이다. 펫·follow camera·클릭 기능 등은 우리 MVP 밖의 선택적 후속 아이디어다.

| # | 구체적 디테일 | 참고 근거 |
| --- | --- | --- |
| 1 | 단순 idle silhouette와 여러 방향의 걷기로 “프로세스”에 몸과 위치를 준다. | [characters.ts][characters], [spriteData.ts][sprites] |
| 2 | walk 0→1→2→1의 재사용 리듬: 세 장으로 네 박자를 만든다. | [spriteData.ts][sprites] |
| 3 | 타이핑과 읽기를 다른 두 프레임으로 구분해 작업 성격이 보인다. | [characters.ts][characters] |
| 4 | 모든 사람을 같은 색으로 만들지 않고 base skin을 균등 배분한다. | [paletteUtils.ts][palette] |
| 5 | 자식이 부모 색을 물려받아 관계가 이름표 없이도 드러난다. | [officeState.ts][office] |
| 6 | teammate가 lead의 고정 자리 가까이 앉아 팀이 공간적으로 뭉친다. | [seatPlacement.ts][seats] |
| 7 | 영역별 바닥·카펫 색과 작은 소품으로 같은 grid 안에 다른 장소를 만든다. | [types.ts][types], [renderer.ts][renderer] |
| 8 | 6px seated offset과 의자 앞/뒤 가림으로 의자에 실제 앉아 보이게 한다. | [renderer.ts][renderer], [layoutSerializer.ts][layout] |
| 9 | active agent가 바라보는 PC가 켜지고 세 프레임 화면이 순환한다. | [officeState.ts][office], [PC manifest][pc] |
| 10 | 쉬는 시간을 2~20초로 흩뜨려 모두 동시에 걷지 않게 한다. | [characters.ts][characters], [constants.ts][constants] |
| 11 | 3~6번 돌아다닌 뒤 자기 자리로 복귀하여 방황에도 작은 생활 리듬을 준다. | [characters.ts][characters] |
| 12 | 긴 상태 문장보다 작은 amber 승인 점·green 완료 check를 먼저 보인다. | [spriteData.ts][sprites], [ToolOverlay.tsx][overlay] |
| 13 | 완료 bubble의 마지막 0.5초 fade로 표시가 갑자기 끊기지 않는다. | [renderer.ts][renderer] |
| 14 | hover 시 silhouette를 따라가는 1px 흰 outline으로 작은 사람을 쉽게 찾는다. | [spriteCache.ts][sprite-cache] |
| 15 | 펫이 잠깐 사람을 따라오다가 자기 생활로 돌아가며, 쓰다듬기에는 heart로 반응한다. | [petEntity.ts][pet-engine], [officeState.ts][office] |

## 7. .NET/Windhawk 적용 권장안

### 현재 계약을 출발점으로 삼기

조사 시점의 우리 `src/Officelings.Core/Models.cs`는 `StateSnapshot(schemaVersion,generatedAt,sequence,atlas,office,agents,warnings)`, `schemaVersion=2`다. `Pose`는 연속 **타일 좌표**, `spriteRow/spriteColumn`, action/facing/visible 및 opacity·report/chat 관련 필드를 제공한다. `src/Officelings.Core/SpriteAtlas.cs`의 atlas v2는 **16×24 cell, 45열×49행, 720×1176px, 8 variants**이며 v1의 row 0..6/column 0..12 의미를 유지한다. Tracker는 atlas를 먼저 쓰고 `state.json`을 atomic replace한다.

`docs/state-schema.md`도 최종 확인 시 v2를 설명한다. v1 reader가 그대로 호환된다는 뜻은 아니며 atlas 크기와 action/facing 범위는 공급 metadata로 읽어야 한다. 후속 구현은 **동시에 확정한 C#·C++ 계약**을 기준으로 버전을 올린다. 원작의 16×32 인간 셀을 우리 16×24 셀에 조용히 대입해서도 안 된다.

### A. `office-layout.json` — 편집 원본과 runtime의 같은 지도

`version:1`의 평평한 JSON을 정의하고 root의 현재 `widthTiles,heightTiles,tileSize,door,furniture` 이름을 유지한다. 동일 구조를 `state.json.office`에 실어 C++도 standalone 파일과 같은 map을 읽게 한다. 파일들은 모두 runtime 데이터 디렉터리 `%LOCALAPPDATA%\Officelings`에 두는 안이며, 여기서 생성하거나 구현하지 않았다.

| 필드 | 권장 계약 |
| --- | --- |
| `version`, dimensions | layout version integer, `tileSize:16`, width/height **1..64**, 좌상단 원점, +X 오른쪽/+Y 아래. |
| `tiles` | 정확히 width×height인 row-major 정수 배열. `0=wall,1=floor,255=void`. **참고 저장소와 달리 패턴은 통행 의미와 분리**한다. |
| `floorPatterns`, `tileColors` | 같은 길이의 선택적 배열. pattern은 작은 정수 ID, 색은 null 또는 `#RRGGBB`. 생략 시 명시된 기본 패턴/색. HSL 계산을 C++ 계약으로 전달하지 않는다. |
| `furniture` | 기존 kind/x/y/width/height/agentId에 안정된 `id,assetId,orientation,state,color?`를 추가. orientation은 `down/up/right/left`, 각도 없음. width/height는 선택 variant의 footprint와 같아야 한다. |
| `door` | 현재 `{x,y}` 유지. 하나의 reachable floor tile인 entrance/exit. 여러 출입구는 나중에 필요할 때 추가한다. |
| `areas`, `areaTiles` | 선택적 `[{id,label,color}]`와 같은 길이의 id 또는 null 배열. label 변경이 FK를 깨지 않도록 id를 분리한다. 용도는 work/break 자리 선호. cwd-prefix→area 매핑은 별도 settings이며 JSON에 로컬 경로를 강제하지 않는다. |

editor-lite는 별도의 명시적 편집 화면 또는 도구로 제한한다. 타일 paint/erase, furniture place/move/delete/rotate, 색 변경, undo/redo, JSON import/export와 explicit Save만으로 충분하다. 50개 snapshot history는 작고 단순한 출발점이다. 기본 viewer의 관찰 전용 조작은 유지하고, palette 저작기·external pack 시장·team launch는 범위에 넣지 않는다.

import/Save 때 dimension/배열 길이, ID 중복·참조, footprint bounds/충돌, door→desk 접근·break interaction 도달 가능 여부를 Core에서 검사한다. 미지 asset ID는 silent skip 대신 유효한 마지막 레이아웃을 유지하며 오류를 알린다. 편집 저장마다 자동 default reset을 하지 않는다. 좌석은 instance/interaction ID로 보존하고 삭제된 좌석만 재배정한다.

### B. `furniture-manifest.json` — 재귀 group 없이 명시적 geometry

`version:1,items:[…]`으로 **평평한 variant 목록**을 정의한다. WPF와 C++에 동일한 orientation/state→region 선택 규칙을 준다. 다양한 예외를 `backgroundTiles`라는 한 숫자에 숨기지 않는다.

| 필드 | 권장 계약 |
| --- | --- |
| Item | `id,label,kind,variants:[…]`; UI용 label은 시뮬레이션 key가 아니다. |
| Variant | `orientation,state,footprintWidth,footprintHeight,frames:[regionId,…],frameDurationMs`. 회전 sprite를 직접 만들고 left mirror가 필요하면 `.NET`에서 미리 굽는다. |
| Collision | `blockedCells:[{x,y},…]`는 footprint 안의 local tile offsets. 빈 배열은 통행 가능. seat/interaction 타일은 여기에 넣지 않아도 되므로 글로벌 blocked set을 임시로 수정할 필요가 없다. |
| Interaction | `interactionPoints:[{id,x,y,facing,action,capacity}]`. x/y는 instance 기준 **타일 offset**이며 furniture 외부의 접근 칸도 허용한다. action은 현행 pose에 맞는 typing/coffee/tv/phone/napping/chatting/reporting 등 고정 문자열. seat는 capacity 1; sofa는 별도 seat point 두 개. |
| Placement/depth | placement는 floor/wall/surface 중 하나. surface instance에는 `surfaceId`를 지정한다. variant의 `layer`는 0=floor, 1=scene, 2=overlay; `sortAnchorY,sortOffsetPx`는 instance 원점 기준 픽셀 값이다. 의자 앞/뒤는 이 값으로 명시하며 sprite 영역은 footprint보다 클 수 있다. |

예: 우리 procedural 2×1 desk는 두 blockedCells와 바로 아래 `work` point를 갖는다. coffee machine은 machine tile만 막고 앞 칸에 `coffee` point를 둔다. 양쪽 sofa seat와 TV 관람 point를 구분하면 idle이 의자에 앉기만 하는 것이 아니라 활동을 하게 된다. 회전마다 blockedCells와 interactionPoints를 명시해 .NET/C++가 서로 다른 좌표 회전을 구현하지 않게 한다.

### C. `character-roster.json` + atlas sheet 규격

인간은 **현행 16×24 cell**을 우선 유지하고 original skin/hair/clothing/accessory를 절차적으로 생성한다. 종이 전달·커피·TV·폰·낮잠·채팅은 우리 SPEC에 맞는 자체 프레임으로 만든다. 외부 character/pet pack 로더는 MVP에 필요하지 않다.

| 범위 | 권장 계약 |
| --- | --- |
| Roster | `version:1,styles:[{id,skin,hair,shirt,accessory}],assignments:[{agentId,styleId}]`. 색은 RGBA hex 또는 이름 붙인 자체 palette ID. provider badge와 supervisor/worker 특징을 style에 bake한다. agentId의 안정된 hash로 기본 style과 animation phase를 선택하고 세션 재발견 때 외모를 유지한다. |
| Sheet | 현행 `atlas.file,width,height,cellWidth,cellHeight,columns,rows,version`을 유지. PNG RGBA 8bit, straight alpha, source pixel 좌표 정수, no gutters. character frame rect는 `(spriteColumn*16,spriteRow*24,16,24)`. |
| Clip metadata | `.NET` 저작 데이터에 `clips:[{action,facing,frames:[{row,column}],frameDurationMs,loop}]`. walk 4방향, typing/thinking/waiting/reporting 및 휴식 활동을 명시. runtime에서는 **이미 선택한 pose frame**을 C++에 전달한다. |
| 기타 sprite | 같은 atlas에 floor/wall/furniture/bubble region을 추가하려면 `atlas.regions:[{id,x,y,width,height,anchorX,anchorY}]`를 정의한다. region은 가변 크기이고 atlas bounds 안이다. character grid 의미를 유지하며 새 공간에 배치하고 필요하면 atlas 버전을 올린다. |
| Anchor/depth | character 발 anchor=(8,24), world 발은 `((x+0.5)*16,(y+1)*16)`, destination은 발−anchor. 가구 destination은 `(instance.x*16−anchorX,instance.y*16−anchorY)`, sortY는 `instance.y*16+sortAnchorY+sortOffsetPx`. scene의 인간은 발 Y로 정렬하며 seated offset/bob은 그리기에만 적용한다. layer → sortY → stable ID 순, bubble은 최상단이다. |

색 변형·미러·바닥 pattern tint를 **.NET에서 한 번 bake**하여 atlas를 만든다. C++는 PNG source rectangle과 alpha만 처리한다. 런타임 palette/HSL 라이브러리나 TS식 픽셀 문자열 배열은 전달하지 않는다. 16×32가 나중에 필요하면 새 sheet version을 명시하고 양쪽 renderer를 함께 갱신한다.

### 적용 순서와 공유 보장

1. **계약 먼저:** 실제 진행 중인 v2 변경을 반영해 map/manifest/roster/region field 의미와 기본값을 합의한다. 예외 없는 JSON 숫자·문자열·배열·객체만 사용한다. wire의 의미가 바뀌면 `schemaVersion`/`atlas.version`을 함께 지원하는 reader부터 준비한다.
2. **Core 한 곳에서 결정:** layout/manifest를 읽고 BFS·좌석/interaction 예약·idle 선택·부모 보고·출입을 계산한다. semantic state와 pose.action을 분리한다. 부모 완료와 background child 완료도 분리하고, 부모가 없는 report는 자기 desk로 fallback한다. 최근 idle 활동 반복을 피하고 agent별 phase를 다르게 한다.
3. **두 렌더러에 같은 입력:** WPF와 Windhawk가 동일한 `office` 및 atlas region manifest를 사용한다. runtime manifest는 `state.json`의 추가 asset metadata로 전달해 **기존 두 파일 배포 계약**을 유지할 수 있다. 동적인 furniture state/frame은 Core가 instance의 runtime `frameRegionId`로 내보낸다. `pose.spriteRow/Column`과 opacity/report/reaction도 authoritative로 읽는다. C++에서 또 다른 BFS, 랜덤 idle, hue shift, provider 추론을 만들지 않는다.
4. **편집 원본과 runtime:** editor-lite는 동일 `office-layout.json`을 쓰고 Core가 검증한 동일 형태를 snapshot에 넣는다. C++는 편집 기능 없이 읽을 수 있어야 한다. 편집 history·UI selection은 runtime snapshot에 넣지 않는다.
5. **교체 일관성:** 지금처럼 atlas를 먼저 완성하고 state를 atomic replace한다. 고정 atlas가 실행 중 바뀌지 않는 MVP는 단순하다. live atlas 변경을 지원하면 generation ID와 content hash를 metadata에 넣고 state가 가리키는 atlas가 맞을 때만 적용한다. 완전한 보장이 필요할 때는 generation별 파일명을 `atlas.file`로 가리키거나 reader가 matching pair를 확인할 때까지 이전 pair를 유지한다. 고정 파일 두 개의 rename만으로 pair가 동시에 바뀌지는 않는다.
6. **최소 검증:** 동일 sanitized fixture에서 layout import/export 왕복, door→interaction BFS, blocked footprint, seat capacity, parent/child completion 구분을 확인한다. 동일 snapshot의 방향·source rect·chair occlusion을 두 renderer에서 한 장씩 비교한다. 구현 시 해당 검사만 추가하며 이번 조사에서는 build/test/tracker 실행을 하지 않았다.

[license]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/LICENSE
[readme]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/README.md
[external]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/docs/external-assets.md
[types]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/types.ts
[constants]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/constants.ts
[default]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/public/assets/default-layout-1.json
[layout]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/layout/layoutSerializer.ts
[floors]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/floorTiles.ts
[walls]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/wallTiles.ts
[renderer]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/renderer.ts
[edit-actions]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/editor/editorActions.ts
[edit-state]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/editor/editorState.ts
[edit-hooks]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/hooks/useEditorActions.ts
[edit-keys]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/hooks/useEditorKeyboard.ts
[app]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/App.tsx
[export]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/layout/exportLayout.ts
[persistence]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/layoutPersistence.ts
[adapter]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/adapters/vscode/PixelAgentsViewProvider.ts
[manifest]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/core/src/assets/manifestUtils.ts
[pc]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/public/assets/furniture/PC/manifest.json
[sofa]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/public/assets/furniture/SOFA/manifest.json
[catalog]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/layout/furnitureCatalog.ts
[reload]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/assetReload.ts
[loader]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/assetLoader.ts
[asset-constants]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/core/src/assets/constants.ts
[decoder]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/core/src/assets/pngDecoder.ts
[sprites]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/sprites/spriteData.ts
[colorize]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/colorize.ts
[palette]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/core/src/paletteUtils.ts
[pet-sprites]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/sprites/petSpriteData.ts
[pet-engine]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/petEntity.ts
[characters]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/characters.ts
[tilemap]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/layout/tileMap.ts
[office]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/officeState.ts
[seats]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/seatPlacement.ts
[messages]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/hooks/useExtensionMessages.ts
[runtime]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/agentRuntime.ts
[subwatch]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/subagentWatch.ts
[teams]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/server/src/teamUtils.ts
[overlay]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/components/ToolOverlay.tsx
[canvas]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/components/OfficeCanvas.tsx
[loop]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/engine/gameLoop.ts
[sprite-cache]: https://github.com/pixel-agents-hq/pixel-agents/blob/3537e140c2094761beae748592aeb92ece8edfdd/webview-ui/src/office/sprites/spriteCache.ts
