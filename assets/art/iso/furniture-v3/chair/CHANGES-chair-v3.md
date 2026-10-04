# Chair v3 인계

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
