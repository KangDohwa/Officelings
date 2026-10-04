# Chair v3 인계

## 방석 확대와 베이스 축소 (lane/chairfix2)

`main 1fd86d0`의 4.2×4.2 방석을 등받이 폭과 같은 5.5×5.5로 늘렸다. 작은 원호 모서리는 네 방향 각각 native 4px를 제거하고, z10.5..12의 얇은 가장자리와 0.25 밑면 taper를 유지한다. 스포크 길이 6→3.8, 반경 0.6→0.32, 캐스터 1.6×1.6×2→1×1×1.5, 리프트 폭 1.6→0.9로 베이스를 줄였다. 색상, 의자·골반·손 anchor, phaseContacts, S0 높이, 세 파트 ID/rank는 유지했다.

책상의 실제 구조 알파와 v3 상판·입력기기 geometry로 프레임별 `iso-desk-sandwich`를 만든다. 추가 앞면은 rank10이며 원본 책상 파트의 나머지와 누락·중복 없이 합쳐진다. 인물 원본 픽셀은 그대로다. SE/SW의 책상보다 위에 남는 가려져야 할 인물 픽셀은 검사한 576크롭에서 19,872→0이다. chair front lip의 lap/thigh 가림도 0이다.

SE 돌출은 최대 4px, SW는 5px로 요청한 4px 기준이 아직 실패한다. 기준을 완화하지 않았다. 전체 .NET 560/560, source/pack lint 실패 0이며 Python fit 실패와 기존 source-count 실패는 [현재 검증 기록](../../../../../docs/iso-seated-occlusion.md)에 적었다. PNG와 실행 로그는 `artifacts/chairfix2/`에만 있다.

재생성은 완전한 현재 팩에 `python -B tools/iso_seated_geometry.py`, `python -B tools/build_iso_pack.py --seated-only` 순서로 한다. cap-teen/labcoat-tall/pets-v2 통합은 packv5 범위다. 병합 시 이 lane의 이전 catalog를 새 팩 위에 덮어쓰지 않는다. 아래 절은 이전 작업 기록이다.

## 의자 축소와 허벅지 깊이 수정 (lane/chairslim)

`beb346a`의 방석을 i/j 각각 -2.1..2.1, z10.5..12로 축소했다. 수평 두 축은 7→4.2로 40% 줄었으며 네 모서리를 깊게 깎았다. 등판 폭은 8→5.5, 팔걸이 패드는 길이 4→3과 중심 j=±4.5→±2.9로 좁혔다. 등판의 i=-4..-3.5 위치와 z21 상단은 유지하고 금속 연결대를 넣었다. 베이스·캐스터와 그림자는 기존 값을 쓴다.

착석 골반 프록시 아래쪽을 z12, 허벅지 아래쪽을 z12.1로 맞췄다. 기존 z8 무릎으로 내려가던 허벅지가 방석을 통과하지 않는다. 캐릭터 원본/배포 픽셀, S0 높이 12/21, 골반 (16,44), phaseContacts, 손 접점, (64,80) 의자 앵커, 1×1 footprint, 세 파트 ID와 rank는 유지한다.

6인·4방향·typing/thinking/sleep 252프레임에서 SE/SW 좌우 최대 돌출은 (9~10,9)→(2,1)px이고, lap/thigh를 덮는 front-01 픽셀 합계는 9,793→0이다. 같은 부위의 프록시 z<12 샘플도 8,823→0개다. 모든 착석/전환 444프레임에서 파트의 RGBA 합집합에 틈·중복이 없고 머리·타이핑 손의 새 가림은 0개다. 투명 front 셀은 원본 의자의 정확한 재구성이 증명된 생성 파트에만 허용한다.

재생성은 완성 팩을 먼저 보존한 뒤 `python -B tools/iso_seated_geometry.py`, `python -B tools/build_iso_pack.py --seated-only` 순서다. 새 수치, 측정 ROI, 검증과 PNG 경로는 [의자 축소 기록](../../../../../docs/iso-seated-occlusion.md)에 있다. 아래의 seatfix와 최초 제작 숫자·PNG는 이전 형상이다.

## 이전 좌석 조립 수정 (lane/seatfix)

`tools/iso_seated_geometry.py`로 방석을 z10..12의 얇은 둥근 앞면 박스로 다시 그렸다. 등판은 z12..21의 메쉬 슬래브이며 골반·손·앵커·팔레트·S0 높이는 유지한다. 원본 atlas는 방향당 rear-00 / front-01(방석) / front-00의 세 파트다. 팩은 3D 깊이 비교로 만든 프레임별 마스크를 저장하고 레이아웃에서 캐시한다. 빈 의자 RGBA를 정확히 재구성하며 중복 불투명 픽셀은 없다.

현재 재생성은 `python -B tools/iso_seated_geometry.py` 다음 `python -B tools/build_iso_pack.py --seated-only`다. 이전 `split_iso_chair_occlusion.py` 진입점도 새 모델을 사용한다. 배포 캐릭터 픽셀을 보존해야 하므로 기존 전체 팩을 준비한 뒤 실행한다.

