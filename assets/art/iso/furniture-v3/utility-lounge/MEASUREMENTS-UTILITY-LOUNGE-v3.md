# Utility·lounge v3 기울기와 검수

소파·정수기·냉장고·커피 머신의 >1px 구간을 보정했다. 사이드 테이블도 정확한 구조 평면으로 다시 그렸다. 최종 native 래스터의 최대 누적오차는 소파 0.455px, 세 가전 0.088px, 테이블 0.052px다. 구조 꼭짓점 오차는 모두 0px다.

책장 v3의 `measure_redraw_shelf_v3.py`를 읽기 전용으로 가져와 같은 `color_mask`, `measure_mask`, `fit_edge`를 사용했다. manifest 부품을 합성한 native 1x를 측정하고, 최소 8개 연속 표본과 꺾임 분할 epsilon 1.05px를 유지했다. 대각선 누적오차는 |m−sign(m)×0.5|×Δx, 수직은 |dx/dy|×Δy다. 각 표의 값은 해당 방향에서 관찰된 선분의 최대값이다. 전/후의 worst edge가 서로 다를 수 있다.

| 품목 | 방향 | v2 최대 오차 px | v3 최대 오차 px | 결과 |
|---|---|---:|---:|---|
| 소파 | SE | 1.390 | 0.167 | ≤1px |
| 소파 | SW | 1.390 | 0.167 | ≤1px |
| 소파 | NE | 1.167 | 0.455 | ≤1px |
| 소파 | NW | 1.404 | 0.455 | ≤1px |
| 사이드 테이블 | SE | 0.365 | 0.052 | ≤1px |
| 사이드 테이블 | SW | 0.365 | 0.052 | ≤1px |
| 사이드 테이블 | NE | 0.400 | 0.052 | ≤1px |
| 사이드 테이블 | NW | 0.000 | 0.052 | 두 버전 모두 ≤1px |
| 정수기 | SE | 1.780 | 0.088 | ≤1px |
| 정수기 | SW | 0.659 | 0.088 | ≤1px |
| 정수기 | NE | 1.545 | 0.088 | ≤1px |
| 정수기 | NW | 0.527 | 0.088 | ≤1px |
| 냉장고 | SE | 1.555 | 0.088 | ≤1px |
| 냉장고 | SW | 0.455 | 0.088 | ≤1px |
| 냉장고 | NE | 1.733 | 0.088 | ≤1px |
| 냉장고 | NW | 1.009 | 0.088 | ≤1px |
| 커피 머신 | SE | 1.300 | 0.088 | ≤1px |
| 커피 머신 | SW | 0.692 | 0.088 | ≤1px |
| 커피 머신 | NE | 0.538 | 0.088 | ≤1px |
| 커피 머신 | NW | 1.250 | 0.088 | ≤1px |
| 램프 줄기 | SE | 0.000 | 0.000 | 원본 보존 |
| 램프 줄기 | SW | 0.000 | 0.000 | 원본 보존 |
| 램프 줄기 | NE | 0.000 | 0.000 | 원본 보존 |
| 램프 줄기 | NW | 0.000 | 0.000 | 원본 보존 |
| 식물 | SE | — | — | 유기체·원형 화분, 직선 기준 적용 불가 |
| 식물 | SW | — | — | 유기체·원형 화분, 직선 기준 적용 불가 |
| 식물 | NE | — | — | 유기체·원형 화분, 직선 기준 적용 불가 |
| 식물 | NW | — | — | 유기체·원형 화분, 직선 기준 적용 불가 |

정수기 병·컵의 원형 림, 램프의 원뿔 갓·타원형 받침, 식물 잎·둥근 화분은 직선 구조 측정에서 제외했다. 소파는 red 구조 mask, 테이블은 wood mask, 정수기는 cream/metal chassis mask, 냉장고·커피 머신은 alpha mask, 램프는 wood 줄기의 수직 경계다. 소파의 크림 쿠션과 휴대폰은 원본 감사와 동일하게 소재 mask에서 제외하지만 새 cuboid의 모든 edge는 구조 꼭짓점 검사에 포함한다.

