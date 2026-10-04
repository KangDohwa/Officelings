# Chair v3 검증 기록

v2 책상과 rev2-source/actor-inputs의 고정된 세 캐릭터를 합성했다. 최종 출력 파트와 실제 깊이 키를 정렬한 뒤 전체 화면의 픽셀 소유권을 계산했고, 인물 셀에서 별도로 계산한 가림 마스크와 일치하는지 확인했다. 캐릭터·책상 픽셀과 분모는 전후 동일하다.

| 인물 방향 | 저장된 착석·전환 프레임 | 머리 최저 가시성 | 의자에 가린 머리 최대 | 타이핑 손 | 방석이 골반·허벅지를 덮은 픽셀 |
| --- | ---: | ---: | ---: | ---: | ---: |
| SE | 48 | 100.00% | 0 | 100% | 0 |
| SW | 48 | 100.00% | 0 | 100% | 0 |
| NE | 60 | 86.97% | 0 | 100% | 0 |
| NW | 60 | 91.18% | 0 | 100% | 0 |

타이핑·생각·앉기·일어나기·잠자기: 최종 216프레임, v2와 합쳐 432프레임. 타이핑 손은 rev2와 같은 피부색·접점 반경 2 ROI로 48프레임을 검사했다. 다른 착석 클립에는 손 접점이 없어 노출된 상체 피부 전체를 별도 ROI로 검사했고 가시성 감소가 없었다. 이를 손만의 100% 판정으로 바꾸어 보고하지 않는다.

NE/NW 머리 최저치는 기존 v2 책상의 가림에서 발생했다. 의자는 모든 216프레임에서 머리를 0px 가렸다. 팔걸이/프레임은 골반·허벅지 ROI를 NE 최대 1px, NW 최대 2px 덮으며, 방석 덮임은 전 방향 0px다.

보행은 의자 주위 8개 위치 × 4방향 × 4위상 × 3인물 × 4의자 방향 × 전후 = 3,072건이다. 의자와 보행자는 각자의 일반 지면 깊이를 사용했다. 의자보다 앞인 보행자 머리는 100% 보이고, 모든 사례에서 독립 가림 계산과 화면 소유권이 일치했다. 전환의 발바닥은 지면에 고정된다.

셀 128×128, 시트 512×256, 앵커 (64,80), 방석 꼭대기 z12/최대 두께 5px, 등판 z21, 팔걸이 z13. 투영된 실제/스냅된 구조 외곽 1,200개는 모두 2:1 또는 수직이다. 시트는 Teal 5색만 사용하며 알파는 0/255다. 그림자는 Navy #111924, 알파 0/48/96, cast offset (6,3), 실제 바퀴 접촉점을 사용한다.

체커: 16항목, 0실패. floor seam, 비가구 shadow의 part 이름, character face-lock/blink만 해당 없음으로 SKIP했다. 파트 마스크는 서로 겹치지 않으며 네 empty PNG를 RGBA까지 정확히 복원한다. 기존 파트 ID, 시트 ID, 위치, 이름, footprint, 변형, 앵커, 깊이 키와 seatGroupId를 보존했다.

픽셀 단위 결과·깊이 큐·입력 SHA-256은 measurements-chair-v3.json, 구조 외곽은 geometry-chair-v3.json, 체커 전문은 validation-chair-v3.txt에 있다. v2와 읽은 입력 파일의 SHA-256은 검증 전후 동일했다.

## 타이핑 프레임

