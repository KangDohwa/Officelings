# dev-hoodie-v2 motion rev1

**시각 개선안이며 최종 승인 완료 아님.** 최종 검사: **11 PASS / 5 SKIP / 1 FAIL** (`--stage final --tolerance 0`). [실제 출력](validation-motion-rev1.txt).

## 변경 파일과 범위

- `dev-hoodie-iso.png`: 걷기 rows 4–7의 16셀과 타이핑 rows 8–11의 16셀만 수정. 38행/152셀, 24행/96 blink 셀, 팔레트 21색, foot (16,60), pelvis (16,44), C/H, 모든 타이밍·별칭을 유지.
- `dev-hoodie.iso.json`, `dev-hoodie-iso.manifest.json`: 위 행의 마스터 출처와 보정 설명만 추가. 머리 오프셋과 고정 연락점은 그대로.
- [걷기 전후](before-after-walk-motion-rev1.png), [타이핑 전후](before-after-typing-motion-rev1.png), [furniture-v2 접촉 전후](before-after-contact-furniture-v2-motion-rev1.png), [최종 위상·손 확대](final-motion-rev1-review.png).
- 각 방향 `walk-se/sw/ne/nw-motion-rev1-3x.gif` 및 동명 PNG 스트립: 실제 네 셀, 평균 8fps/125ms (GIF의 10ms 저장 단위에 맞춰 120/130ms 교대, 한 루프 500ms), 3× nearest-neighbor. `runs/motion-rev1/`에 원본 스냅샷, 생성 마스터·프롬프트, sprite-gen 추출 결과, 재현 스크립트, 해시·검증 근거 보관.

## 실제 제작과 검증

내장 image generation을 순차 3회 사용: SE 2회, NE 1회. SE 1차는 보폭/무릎이 더 낫고 2차는 다리가 서로 너무 비슷해 1차 선택. NE 1차 선택. 생성만으로 팔의 역스윙이 충분하지 않아 native grid에서 팔꿈치·손목·골반·무릎·발끝을 다시 그렸다. 생성된 hoodie 덩어리는 몸통에 유지했다. SW/NW는 머리를 제외한 몸만 anchor 보정 미러 후 화면 좌상단 색조 보정. head 전체는 이전 atlas에서 다시 고정했다. 생성 마스터 전체나 머리를 그대로 채택한 결과가 아니다.

sprite-gen canonical component extraction → 한 방향 행 전체의 단일 고정 축소율 → 기존 팔레트·이진 alpha → native 보정. 원시 이미지를 고정 칸으로 잘라 추출하지 않았다. 몸통/골반 bob은 0,+1,0,−1px이고 **머리는 0px 이동**. 손은 서로 다른 손바닥/엄지/손끝 덩어리와 교대 하이라이트를 사용하고 접점 texel을 고정했다.

152 normal 셀과 96 blink 셀의 canonical head RGBA 동일성, headOffsetsByClip 값 동일성, 미수정 120 normal 셀 전체 RGBA 동일성, 눈 마스크 두 PNG의 SHA-256 동일성을 확인했다. blink PNG 전체도 SHA-256 동일하다. 네 방향 모두 서로 다른 걷기·타이핑 위상, 연결된 실루엣, 매 위상 y=60의 접지, 타이핑 하체 보존을 확인했다. 32개 실제 피부 접점은 모두 0px 오차이고 furniture-v2 C/H도 0px이다. 3→0 픽셀 변화량은 다른 인접 위상 최대 변화량 이하이며, 이는 이상한 단독 seam이 없다는 수치 근거일 뿐 자연스러운 gait 승인 근거는 아니다.

furniture-v2는 작업 중 병렬 수정이 관찰되어 로컬 읽기 스냅샷으로 리뷰를 고정했다. `runs/motion-rev1/furniture-v2-contact-proof.json`의 해시와 캡처 시각에 해당하는 상태이며, 이후 가구 변경은 이 리뷰에 포함되지 않는다. 머리 가시율은 canonical head mask 기준이라 기존 NOTES의 y<43 넓은 영역 수치(약42%)와 직접 비교할 수 없다.

- NW 뒤/앞 뷰 머리 가시율: 231/268 (86.2%).
- NE 뒤/앞 뷰 머리 가시율: 225/268 (84.0%).
- SW 뒤/앞 뷰 머리 가시율: 285/287 (99.3%).
- SE 뒤/앞 뷰 머리 가시율: 279/287 (97.2%).

## 남은 문제와 승인 대기

**FAIL 원인:** 사용자가 blink atlas 파일 변경을 금지하여 새 normal 몸 픽셀을 blink에 동기화하지 않았다. normal rows 4/5/8/9와 대응 blink의 non-eye RGBA가 달라졌다. 눈/얼굴/머리가 흔들렸다는 FAIL이 아니다. 원본 blink가 교체되는 순간 몸이 이전 pose로 바뀌므로 이 상태로 최종 pack에 넣으면 안 된다. 얼굴·눈·머리를 유지한 채 대응 몸 픽셀만 동기화하는 허용 여부를 질문했고, 이 기록 시점까지 승인 답변은 없었다. 검사 완화·마스크 확대·행 추가로 숨기지 않았다.

보폭, 무릎 굽힘과 역스윙의 구분은 개선됐지만 4프레임 압축 gait는 여전히 다소 딱딱하다. phase 2는 passing과 반대 다리 contact를 합친 키포즈로, 세부 생체역학의 contact/down/passing/up 전체를 두 발 모두 독립 표현하지 못한다. 머리 고정 때문에 상체 bob이 작고, 어깨/소매 연결은 단순하다. 타이핑 손은 1×에서 여전히 작은 몇 픽셀 덩어리이며 rear 손 일부는 데스크/모니터에 가려진다. hero의 편안한 손과 캐릭터 볼륨까지 맞췄다고 주장하지 않는다.

정지 위상 스트립을 1×/3× hero crop과 직접 보았고, 총 14개 이미지 뷰 안에서 검수했다. GIF 파일은 네 프레임·96×192 배율·120/130ms 교대·500ms loop를 확인했으나 실제 시간 재생의 사람 승인을 받지 않았다. WPF 실제 화면, 이동 속도와 보폭 매칭, 런타임 깊이/occlusion, 최신 병렬 가구 변경 후 결과는 검증하지 않았다. 앱 빌드·전체 dotnet test·pack 수정·git 쓰기는 수행하지 않았다.

## 실제 lane 시간

시작 2026-10-03T21:55:20+00:00 → 최종 내보내기 검사/기록 2026-10-03T22:10:27.915622+00:00: **0.252 lane-hours (15.13분)**. 단일 세션의 조사·생성 대기·수정·검수·검증 wall-clock 점유를 실측했다. 다른 lane 시간은 더하지 않았고 생성 대기를 이중 합산하지 않았다. 마지막 이미지 확인·채팅 보고의 수 초는 별도다.

재현(허용 폴더만 기록): sprite-gen venv Python으로 `runs/motion-rev1/build_motion.py`, `verify_motion.py`, `review_motion.py`, `finalize_motion.py` 순서. 원래 `runs/v2/build.py`를 다시 실행하면 이번 모션이 되돌아간다. blink 몸 동기화는 사용자 승인 후에만 `build_motion.py --sync-blink-body`를 사용한다.
