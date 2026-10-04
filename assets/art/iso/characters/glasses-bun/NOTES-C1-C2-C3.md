# glasses-bun ISO — C1/C2/C3 제작 결과

일반 38행/152셀(128×2432), blink 24행/96셀(128×1536), SE/SW eye mask, 동일 내용의 manifest 두 파일, 4개 iso 검수판, 네 방향 3× walk GIF·스트립을 이 폴더에 내보냈다. final checker **PASS 12 / SKIP 5 / FAIL 0**, exit 0, tolerance 0. [실제 출력](validation.txt). SKIP은 캐릭터에 해당하지 않는 바닥 이음·가구 part 검사 및 blink 중복 face-lock 검사다.

Locked roster/README·face references, hero dev/professor crop과 최종 dev-hoodie-v2/punk-girl/professor를 먼저 읽고 비교했다. 쌍번 갈색 머리, 둥근 안경과 두 눈의 하이라이트, 크림 카디건·블라우스, 긴 테라코타 치마, 갈색 신발을 새 대각선 그림으로 제작했다. 42px idle 실루엣, 머리 레이어 21px로 몸의 1/3 이상이다. 모든 facing은 foot (16,60), pelvis (16,44), 고정 C/H와 transition 일정 그대로다. 사용한 RGB는 정확한 원본 identity 색과 S0 하이라이트 **17색**, alpha 0/255다.

내장 image_gen만 사용해 idle 네 방향 마스터 1회, walk SE/NE 8포즈 마스터 2회, seat 네 방향 마스터 1회, 총 **4회 직렬 생성**했다. walk 1차는 하강 자세 차이·배경 분리가 약해 2차 선택. 각 마스터 최대 3회 이내다. selected raw와 rejected walk 1차, [전체 프롬프트](runs/generation-prompts.json), 추출 box·공통 scale·고정 원점을 runs에 남겼다. 도구가 관리하는 기본 generated_images 캐시 외에 직접 쓴 파일은 이 lane뿐이다. 인증·기본 설정·공유 문서·소스·테스트·가구·pack·git은 변경하지 않았다.

생성 마스터를 연결 성분으로 추출하고 한 번 네이티브 격자로 축소했다. idle/seat는 BOX 면적 샘플 후 팔레트·이진 alpha로 정리했고, walk는 행 전체 공통 scale와 고정 column 원점/row 바닥을 사용했다. 프레임마다 bbox 피팅·재중심화하지 않았다. 머리·몸통·치마 주름·신발은 생성 그림에서 왔으며 안경/큰 눈·손바닥·소매·윤곽·접점/연결은 native pixels로 보정했다. 248개를 독립 생성했다고 주장하지 않는다.

보행은 contact/down/passing/up의 서로 다른 네 포즈, 치맛단 변화, body/head bob [0,+1,0,−1], 지지발 교대와 반대 팔 swing이다. SW/NW는 걷는 몸만 x′=32−x로 굽고 좌상단 명암을 보정했다. 머리와 착석은 각 facing의 독립 생성 레이어다. phone은 SW에서도 해부학적 같은 손으로 별도 reach를 그렸다. 머리는 clip별 고정 RGBA이며 walk의 명시적 ±1px 외에는 0px; 전환도 source head offset 0으로 누적 3px 해석 문제를 피했다. 마지막 SW 동작 보정에서 쉬는 반대 팔이 가슴을 가로지르지 않게 바꿨다.

모션 확정 **후** [blink_resync.py](blink_resync.py)가 final normal 셀 전체를 복사하고 12개 eye interior texel만 닫힌 눈으로 바꾼다. 모든 96셀의 non-eye RGBA는 normal과 동일하고 mask는 SE/SW뿐이다. 머리·머리 offset·눈 mask를 다른 몸 pose와 섞지 않는다. [blink 근거](blink-resync-proof.json).

## v3 책상 + v3 의자 가시율

workstations measure-A3-rev2/verify-desk-rig-v3 방식의 실제 serialized part/depth queue, 원래 ground key·source anchors·seat ties로 4방향×4 typing phase를 합성했다. 순서별 픽셀 ownership과 이후 occluder alpha 합집합을 교차 확인했다. 가구 입력 snapshot/hash와 개별 phase 분자는 [seated-visibility.json](seated-visibility.json), 합성은 [iso-seated-review.png](iso-seated-review.png)다. torso/hand 정의는 JSON과 txt에 명시했다.

