# cap-teen ISO 제작 기록

요청한 파일 세트와 행 계약은 완료했다. **최종 체커 PASS 12 / SKIP 5 / FAIL 0**이며, **v3 착석 머리 가시성은 SE/SW 미달**이다. 시각·런타임 승인을 완료했다고 주장하지 않는다.

normal은 128×2432, 38행×4단계(152셀), blink는 128×1536, 24행×4단계(96셀)이다. SE/SW eye mask 두 장, 같은 내용의 manifest 두 장, iso-contact / iso-rows-review / iso-seated-review / iso-vs-flat, 네 방향 3× walk GIF와 스트립, 원본·프롬프트·재현 스크립트를 이 폴더에 저장했다. props와 말풍선은 별도 바인딩이며 몸에 굽지 않았다.

뒤집어 쓴 빨간 모자의 앞쪽 조절 끈, 뒤쪽 민무늬 크라운·챙, 갈색 앞머리, 남색 헤드폰, 머스터드 티셔츠, 올리브 반바지와 빨간 운동화를 locked 원본에서 읽었다. 내장 image_gen을 순차 5회 호출했다. idle·seat은 각1회, walk는 최대3회 안에서 마지막 결과를 채택했다. 선택 마스터3장은 실제 키 포즈8개이며 248개의 독립 생성 그림이 아니다. 앞선 walk 두 장은 같은 발을 드는 반복이 많아 거절했다. 생성 이미지는 도구 기본 캐시에도 남지만 프로젝트용 사본과 직접 쓴 모든 파일은 cap-teen 폴더 안에만 있다.

연결 컴포넌트 추출 → 마스터별 공통 축소율 → 원본32색 팔레트 snap → 이진 alpha → native 윤곽·관절·접점 보정을 적용했다. 약한 walk 배경 alpha는245 미만을 제거했다. 고정 머리는 생성된 대각선 참조에서 뽑아 눈2×3 픽셀과 밝은 하이라이트를 한 번 보정했다. 40px 서기, foot(16,60), pelvis(16,44), 키보드 접점, 모든 phaseContacts와 별칭은 계약대로다. 머리 높이는 약17px(서기40px의42.5%)다. 모션 셀마다 크기를 다시 맞추거나 anchor를 바꾸지 않았다.

SW/NW는 x′=32−x로 굽고 명암을 화면 좌상단으로 보정했다. 글자·편측 장식은 없으며 모자 끈은 앞에서만 보인다. phone은 같은 해부학적 손에 별도로 배치했다. 머리 오프셋은 idle/walk/actions0, seated/sleep−1, sit0/0/−1/−1, stand 역순으로 전체 변위1px 안이다. 152셀 canonical head RGBA와 152개 연결된 몸을 검증했다. 보행·타이핑은 방향마다 다른4단계, 전환은 정확한 역순과 idle/rest 끝점을 확인했다.

최종 motion에서 blink_resync.py가 normal 셀을 복사하고 마스크의 눈12픽셀만 교체한다. 96셀의 모든 non-eye RGBA가 normal과 같다. 검사 원문은 validation.txt, 머리·연결·루프 근거는 runs/derivation-proof.json, blink 근거는 blink-resync-proof.json이다. 체커의5 SKIP은 바닥·가구 전용 및 blink 중복 항목이다.

v3 책상과 **v3 의자**의 현재 manifest/PNG를 이 폴더에 읽기 스냅샷으로 고정해 S0 seat-group 실제 part 순서를 합성했다. v3 measure 스크립트와 같은 최종 alpha 소유권 방식으로 전면 덮개 마스크와 교차 확인했다. C/H metadata 오차0px, front 손 피부는 H보다1px 위, rear는0px이며 네 방향 모든16 typing 셀 손 ROI100%다. 아래는 네 단계 중 최소 가시율이다.

| 작업자 | 머리 visible/total | 몸통 | 손 |
| --- | --- | --- | --- |
| SE | 194/249 (77.91%) | 100.00% | 100.00% |
| SW | 190/249 (76.31%) | 100.00% | 100.00% |
| NE | 241/250 (96.40%) | 82.28% | 100.00% |
| NW | 229/250 (91.60%) | 73.24% | 100.00% |

머리는 canonical PNG alpha를 선언된 offset에 배치한 고정 분모다. 몸통은 opaque에서 머리/손을 제외한 x11..22, y<44 영역이다. 손은 고정 키보드 쌍 Chebyshev 반경2 안의 피부이며 머리를 제외한다. 실제 피부 접점 거리≤1px를 확인했다. 입력 해시, 모든 단계 분자/분모, queue와 가리는 파트/픽셀은 runs/visibility-v3.json에 있다.

SE 머리55픽셀과 SW59픽셀은 각각 현재 chair__front__se__00, chair__front__sw__00가 가린다. 이 두 part는 source anchor y80에 bbox top y51, 즉 원점 위29px까지 올라오고 actor 뒤에 그려져야 할 머리 영역을 rank20으로 덮는다. frozen back21 기준과 차이가 있어 공유 가구 담당의 확인이 필요하다. 가시율90%를 충족하지 못했으며 가구, 깊이 순서나 캐릭터 앵커를 이 레인에서 임의로 바꾸지 않았다. 후면 NE/NW85%와 모든 손100%는 충족했다.

보행은 생성된 contact/passing의 팔·다리 볼륨을 남기고 down/up의 압축, 뒤꿈치/종아리 호와 body bob0/+1/0/−1을 파생했다. 네 장 압축으로 중간 자세가 생략돼 리듬이 다소 끊길 수 있다. SE/SW 3→0 픽셀 변화는 내부 최대 이하이며 NE/NW는 각각22/20픽셀 더 크다. 이 지표는 사람의 자연스러운 루프 승인 근거가 아니다. 1×에서 손은 몇 픽셀 군집이고 착석 전완이 길며 sleep은 약한 좌식 doze다. 모자/헤드폰은 읽히지만 hero와 같은 볼륨·매력에 도달했다고 단정하지 않는다.

참조, 생성 마스터, 최종38행과 마지막 전환 보정, identity/blink, v3 착석을 총14개 이미지로1×/NEAREST3× 검수했다. 세 파일럿과 hero crop을 나란히 놓았다. GIF는4프레임·96×192·120/130/120/130ms·500ms 루프이며 원본 픽셀의 정확한3×를 확인했다. 실제 애니메이션 재생의 자연스러움, 이동 속도·발 미끄러짐, 독립 사람의 정체성/모션 승인, WPF/Core 런타임 순서·DPI·hover, sofa 접촉과 실소품 장착은 검증하지 않았다.

재현은 기존 Pillow/numpy Python의 -B로 normalize_cap_lane.py → build_cap_lane.py → blink_resync.py → review_verify_cap_lane.py 순서다. 리뷰 스크립트는 체커와 가시성 결과를 별도로 기록하며 현재 가시성 FAIL이면 exit2로 끝난다. checker exit0을 시각 승인으로 바꾸지 않는다. src/tests/tools/docs/기존 art/다른 lane/pack을 수정하지 않았고 git 쓰기, dotnet 전체 테스트, 설치·업로드·배포는 실행하지 않았다.

실측 lane 점유: 2026-10-04T06:33:20+00:00 → 2026-10-04T07:09:01.074701+00:00, **0.5947 lane-hours (35.68분)**. 최초 시계 관측부터 조사·생성 대기·수정·검수·검증·노트 작성까지 포함했다. 다른 lane 시간을 합산하거나 생성 대기를 이중 계산하지 않았다. 개별 생성 시간 합계는 별도로 측정하지 않았고 처리량 승인치로 일반화하지 않는다.
