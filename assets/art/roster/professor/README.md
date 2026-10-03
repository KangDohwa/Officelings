# professor — 팀 A

뒤로 쓸어 넘긴 흰 머리와 흰 수염의 노년 남성, 갈색 조끼와 나비넥타이. 뾰족한 흰 머리, 수염, 조끼의 층으로 구별됩니다.

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

생성된 기본 이미지와 모든 행, 최종 셀을 시각 검수하고 약한 행을 재생성했습니다. talk의 원본이 안경과 긴 코트를 추가해 폐기하고, 안경 없는 동일 조끼로 재생성했습니다. report도 너무 작았던 원본을 재생성했습니다. 새 원본은 같은 크기지만 픽셀 격자 검출이 프레임별 크기를 달리해, canonical curation으로 세 프레임 높이 56px와 발 바닥 y=60을 맞췄습니다.

남은 약점: walk_down은 idle보다 작게 읽힙니다. report의 팔 뻗기는 짧으며, 얼굴 비율에 소폭 차이가 남습니다. 나비넥타이와 손가락 세부는 32px 폭과 32색 팔레트에서 단순해집니다. 연결 가능한 브라우저가 없어 GIF 실제 재생·루프 연결은 검증하지 못했습니다. 네 보행 행은 **experimental**, 모션 연속성 합격 판정은 보류입니다. 정적 품질 비교는 주관적 검수이며 pixel-agents 기준 이상의 객관적 점수로 측정한 결과는 아닙니다.

