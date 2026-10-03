# dev-hoodie — 팀 A

짙은 피부의 청년, 큰 검은 곱슬머리, 넉넉한 머스터드 후디, 목에 걸친 헤드폰. 넓은 머리와 후디가 실루엣의 중심입니다.

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

생성된 기본 이미지와 모든 행, 최종 셀을 시각 검수하고 약한 행을 재생성했습니다. 왼쪽 보행의 네 번째 프레임 크기 변화를 발견해 행 전체를 재생성했습니다. 이후 네 프레임에 같은 1.18배 NEAREST 변환과 발 정렬을 적용해 오른쪽 보행과 크기를 맞췄습니다. 변경은 run/curation.json에 기록되어 있습니다.

남은 약점: report 행은 서 있는 idle보다 인물이 작습니다. typing_side와 phone의 손가락 변화는 미세하며, 짙은 피부와 헤드폰은 원래 32px 폭에서 일부 세부가 합쳐집니다. 연결 가능한 브라우저가 없어 GIF 실제 재생·루프 연결은 검증하지 못했습니다. 네 보행 행은 **experimental**, 모션 연속성 합격 판정은 보류입니다. 정적 품질 비교는 주관적 검수이며 pixel-agents 기준 이상의 객관적 점수로 측정한 결과는 아닙니다.

