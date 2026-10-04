# TV v3 결과 및 검증

검은 OLED형 패널, 최소 테두리와 하단 턱, 평평한 금속 받침, 손잡이 없는 목재 콘솔과 선반을 제작했다. 브랜드 표시는 없다. 네 방향은 같은 모델을 각각 투영하고 조명을 다시 계산한 원본 픽셀이다. 고해상도 생성 이미지가 필요하지 않은 형태라 네이티브 픽셀로 직접 제작했다. 방향별 시안은 2회 검토했다.

**S0 예외는 TV 상단 38 → 48 하나다.** 정확한 원문 diff와 유지한 상수는 [CHANGES-TV-v3.md](CHANGES-TV-v3.md)에 기록했다. 원본 S0 문서와 part convention은 수정하지 않았다.

납품 manifest는 `tv-cabinet.iso.json`, `tv-cabinet-shadow.iso.json`이다. 그림자 sheet는 각 variant의 `shadow`로 연결되어 있다. `tv-cabinet-parts.png`의 정적 부품은 서로 겹치지 않으며, 화면은 정적 본체의 구멍을 채운다. 넓은 콘솔 표면과 패널 본체는 지면 셀별로 나눴다. `depthAnchor`는 지면 좌표이며, 일반 부품 rank 0 → 화면 rank 1 → 글로우 rank 2 순서다. 원점은 alpha 범위로 다시 맞추지 않았다.

화면은 v2의 4프레임·3fps·loop 상태를 옮겼다. `default`/`playing`은 4프레임, `idle`/`static`은 첫 프레임 정지, `frame-00`부터 `frame-03`은 각 프레임 정지다. 재생 버튼은 1x에서 읽히도록 다시 그렸다. NE/NW는 숨겨진 화면 대신 기존 상태 LED를 유지한다. 모든 방향에서 default 4프레임의 SHA256이 서로 달라 프레임 손실 없이 유지된 것을 확인했다. 애니메이션 변화는 v2처럼 작다.

`tv-cabinet-glow.png`는 화면과 분리되어 있으며 alpha는 0/255다. `glow`의 기본 opacity는 0.18이다. 정지 화면은 `glowStates`에 기록된 대응 프레임을 고정한다. 글로우는 화면 하단 림/후면 LED 마스크 안에만 있으며 바닥이나 배우에 칠해져 있지 않다. 이는 작은 글로우 표현이며 실시간 광원은 아니다.

| 측정 | v2 | v3 |
| --- | ---: | ---: |
| 정면 화면의 가로 투영 폭 | 32px | 54px |
| 정면 화면의 수직 높이 | 15px | 28px |
| SE/SW 화면 마스크 면적 | 480px | 1512px |
| TV 상단 | 38 | 48 |
| 캐비닛 상단 | 16 | 16 |
| 본 패널 두께 투영 | — | 2×1px |
| 후면 하단 전자부 최대 두께 투영 | — | 6×3px |

| 방향 | 정적 부품 | 정적 중복 픽셀 | 빈 물체 RGBA 재조립 오차 | 모델 모서리 검사 |
| --- | ---: | ---: | ---: | ---: |
| SE | 12 | 0 | 0 | 244/244 |
| SW | 11 | 0 | 0 | 244/244 |
| NE | 9 | 0 | 0 | 244/244 |
| NW | 9 | 0 | 0 | 244/244 |

각 모델 모서리는 스냅한 끝점에서 `dx=0` 또는 `abs(dx)=2*abs(dy)`를 만족한다. 바닥 평면의 수평선이나 임의 기울기를 사용하지 않았다. 본체·화면·글로우의 모든 불투명 RGB는 frozen ramp에 정확히 포함되며, 투명 RGB는 0이다. 그림자는 navy `#111924`, alpha 0/48/96, cast offset (6,3)을 사용한다.

최종 PNG와 manifest를 디스크에서 다시 읽어 다음을 검증했다. 모델 생성 중의 배열만 비교한 결과가 아니다.

- 선언한 part를 실제 깊이 순서로 합쳤을 때 네 방향 empty reference와 RGBA가 정확히 일치한다.
- 정적 부품 중복, 화면과 정적 부품의 중복, 화면 프레임 간 alpha 마스크 변화는 모두 0이다.
- 모든 정지 상태와 개별 프레임 선택은 해당 default 프레임과 RGBA가 같다.
- part 선택자의 ID·방향·depthAnchor·rank가 실제 clip과 일치한다.
- v2의 rotation·orientation·footprint·blockedCells·height·points·layer를 유지했다.
- anchorChecks는 받침/콘솔 접합면, 패널 하단 림, LED 표면의 실제 픽셀에 있다. 재생 버튼을 접촉 픽셀로 삼지 않았다.
- v2 TV 파일 13개의 SHA256을 생성 전후 대조해 변경이 없음을 확인했다.
- CHANGES의 두 삭제 행이 실제 원본 문서의 행과 정확히 일치한다.

실행한 checker 명령:

```text
<installed-python> -B tools/iso_check.py --tolerance 0 assets/art/iso/furniture-v3/lounge-tv
```

결과: **32 checks; 0 failed**. 23 PASS와 9 SKIP이다. SKIP은 floor seam(4), character face/blink(4), shadow sheet의 furniture depth-name 검사(1)로, 모두 TV 자산에 적용되지 않는 검사다. 상세 로그는 [iso-check-tolerance-0.txt](iso-check-tolerance-0.txt), 측정 원본은 [validation-measurements.json](validation-measurements.json), 모델 모서리와 접촉 픽셀은 [geometry-reconstruction-measurements.json](geometry-reconstruction-measurements.json)에 있다. 전체 dotnet 테스트는 실행하지 않았다.

[before-after.png](before-after.png)에서 v2와 v3를 동일한 1x/정확한 nearest-neighbor 3x로 나란히 검수했다. 같은 이미지 상단에는 지정된 히어로 TV와 최근 통합 렌더의 TV 크롭이 있다. 히어로의 목재·검은 테두리·청록 화면 관계를 유지하면서, 최근 렌더에서 보이는 깊은 CRT 상자를 얇은 대형 패널로 바꿨다. [tv-cabinet-four-views-3x.png](tv-cabinet-four-views-3x.png), [tv-cabinet-screen-states-3x.png](tv-cabinet-screen-states-3x.png), [tv-cabinet-overlays-proof.png](tv-cabinet-overlays-proof.png)도 검수했다. 검수한 이미지는 총 10개로, 요청된 약 14개 한도 안이다.

남은 한계: 화면의 펴낸 좌표 비율은 54:28 ≈ 1.93:1로 실물 16:9를 정밀하게 복제하지 않았다. 콘솔은 히어로보다 표면 무늬가 단순하며 후면은 어두운 단색에 가깝다. 애니메이션과 글로우는 원래 상태를 유지하는 작은 변화다. 이 lane에서는 팩·런타임·통합 렌더를 수정하지 않았고, 실제 배우와의 WPF 가림/alpha picking은 검증하지 않았다. 패키징 시 TV 상단 48 예외와 새 sheet ID, 정지 글로우 프레임 연결을 반영해야 한다.

재생성 helper: `build_tv_oled_v3_20261004.py`를 `-B`로 실행하면 이 폴더만 쓴다. `--verify`는 납품 파일을 읽고 검증 로그/측정을 이 폴더에 남긴다. 외부 다운로드·설치·외부 실행기는 사용하지 않는다.