| 인물 | 인물/책상 방향 | 위상 | 머리 | 손 | 골반·허벅지 의자 가림 |
| --- | --- | ---: | ---: | ---: | ---: |
| dev-hoodie-v2 | NW/SE | 0 | 247/268 | 17/17 | 0px |
| dev-hoodie-v2 | NW/SE | 1 | 247/268 | 18/18 | 0px |
| dev-hoodie-v2 | NW/SE | 2 | 247/268 | 17/17 | 0px |
| dev-hoodie-v2 | NW/SE | 3 | 247/268 | 17/17 | 0px |
| dev-hoodie-v2 | NE/SW | 0 | 243/268 | 17/17 | 0px |
| dev-hoodie-v2 | NE/SW | 1 | 243/268 | 18/18 | 0px |
| dev-hoodie-v2 | NE/SW | 2 | 243/268 | 17/17 | 0px |
| dev-hoodie-v2 | NE/SW | 3 | 243/268 | 17/17 | 0px |
| dev-hoodie-v2 | SW/NE | 0 | 284/284 | 17/17 | 0px |
| dev-hoodie-v2 | SW/NE | 1 | 284/284 | 14/14 | 0px |
| dev-hoodie-v2 | SW/NE | 2 | 284/284 | 17/17 | 0px |
| dev-hoodie-v2 | SW/NE | 3 | 284/284 | 18/18 | 0px |
| dev-hoodie-v2 | SE/NW | 0 | 284/284 | 17/17 | 0px |
| dev-hoodie-v2 | SE/NW | 1 | 284/284 | 14/14 | 0px |
| dev-hoodie-v2 | SE/NW | 2 | 284/284 | 17/17 | 0px |
| dev-hoodie-v2 | SE/NW | 3 | 284/284 | 18/18 | 0px |
| punk-girl | NW/SE | 0 | 217/238 | 21/21 | 0px |
| punk-girl | NW/SE | 1 | 217/238 | 22/22 | 0px |
| punk-girl | NW/SE | 2 | 217/238 | 23/23 | 0px |
| punk-girl | NW/SE | 3 | 217/238 | 20/20 | 0px |
| punk-girl | NE/SW | 0 | 243/267 | 21/21 | 0px |
| punk-girl | NE/SW | 1 | 243/267 | 22/22 | 0px |
| punk-girl | NE/SW | 2 | 243/267 | 21/21 | 0px |
| punk-girl | NE/SW | 3 | 243/267 | 20/20 | 0px |
| punk-girl | SW/NE | 0 | 260/260 | 14/14 | 0px |
| punk-girl | SW/NE | 1 | 260/260 | 14/14 | 0px |
| punk-girl | SW/NE | 2 | 260/260 | 14/14 | 0px |
| punk-girl | SW/NE | 3 | 260/260 | 16/16 | 0px |
| punk-girl | SE/NW | 0 | 262/262 | 17/17 | 0px |
| punk-girl | SE/NW | 1 | 262/262 | 16/16 | 0px |
| punk-girl | SE/NW | 2 | 262/262 | 15/15 | 0px |
| punk-girl | SE/NW | 3 | 262/262 | 18/18 | 0px |
| professor | NW/SE | 0 | 217/238 | 17/17 | 0px |
| professor | NW/SE | 1 | 217/238 | 16/16 | 0px |
| professor | NW/SE | 2 | 217/238 | 17/17 | 0px |
| professor | NW/SE | 3 | 217/238 | 18/18 | 0px |
| professor | NE/SW | 0 | 211/238 | 17/17 | 0px |
| professor | NE/SW | 1 | 211/238 | 16/16 | 0px |
| professor | NE/SW | 2 | 211/238 | 17/17 | 0px |
| professor | NE/SW | 3 | 211/238 | 17/17 | 0px |
| professor | SW/NE | 0 | 254/254 | 14/14 | 0px |
| professor | SW/NE | 1 | 254/254 | 11/11 | 0px |
| professor | SW/NE | 2 | 254/254 | 14/14 | 0px |
| professor | SW/NE | 3 | 254/254 | 12/12 | 0px |
| professor | SE/NW | 0 | 254/254 | 14/14 | 0px |
| professor | SE/NW | 1 | 254/254 | 11/11 | 0px |
| professor | SE/NW | 2 | 254/254 | 14/14 | 0px |
| professor | SE/NW | 3 | 254/254 | 12/12 | 0px |
