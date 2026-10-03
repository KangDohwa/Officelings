# A3 rev1 실제 파츠 가림 측정

4 typing phase 합산. 고정 canonical head 및 원래 torso/skin-contact ROI를 사용하며 분모를 보존했다. ROI는 수동 분리한 전체 해부학 영역이 아니다.

| 캐릭터 | desk / worker | 수정 전 머리 | 수정 후 머리 | 수정 후 몸통 ROI | 수정 후 손 ROI |
|---|---|---:|---:|---:|---:|
| dev-hoodie-v2 | SE / NW | 480/1072 (44.8%) | 960/1072 (89.6%) | 272/324 (84.0%) | 41/41 (100.0%) |
| dev-hoodie-v2 | SW / NE | 272/1072 (25.4%) | 944/1072 (88.1%) | 308/360 (85.6%) | 41/41 (100.0%) |
| dev-hoodie-v2 | NE / SW | 1120/1136 (98.6%) | 1132/1136 (99.6%) | 248/248 (100.0%) | 0/35 (0.0%) |
| dev-hoodie-v2 | NW / SE | 1092/1136 (96.1%) | 1116/1136 (98.2%) | 220/276 (79.7%) | 0/35 (0.0%) |
| punk-girl | SE / NW | 432/952 (45.4%) | 868/952 (91.2%) | 256/352 (72.7%) | 43/43 (100.0%) |
| punk-girl | SW / NE | 292/1068 (27.3%) | 948/1068 (88.8%) | 296/360 (82.2%) | 34/34 (100.0%) |
| punk-girl | NE / SW | 976/1040 (93.8%) | 1040/1040 (100.0%) | 327/327 (100.0%) | 0/43 (0.0%) |
| punk-girl | NW / SE | 1004/1048 (95.8%) | 1048/1048 (100.0%) | 275/333 (82.6%) | 0/37 (0.0%) |
| professor | SE / NW | 528/952 (55.5%) | 868/952 (91.2%) | 360/436 (82.6%) | 68/68 (100.0%) |
| professor | SW / NE | 276/952 (29.0%) | 844/952 (88.7%) | 384/444 (86.5%) | 67/67 (100.0%) |
| professor | NE / SW | 1000/1016 (98.4%) | 1016/1016 (100.0%) | 368/368 (100.0%) | 0/51 (0.0%) |
| professor | NW / SE | 988/1016 (97.2%) | 1016/1016 (100.0%) | 332/380 (87.4%) | 0/51 (0.0%) |

## 수정 후 손 ROI topmost 가림 원인 (phase 0)

| 캐릭터 / worker | part: 가림 픽셀 |
|---|---|
| dev-hoodie-v2 / NW | 없음 |
| dev-hoodie-v2 / NE | 없음 |
| dev-hoodie-v2 / SW | desk__front__ne__06: 2, desk__monitor__ne__00: 7 |
| dev-hoodie-v2 / SE | desk__front__nw__04: 2, desk__monitor__nw__01: 7 |
| punk-girl / NW | 없음 |
| punk-girl / NE | 없음 |
| punk-girl / SW | desk__front__ne__06: 2, desk__monitor__ne__00: 8 |
| punk-girl / SE | desk__front__nw__04: 2, desk__monitor__nw__01: 7 |
| professor / NW | 없음 |
| professor / NE | 없음 |
| professor / SW | desk__front__ne__06: 2, desk__monitor__ne__00: 12 |
| professor / SE | desk__front__nw__04: 2, desk__monitor__nw__01: 12 |

기존 리뷰 이후 캐릭터 원본이 변경됨: 30개 ROI/phase 기록 차이. 전후 비교에는 같은 고정 캐릭터 snapshot을 사용했다. 상세: JSON referenceDrift.