| Worker | Head visible/total | Head % | Torso visible/total | Torso % | Hands visible/total | Hands % |
|---|---:|---:|---:|---:|---:|---:|
| SE | 1220/1220 | 100.00 | 192/192 | 100.00 | 34/34 | 100.00 |
| SW | 1232/1232 | 100.00 | 188/188 | 100.00 | 50/50 | 100.00 |
| NE | 1192/1284 | 92.83 | 224/224 | 100.00 | 32/32 | 100.00 |
| NW | 1140/1224 | 93.14 | 188/188 | 100.00 | 35/35 | 100.00 |

위 분자/분모는 네 phase 합계다. head의 최소 phase 비율도 표와 같고 torso·손은 모든 phase 100%. 실제 두 H의 skin 거리는 전부 0px다. NE/NW head 85%, SE/SW 90% gate를 모두 통과했다. 완료 시 live furniture hash 차이: 0개. 다른 lane의 후속 가구 수정에는 해당하지 않는다.

## 수행한 검증과 남은 약점

[국소 근거](runs/derivation-proof.json): 152개 canonical head RGBA 비교·152개 연결 실루엣, 4방향 walk/typing 네 phase 구분, 모든 walk y60 접지, 32개 prop hand skin 접점 0px, sit/stand 정확한 역순과 idle/rest 끝점 일치. GIF는 최종 native의 정확한 3× RGB, 각 4프레임, 120/130/120/130ms·500ms 루프를 파일에서 확인했다. [GIF 근거](runs/gif-proof.json). 원본·생성본·일반 전 행·blink 전 행·v3 착석·최종 수정판 포함 **14개 이미지**를 1×/3×로 비교했다. 정적 검수는 독립적인 사람의 시각 승인을 대신하지 않는다.

정체성은 읽히지만 앞 얼굴은 약간 좁고 안경 원형·눈은 1×에서 작은 군집이다. 소매·손이 여전히 가늘고 일부 reach/typing의 전완이 곧게 보인다. 4프레임 압축 gait의 타이밍은 거칠고 두 발의 완전한 8단계 생체역학을 표현하지 못한다. 3→0 바뀐 픽셀 수는 내부 최대보다 SE 18, SW 25, NE 35, NW 37개 많다. 정적 wrap 비교를 했으나 자연스러운 재생 합격을 주장하지 않는다. sleep은 작은 seated doze라 충분한 sofa recline이 아니다. hero 수준의 부드러운 손·몸 볼륨은 아직 약하다.

WPF/app/DPI·runtime depth/picking/seat interruption, 실제 이동 속도·발 미끄러짐, GIF 플레이어에서의 연속 재생, sofa sleep 접촉, 실제 cup/paper/phone prop atlas 합성, 사람의 비대칭·정체성/모션 승인, pack 소비는 검증하지 못했다. 전체 dotnet test/빌드·git 작업·추가 에이전트는 실행하지 않았다.

## 실측 lane 시간·재현

시작 2026-10-04T02:17:24+00:00 → 결과 기록 2026-10-04T02:46:45.872631+00:00: **0.4894 lane-hours (29.36분)**. 단일 lane의 조사·직렬 생성 대기·native 보정·정적 검수·체커 점유 wall-clock이다. 첫 시계 관측 전의 초기 읽기는 제외했고, 다른 lane과 대기 시간을 중복 합산하지 않았다. 마지막 이미지 확인/채팅 보고 시간은 이 종료 시각 뒤다. [시계 기록](runs/timing.json). 승인된 생산 처리량으로 일반화하지 않는다.

workspace root에서 기존 sprite-gen venv Python의 `-B`로 `build_glasses_bun_lane.py normalize` → `build` → `blink_resync.py` → `verify` → `reviews` → `seated` → `finish` 순서. 모든 출력은 이 폴더 안이다. `finish`는 최종 checker와 GIF 직렬 확인·기록을 수행한다. builtin 그림을 다시 생성하지 않아도 선택 master로 재구성 가능하다.
