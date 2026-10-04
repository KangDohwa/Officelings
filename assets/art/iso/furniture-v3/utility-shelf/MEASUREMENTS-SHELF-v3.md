# 책장 기울기 측정·원인·검수

**주원인: 생성 원본을 목표 bbox에 가로/세로 비균등 축소한 v2 cleanup.** 원점 이동이나 한 번의 픽셀 반올림으로 설명되는 오차가 아니다.

`assets/art/iso/furniture-v2/utility/runs/a5-v2/build.py`의 clean_master는 alpha bbox를 가이드 bbox에 NEAREST로 resize한다. 원본 앞면 bbox는 (280,162,977,1117)=697×955, 가이드 bbox는 (30,19,99,98)=69×79다. 따라서 sx=69/697=0.099137931, sy=79/955=0.082722513, sy/sx=0.835617가 된다. 원본 긴 윗변의 OLS 기울기 −0.491878은 이 affine fit 뒤 −0.410432가 되며, 실제 SE 측정 −0.411893과 일치한다. 원본 master는 2:1에 가까웠으므로 master 자체보다 이 bbox fit이 주원인이다.

SE는 앞면 fitted master를 직접 사용하고 NE는 후면 fitted master를 사용한다. SW/NW는 canonical 3D plane sampling으로 재투영되어 긴 변이 대체로 ±0.5로 돌아간다. 반사 시 조명 보정은 이미 있었으며 주된 기울기 원인은 아니다. 기존 앵커와 모델 배치 계약은 보존했다. 어떤 고정 원점/벽 오프셋 이동도 선의 기울기를 바꿀 수 없으므로 앵커 수정으로 고치지 않았다.

| 방향 | 변 | 실제 기울기 | 기준 | 길이 px | 누적오차 px |
|---|---|---:|---:|---:|---:|
| SE | top-long | -0.411893 | -0.5 | 55 | 4.846 |
| SE | top-short | +0.440559 | +0.5 | 11 | 0.654 |
| SE | base-short | +0.525000 | +0.5 | 14 | 0.350 |
| SE | base-long | -0.379939 | -0.5 | 52 | 6.243 |
| SE | upright-left | -0.000000 | +0.0 | 41 | 0.000 |
| SE | upright-right | -0.000000 | +0.0 | 46 | 0.000 |
| SW | top-short | -0.434066 | -0.5 | 12 | 0.791 |
| SW | top-long | +0.501048 | +0.5 | 52 | 0.055 |
| SW | base-long | +0.500019 | +0.5 | 53 | 0.001 |
| SW | base-short | -0.418182 | -0.5 | 10 | 0.818 |
| SW | upright-left | -0.000000 | +0.0 | 37 | 0.000 |
| SW | upright-right | -0.000000 | +0.0 | 38 | 0.000 |
| NE | top-short | -0.450549 | -0.5 | 12 | 0.593 |
| NE | top-long | +0.411305 | +0.5 | 52 | 4.612 |
| NE | base-long | +0.375438 | +0.5 | 51 | 6.353 |
| NE | base-short | -0.601399 | -0.5 | 11 | 1.115 |
| NE | upright-left | -0.000000 | +0.0 | 46 | 0.000 |
| NE | upright-right | +0.000000 | +0.0 | 38 | 0.000 |
| NW | top-long | -0.500000 | -0.5 | 50 | 0.000 |
| NW | top-short | +0.434066 | +0.5 | 12 | 0.791 |
| NW | base-short | +0.428571 | +0.5 | 7 | 0.500 |
| NW | base-long | -0.503145 | -0.5 | 52 | 0.164 |
| NW | upright-left | +0.000000 | +0.0 | 34 | 0.000 |
| NW | upright-right | -0.000000 | +0.0 | 37 | 0.000 |

v3의 SE/NW 지면 꼭짓점은 (30,91),(86,63),(98,69),(42,97), 상단은 각 y−44다. SW/NE는 x′=128−x 위치에서 새로 그렸다. 긴 변 Δx56/Δy28, 짧은 변 Δx12/Δy6, 수직 Δx0/Δy44. 네 방향 48개 구조선의 endpoint drift는 정확히 0이다. pixel-center 래스터 회귀에서는 표본 끝의 계단 위상 때문에 최대 0.026316px 차이가 남는다.

| 비교 | 파일 |
|---|---|
| v2/v3 네 방향 native1× 및 정확한3× 확대 | [before-after-1x3x.png](before-after-1x3x.png) |
| 실제 frozen wall 모듈과 평행 기준선 | [wall-alignment-before-after.png](wall-alignment-before-after.png) |
| hero 및 제공된 최근 통합 render | [hero-integrated-comparison.png](hero-integrated-comparison.png) |

시각 검수: v2의 SE/NE 상단/밑판 선이 벽과 벌어지는 현상이 줄었고, v3의 프레임·밑판·책 상단은 서로 평행하다. SW/NE의 넓은 왼쪽 면, SE/NW의 좁은 왼쪽 면이 각각 밝다. 책 배열은 각 층마다 다른 폭/높이/색 조합을 쓴다. 새 master는 두 번 그렸고, 참조/비교/감사 보드 열람은 총 13개 이내다.

남은 약점: v3의 넓은 목재 면은 hero보다 평평하고 깨끗하며, 책 제목/표지 표현은 몇 픽셀의 띠에 제한된다. strict 2:1 때문에 둥근 모서리·기울어진 책의 자연스러움은 줄였다. 통합 이미지 비교는 원본 screenshot을 옆에 둔 것이며, 새 자산을 실제 앱에 설치한 render가 아니다. 런타임 벽 부착 간격·보행/배우 가림·pack 소비는 이번 lane에서 검증하지 않았다.

전체 감사는 [SKEW-AUDIT.md](SKEW-AUDIT.md), 정확한 표본 좌표/길이는 CSV/JSON, 구조 endpoint는 shelf-geometry.json, 재현 명령/검증은 아래와 validation.txt에 있다. 다른 가구는 수정하지 않았다.

```powershell
& 'C:/Users/myori/.agents/document-toolchain/runtime/python/Scripts/python.exe' -B assets/art/iso/furniture-v3/utility-shelf/measure_redraw_shelf_v3.py --audit --verify
& 'C:/Users/myori/.agents/document-toolchain/runtime/python/Scripts/python.exe' -B tools/iso_check.py assets/art/iso/furniture-v3/utility-shelf --tolerance 0
```

재그리기까지 재현하려면 첫 명령에 --build를 추가한다. helper의 모든 쓰기 대상은 자신의 출력 폴더다. --inspect는 참조 보드를 다시 만든다.
