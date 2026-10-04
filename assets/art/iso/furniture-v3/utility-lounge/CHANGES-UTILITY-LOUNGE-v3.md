# Utility·lounge v3 변경 기록

**Frozen S0 변경 없음. `docs/iso-slice-s0.md`의 정확한 텍스트 diff는 비어 있다.**

```diff
--- docs/iso-slice-s0.md (before)
+++ docs/iso-slice-s0.md (after)
# No changed lines. The source file was not written.
```

이번 출력은 소파, 사이드 테이블, 정수기, 냉장고, 커피 머신의 SE/SW/NE/NW를 다시 그렸다. 램프의 수직 줄기는 이미 정확했고 식물·원형 화분에는 직선 기울기 기준이 적용되지 않아 원본을 그대로 내보냈다. TV장, 책장, 책상·모니터·의자는 다루지 않았다.

128×128 셀과 scale:1, P(i,j,z)=(32(i−j),16(i+j)−z), 셀 앵커, footprint/blockedCells, 상호작용 좌표, 부품 이름과 atlas 위치, depthAnchor, isoZOrder, seatGroupId는 원본과 같다. 소파 좌면 12, 등받이 24, 사이드 테이블 상판 16, 정수기 전체 40, 냉장고 44, 커피 머신 28도 유지했다. 네 소파의 seatGround·chairSeatAnchor·seatProfiles를 그대로 두었고 C=P(seatGround)+(0,−12), 배우의 source pelvis (16,44)는 바꾸지 않았다.

그림자는 일곱 품목 모두 원본 PNG와 바이트 단위로 같다. 기존 접촉/투사 범위와 (+6,+3), alpha 0/48/96를 보존했다. body는 alpha 0/255, 투명 RGB 0이며 RGB는 S0의 여덟 ramp 안에서만 골랐다. 회전마다 화면 왼쪽 면을 더 밝게 그렸다.

책장 v3처럼 native 픽셀 중심에서 3D 평면을 직접 샘플링했다. bbox를 가로·세로 따로 축소하지 않는다. 정수 꼭짓점의 모든 구조선이 ±0.5 또는 수직이며, 기록한 1,008개 cuboid edge의 endpoint drift는 0px다. 원래 v2 manifest는 구조 bounds를 공개하지 않으므로 아래의 private bounds 변경을 frozen S0 변경으로 취급하지 않는다.

정수기·냉장고·커피 머신은 v2 utility builder의 bounds를 최소 격자로 보정했다. 이 보정의 이상적 꼭짓점 이동은 x 최대 1.36px, y 최대 0.68px다. 높이는 그대로다.

```diff
- appliance ground bounds (i0,j0,i1,j1) = (-0.22,-0.20,0.22,0.20)
+ appliance ground bounds (i0,j0,i1,j1) = (-0.25,-0.1875,0.25,0.1875)
```

소파의 새 private bounds는 base i=−0.4375..0.4375, j=−0.4375..2.4375, z=3..8이다. 등받이는 z=8..24, 쿠션은 z=8..12, 팔걸이는 z=5..20이다. 마지막 검수에서 팔걸이 윗면을 첫 시안의 19에서 20으로 높였다. 뒤쪽 팔걸이와 등받이 접합부의 짧은 꺾임을 줄이는 1px 변경이며, S0의 좌면·등받이·앵커·접점은 유지한다. 낮은 발, 두꺼운 팔걸이, 쿠션 가장자리·중앙 음영, 크림 쿠션과 휴대폰을 넣었다.

사이드 테이블은 i/j=−0.4375..0.4375, 상판 z=13..16이다. 세 다리의 면과 물리적 상단 연결부를 기존 아홉 rear 부품에 할당했고, 상판·컵·접은 종이는 기존 front 부품에 들어간다. 임의 화면 y 띠로 자르지 않았다. 소파도 원래 지면 셀별 rear/front strip과 가운데 seat group을 유지했다. NE/NW 등받이는 기존 규칙대로 front 가림 부품에 할당한다.

Native 디자인 검수는 세 번으로 끝냈다. 새 이미지 생성은 0회다. 이 크기의 직선 구조는 native 재그리기로 보정했으며, 생성 원본을 다시 bbox에 맞추는 과정을 쓰지 않았다. 비교·원본 보드는 총 11개 이내로 열람했다.

모든 쓰기는 이 폴더 안에서만 했다. 기존 v2, 다른 lane, docs/src/tests/tools, pack은 수정하지 않았다. 커밋·git staging·전체 dotnet test·업로드·배포는 하지 않았다.
