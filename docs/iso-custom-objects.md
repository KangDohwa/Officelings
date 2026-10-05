# 로컬 iso 가구 제작

이 안내는 [동결된 에디터 명세 §7](iso-editor-spec.md#7-local-custom-object-contract)을 따릅니다. 가져오기 대상은 로컬 `.iso.json` 한 파일과 같은 디렉터리 안의 상대 경로 PNG입니다. 원본 픽셀은 보존하며 회전·미러링·색 보정·잘라내기를 하지 않습니다. 캐릭터, 좌석 리그, 벽 장식은 가져오기 대상이 아닙니다.

## 예제: 작은 상자

완전한 예제는 [test-crate.iso.json](../tests/fixtures/iso-editor/objects/valid/test-crate/test-crate.iso.json)과 같은 폴더의 [crate.png](../tests/fixtures/iso-editor/objects/valid/test-crate/crate.png)입니다. 둘을 새 로컬 폴더에 복사한 뒤 제작을 시작하세요.

상자는 `user/test-crate`, 이름은 `Synthetic test crate`입니다. 시트는 64×80, 기준점은 `(32,48)`이며 한 칸을 막습니다. 높이는 16px, 회전은 0° 하나만 제작했습니다. 실제 접점 확인은 불투명한 `(32,32)` texel을 검사합니다. 기준점 자체가 투명해도 됩니다.

```json
{
  "rotation": 0, "orientation": "se", "state": "default",
  "footprintW": 1, "footprintH": 1,
  "blockedCells": [{"x": 0, "y": 0}],
  "height": 16, "layer": 1, "points": [],
  "parts": [{
    "sheetId": "user/test-crate/body", "animation": "body",
    "direction": "se", "partId": "test-crate__ground__se__00",
    "depthAnchor": {"i": 0.5, "j": 0.5}, "isoZOrder": 0
  }]
}
```

이 코드는 전체 예제의 variant 부분입니다. manifest에는 `version:1`, `customObjectVersion:1`, `projection:"iso2to1"`, `id`, `displayName`, `palette`, `sheets`, `furniture`가 필요합니다. 가구 family는 하나이며 manifest ID와 같아야 합니다.

새 가구를 만들 때 `user/<slug>`와 시트·파트 ID를 함께 바꾸세요. slug는 소문자 영문·숫자로 시작하고 소문자 영문·숫자·하이픈만 사용하며 최대 48자입니다. 시트 ID는 `user/<slug>/`로 시작합니다. 파트 ID는 `<slug>__<role>__<facing>__<nn>` 형식이며 같은 물리 파트의 상태별 참조에서 유지합니다.

90°/180°/270°의 orientation은 각각 `sw`/`nw`/`ne`입니다. 회전을 늘리려면 각 방향의 원본 그림과 variant를 직접 제작하세요. 회전 하나만 있는 상자는 유효하며 에디터에서 회전 명령은 C003을 반환합니다. `desk`나 `chair` kind를 붙여도 사용자 가구에 작업 좌석 기능이 생기지는 않습니다.

## PNG와 파트

8-bit RGB, RGBA, indexed PNG를 사용하세요. PNG animation, interlace, 텍스트·EXIF·알 수 없는 payload는 허용하지 않습니다. 일반 시트 alpha는 0/255이고, 투명 texel의 RGB는 `(0,0,0)`이어야 합니다. 그림자 시트는 RGB `#111924`, alpha 0/48/96을 사용하며 별도 `shadow` descriptor로 연결합니다.

모든 보이는 일반 texel 색을 `palette`에 정확히 적으세요. 누락은 O008 Error이며 가져오기를 막습니다. 승인된 환경 ramp 밖의 색은 O008 Warning입니다. 경고만 있으면 가져오며 색을 바꾸지 않습니다.

각 clip에 `partId`, `depthAnchor`, `isoZOrder`, `anchorChecks`를 명시하세요. `anchorChecks`는 clip 안의 실제 texel과 반경 0 또는 1입니다. clip과 part 양쪽에 적은 메타데이터는 같아야 하며 0도 명시한 값으로 비교합니다. 긴 가구는 깊이별로 불투명 texel이 겹치지 않는 마스크로 분리하세요. 그림자만 일반 파트와 겹칠 수 있습니다.

## 제한과 진단

| 규칙 | 확인할 내용 |
| --- | --- |
| O001 | 닫힌 스키마, 중복 JSON key 금지, 깊이 64 이하. `name`, variant `id`, 설명용 `provenance`만 추가 허용 |
| O002 | namespace·이름·ID. 다른 해시로 같은 ID를 덮어쓰려면 사용하지 않는 기존 가구를 먼저 제거 |
| O003 | 상대 경로, 일반 파일, 모든 조상의 reparse point 금지. URL·UNC·ADS·장치명·`.`·`..`·빈 구성요소·끝의 점/공백 금지 |
| O004 | JSON 4 MiB, 시트 8개, PNG 하나 8 MiB/전체 16 MiB, 한 변 2048 이하, 전체 8,388,608 pixels. cell 1..512, clip 64개/시트, frames 1..16, fps (0,12], variant 16개, part 32개/variant |
| O005 | PNG CRC·chunk·압축 해제·alpha 검사. 압축 해제 한도에 scanline filter byte 포함 |
| O006 | footprint 1..10, 높이 0..96, 고유한 회전/상태와 blocked cell. 바닥 layer 0은 높이 0, blockedCells 빈 배열 |
| O007 | 파트 참조·프레임·물리 접점·마스크. depth 범위 `[-0.5,w-0.5]×[-0.5,h-0.5]`, rank -512..511, supportRank 0..4 |
| O008 | palette 1..256개의 고유 RGB. 선언 palette 위반은 Error, frozen ramp 차이는 Warning |
| O009 | 설치·해시·카탈로그 트랜잭션. 실패 진단에 개인 경로나 임의 원문을 표시하지 않음 |
| O010 | 사용자 가구 64개/설치 PNG 64 MiB. 합친 팩은 시트 128개, family 512개, 각 runtime JSON 4 MiB, RGBA 128 MiB |
| O011 | 현재·마지막 유효 상태·저장본·백업·Undo/Redo가 참조하는 가구는 제거 불가. 안전하지 않은 정리도 거절 |

## 같은 검증기로 확인하기

저장소 루트에서 .NET CLI를 실행하세요. 아래 경로는 합성 예제용이며 개인 프로필을 읽지 않습니다.

```powershell
dotnet run --project tools/IsoCustomObject -- validate tests/fixtures/iso-editor/objects/valid/test-crate/test-crate.iso.json
dotnet run --project tools/IsoCustomObject -- import tests/fixtures/iso-editor/objects/valid/test-crate/test-crate.iso.json artifacts/custom-object-example
dotnet run --project tools/IsoCustomObject -- catalog artifacts/custom-object-example
```

실패는 `O003 Error: Custom object paths must be regular local files within the import directory.`처럼 에디터와 같은 rule ID·영문 템플릿으로 출력합니다. 종료 코드는 성공 0, 검증 오류 1, 사용법 오류 2입니다. CLI 제거 명령은 제공하지 않습니다. 제거는 모든 레이아웃·히스토리 참조를 아는 에디터에서 수행하세요.

변경하지 않은 예제의 content hash는 `88d16cbb12cbb96f98521eba4389d19d66789cca5395002227cb9d0dd91b1034`입니다. manifest 원본 UTF-8 byte 길이를 UInt64 little endian으로 붙인 뒤 원본 byte를 해시합니다. 시트를 ordinal ID 순으로 정렬해 ID 길이(UInt32 little endian), UTF-8 ID, PNG 길이(UInt64 little endian), PNG byte를 이어 SHA-256을 계산합니다. BOM을 허용하지 않으며 공백·파일명 변경도 다른 버전입니다.

설치본은 명시한 root의 `iso-objects/objects/<hash>/`에 들어갑니다. `source.iso.json`은 원본이고, `object.iso.json`은 시트 파일명만 `sheet-<png-sha256>.png`로 바꾼 파일입니다. PNG는 원본 그대로 복사하며 checker 배경의 128×128 nearest-neighbor 썸네일은 별도로 생성합니다. 같은 byte를 재가져오면 파일을 다시 쓰지 않습니다.

설치는 원본 built-in 팩과 카탈로그의 전체 한도를 먼저 확인하고 디렉터리 게시 후 카탈로그를 교체합니다. 기존 카탈로그는 `.bak`으로 남습니다. 설치된 source·PNG 해시나 메타데이터가 맞지 않으면 그 가구 전체를 제외하고 진단을 반환합니다. startup은 파일을 고치거나 지우지 않습니다. 레이아웃 Undo는 파일 설치를 되돌리지 않습니다.

## 통합 시 참고

§3.1의 `iso/computer`는 원본 desk variant의 모니터 housing과 상태 파트(`monitor-*`, `rear-15`, `rear-16`), screen/glow descriptor를 그대로 참조합니다. PartId를 새로 만들지 않습니다. 원본 screen descriptor에 PartId가 없으면 참조 clip의 PartId를 사용하세요. `Screen`/`Glow` 필드도 함께 처리해야 합니다. keyboard/mouse가 있는 `rear-14`는 desk에 남습니다. 실제 mounted suppression과 화면 동작은 core/UI 레인 통합 검증 대상입니다.

편집기는 §7.5의 `ValidatePreview(path, CancellationToken)`으로 한 번 캡처한 검증본을 표시하고 `PrepareImport`에 그대로 전달합니다. 이름·variant 수·썸네일·해시는 같은 bytes에서 나옵니다. 원본 파일이 나중에 달라져도 검증본을 가져오며 대화상자는 원본을 다시 읽지 않습니다.

`PrepareImport/PrepareRemove`는 디스크를 쓰지 않습니다. 구성점이 editor/history와 runtime의 live/pending 상태, sheet decoding을 사전 검증한 뒤 `Commit`으로 카탈로그를 게시합니다. 사전 검증 실패와 commit 전 취소는 기존 상태를 유지하고 같은 해시 재가져오기는 소비자 revision도 바꾸지 않습니다. Import/Remove는 CLI 호환용 wrapper입니다. 카탈로그가 커밋된 뒤에는 완료로 처리합니다.
