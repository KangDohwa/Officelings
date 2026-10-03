# glasses-bun — 팀 A

쌍번 머리와 둥근 안경의 여성, 크림 카디건과 긴 테라코타색 치마. 두 번과 넓게 퍼지는 치맛단으로 구별됩니다.

최종 파일: [투명 시트](run/sprite-sheet-alpha.png), [manifest](run/manifest.json), [전체 프레임 검수](run/sheet-review.png), [검수 기록](qa-notes.md). 팀 전체의 idle·walk 3배 NEAREST 비교는 [A-contact.png](../A-contact.png)입니다.

셀 **32×64px**, 4열×13행, 시트 **128×832px**, 유효 프레임 **42개**입니다. 설정 마진은 x=1px/y=4px입니다. 행과 프레임 수, FPS, 반복값은 ../../worker-request.json을 따릅니다. 일부 머리/포즈 픽셀은 마진 영역에 걸칩니다.

| 행 (0부터) | 상태 / 파일 이름 | 프레임 | FPS | 반복 |
|---:|---|---:|---:|:---:|
| 0 | idle | 4 | 0 | 아니오 |
| 1 | walk_down | 4 | 7 | 예 |
| 2 | walk_left | 4 | 7 | 예 |
| 3 | walk_right | 4 | 7 | 예 |
| 4 | walk_up | 4 | 7 | 예 |
| 5 | typing_back | 3 | 5 | 예 |
| 6 | typing_side | 3 | 5 | 예 |
| 7 | report | 3 | 5 | 아니오 |
| 8 | sleep | 2 | 2 | 예 |
| 9 | coffee | 3 | 3 | 예 |
| 10 | phone | 3 | 3 | 예 |
| 11 | talk | 3 | 4 | 예 |
| 12 | surprised | 2 | 5 | 아니오 |

idle의 네 셀은 **DOWN → LEFT → RIGHT → UP** 방향의 정지 포즈입니다. 연속 재생하지 않습니다. report는 보고서 전달, talk는 대화입니다. surprised의 **!**는 worker 규약과 같이 런타임 말풍선으로 표시하며 캐릭터 시트에 굽지 않았습니다. typing과 sleep에는 책상·키보드·의자 등 가구를 넣지 않았습니다.

원본 run/raw/<state>.png는 **단색 #FF00FF** 배경입니다. 최종 시트와 run/curated/<state>-frame-N.png는 RGBA 투명 배경, 알파 0/255입니다. 프레임 번호는 0부터 시작하며 사용하지 않는 열은 투명합니다. 원본 추출은 run/frames/<state>/frame-N.png, 최종 보정이 반영된 개별 PNG는 run/curated/에 있습니다.

sprite-gen의 **codex 정지 이미지 atlas rows** 경로로 제작했습니다. 영상 생성은 사용하지 않았습니다. run/previews/<state>.gif는 최종 curation을 반영한 검토용 파일입니다. GIF 반복·idle 미리보기 시간은 런타임 규약이 아니며 재생은 manifest의 FPS/loop와 idle 방향 선택을 따릅니다.

생성된 기본 이미지와 모든 행, 최종 셀을 시각 검수하고 약한 행을 재생성했습니다. surprised를 두 번 재생성했습니다. 첫 행은 포즈 차이가 너무 작았고, 두 번째는 추출 후 얼굴이 길어졌습니다. 세 번째는 승인된 native idle을 참조해 얼굴 비율과 두 손을 올리는 차이를 개선했습니다. report도 큰 첫 포즈를 줄이도록 재생성했습니다.

남은 약점: 측면에서는 안경의 원형이 몇 픽셀로 단순해집니다. typing_side와 phone의 손 변화가 작고, 상태 전환 시 머리 크기에 소폭 차이가 남습니다. 연결 가능한 브라우저가 없어 GIF 실제 재생·루프 연결은 검증하지 못했습니다. 네 보행 행은 **experimental**, 모션 연속성 합격 판정은 보류입니다. 정적 품질 비교는 주관적 검수이며 pixel-agents 기준 이상의 객관적 점수로 측정한 결과는 아닙니다.