첫 두 시안의 소파 NE/NW에는 서로 정확한 두 직선이 만나는 짧은 꺾임을 한 회귀 구간으로 묶어 1.167px가 나오는 부분이 있었다. 최종 시안은 팔걸이 윗면을 1px 높여 그 접합부를 줄였고, 같은 측정기를 다시 실행해 표의 값으로 내려갔다. 측정기를 느슨하게 바꾸거나 표본을 삭제하지 않았다.

| 비교·증거 | 파일 |
|---|---|
| 소파 네 방향 1x/3x | [before-after-sofa-1x3x.png](before-after-sofa-1x3x.png) |
| 테이블·세 가전 네 방향 1x/3x | [before-after-utility-1x3x.png](before-after-utility-1x3x.png) |
| 보존한 램프·식물 1x/3x | [before-after-radial-1x3x.png](before-after-radial-1x3x.png) |
| hero·최근 통합 화면 옆의 v2/v3 1x/3x | [hero-integrated-before-after-1x3x.png](hero-integrated-before-after-1x3x.png) |
| 네 방향과 S0 벽 기울기 기준선 | [wall-parallel-before-after.png](wall-parallel-before-after.png) |
| 선분 좌표·기울기·표본 수·잔차 | [skew-edge-measurements.csv](skew-edge-measurements.csv), [JSON](skew-edge-measurements.json) |
| 품목별 측정 범위 | [skew-coverage.json](skew-coverage.json) |
| 1,008개 구조 꼭짓점 | [structural-geometry.json](structural-geometry.json) |
| 검증과 checker 원문 | [validation.txt](validation.txt), [iso-check-tolerance0.txt](iso-check-tolerance0.txt) |

최종 검사: `tools/iso_check.py`를 이 출력 디렉터리에 `--tolerance 0`으로 실행했다. **112개 검사, 실패 0건.** SKIP은 가구·그림자에 해당하지 않는 floor 6×6 seam, character face-lock/blink, shadow의 furniture depth-part 검사뿐이다. 별도 검증은 116개 body 부품의 mask 비중첩·28개 완전 재합성, 136개 clip의 보존 필드, 네 소파의 pelvis 접점, frozen ramp 소속, 그림자 바이트 동일성을 확인했다.

시각 검수에서 v2의 기울어진 외곽과 가전 모서리가 정리됐고, 문손잡이·급수구·추출창을 1x에서 구별할 수 있다. 소파는 쿠션의 두께와 팔걸이·밑판을 분리했다. 벽 비교선은 S0의 (+32,+16), (−32,+16)에서 계산한 기울기 가이드다. 실제 벽 부착 위치 검증은 아니다.

남은 약점: v3는 hero와 v2의 세밀한 직물·목재 질감보다 깨끗하고 단순하다. 소파 쿠션의 음영은 부드러움을 암시하지만 외곽은 여전히 각져 보인다. 원형 병과 컵의 묘사도 단순화됐다. 기울기와 읽기 개선을 전체 화풍의 완성으로 주장하지 않는다. 최근 통합 이미지는 제공된 원본을 옆에 둔 비교이며, v3를 설치한 앱 render가 아니다. 최종 배우의 가림·벽 부착 간격·런타임 소비는 검증하지 않았다.

재현은 `-B`로 실행한다. 읽기 전용으로 가져오는 다른 lane의 Python 파일에 bytecode를 쓰지 않기 위해서다. 세 번의 디자인 검수가 끝났으므로 아래 기본 명령은 재그리기 없이 측정·검증만 한다.

```powershell
& 'C:/Users/myori/.agents/document-toolchain/runtime/python/Scripts/python.exe' -B assets/art/iso/furniture-v3/utility-lounge/redraw_measure_utility_lounge_v3.py --audit --verify
& 'C:/Users/myori/.agents/document-toolchain/runtime/python/Scripts/python.exe' -B tools/iso_check.py assets/art/iso/furniture-v3/utility-lounge --tolerance 0
```

출력 재생성이 필요하면 helper에 `--build --review --context`를 추가한다. 모든 파일 쓰기 대상은 helper 자신의 디렉터리다.