기하, 검사 결과, PNG 위치와 병합 조건은 [현재 착석 가림 기록](../../../../../docs/iso-seated-occlusion.md)에 있다. 아래의 최초 제작 기록과 기존 geometry/measurements/비교 PNG는 이전 5px 방석의 자료다. 현재 모델이나 마스크 검증 결과로 사용하지 않는다.

## 최초 v3 제작 기록

## S0 변경 없음

**정확한 S0 텍스트 diff는 빈 diff다. docs/iso-slice-s0.md를 수정하지 않았다.**

`P(i,j,z)=(32(i-j),16(i+j)-z)`, 128×128 셀, (64,80) 의자 앵커, 1×1 footprint, 방석 12, 등판 21, 착석 골반 (16,44), A/C/H, 방향/파트 이름/일반 지면 깊이/seatGroupId를 유지했다. `furniture-part-convention.md` 표의 예전 `back28`보다 현재 S0와 v2 manifest의 `chair back top 21`을 우선했다. 원본 문서나 다른 레인은 수정하지 않았다.

## 디자인과 겹침 수정

넓고 네모난 v2 방석을 계단식 둥근 윤곽과 5px 두께, 한 픽셀 솟은 중앙 면, waterfall 앞쪽 경사·재봉선으로 다시 그렸다. 배색은 동결 Teal 5색뿐이다. 낮은 메쉬 등판에 얇은 프레임과 요추 곡선을 넣고, 어두운 조절형 팔걸이·가스 리프트·5개 바퀴 접점을 그렸다. 헤드레스트와 로고는 없다.

방석 윗면과 앞쪽 립 전체는 인물 뒤쪽 rear 파트다. NE/NW 등판은 기존 규약대로 front이며, 방석과 겹치는 실제 접합부는 rear에 넣었다. 이 분할은 물체 부품의 접합면을 따른다. 인물별 투명 구멍이나 임의의 소스 y 띠를 사용하지 않았다. 파트의 -20/20 깊이 순서를 바꾸지 않았다.

`chair-parts.png`, `chair.iso.json`, 네 `chair-empty-*.png`, `chair-shadow.png`, `chair-shadow.iso.json`의 파일명과 파트 ID를 그대로 쓴다. atlas 배치와 variant는 v2와 같고, 물리 접점 `anchorChecks`와 제작 provenance만 갱신했다. pack이나 런타임 어댑터는 변경하지 않았다.

## 비교 자료

- `empty-before-after-chair-v3.png`: 네 방향 빈 의자, 1x/3x.
- `contact-before-after-chair-v3-1x.png`, `-3x.png`: v2 책상에서 세 파일럿의 전후 착석.
- `quality-v2-v3-hero-integrated-chair-v3.png`: v2/v3, hero 원본, 통합 렌더와 1x/3x 비교. 통합 렌더는 원래 2x이므로 nearest로 native 1x에 맞췄다.
- `all-seated-phases-chair-v3-1x.png`: 저장된 모든 착석·전환 216프레임.
- `transitions-chair-v3-1x.png`, `-3x.png`: 낮추기 4위상/시작·끝 확대.
- `walkers-chair-v3-1x.png`, `-3x.png`: 독립 지면 깊이로 정렬한 보행 겹침.

## 남은 약점

S0의 등판 21·방석 12를 유지해 뒤쪽에서 등판이 낮고 짧게 보인다. 1x에서는 세밀한 메쉬·곡면이 계단형 윤곽으로 읽히며 제품 사진처럼 매끄럽지 않다. 5개 별형 다리는 2:1/수직 구조 규칙에 맞춰 단순화했고, 작은 바퀴는 실제 이중 캐스터의 분리감이 약하다. NE/NW의 일부 기존 책상 가림과 팔걸이/프레임의 하체 1~2px 가림은 남는다. 방석에 의한 골반·허벅지 가림과 의자에 의한 머리 가림은 0px다.

이 검증은 고정된 actor snapshots와 v2 책상의 정적 합성이다. 최신 live 캐릭터 입력, WPF/Core 실제 렌더 순서·instance tie·연속 재생·DPI는 검증하지 않았다. 기본 팔레트와 접점의 자동 검증이 미술 품질이나 runtime 통합 통과를 대신하지 않는다.

## 마스터와 재현

내장 image generation을 1회 사용했다. `master-chair-v3-attempt1.png`는 형태/재질 참고용이며 최종 시트와 별도다. 이후 native geometry로 다시 투영해 두 시안을 검토하고 채택했다. 최종 프롬프트는 `generation-prompt-chair-v3.txt`에 보존했다. 생성 이미지는 다른 레인의 자산을 편집하거나 잘라 쓰지 않았다.

유일한 새 helper `chair-v3-build-review.py`는 모든 쓰기를 자기 폴더로 제한한다. 읽은 v2와 actor 입력의 SHA-256을 검사한다. 기존 Python/Pillow/numpy를 사용했으며 설치나 dotnet 테스트를 하지 않았다.

```powershell
& 'C:\Users\myori\.agents\document-toolchain\runtime\python\Scripts\python.exe' -B 'assets/art/iso/furniture-v3/chair/chair-v3-build-review.py'
& 'C:\Users\myori\.agents\document-toolchain\runtime\python\Scripts\python.exe' -B 'assets/art/iso/furniture-v3/chair/chair-v3-build-review.py' --finalize-only
```
