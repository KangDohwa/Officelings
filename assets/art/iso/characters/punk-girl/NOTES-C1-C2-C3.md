# punk-girl — C1/C2/C3 iso lane

파일·수치 규격은 완료했으며 **시각 승인은 보류**합니다. 특히 후면 착석 가림을 해결하지 못했습니다. src/, tests/, docs/, 가구, 기존 v1/v2, pack, build_pack은 수정하지 않았고 git 변경 명령과 dotnet 테스트는 실행하지 않았습니다.

## 파일과 제작

- `punk-girl-iso.png`: 128×2432, 38행×4단계 = 152셀.
- `punk-girl-iso-blink.png`: 128×1536, 24행×4단계 = 96셀. SE/SW 눈 마스크 두 장, 동일 내용의 `punk-girl.iso.json`·`punk-girl-iso.manifest.json`.
- `iso-contact.png`, `iso-rows-review.png`, `iso-seated-review.png`, `iso-vs-flat.png`. 실제 최종 통합 검수 화면은 `runs/final-review.png`.
- 실제 체커 출력은 `validation.txt`, 재생용 참고 GIF와 생성·접점·파생 근거는 `runs/`.

Locked 원본의 분홍 각진 단발, 얼굴, 분홍 이너, 검은 스터드 재킷·찢어진 반바지, 맨다리와 큰 부츠를 참조했습니다. hero worker crop은 비율·카메라·무드 참조이며 원본 도안 대신 잘라 쓰지 않았습니다.

네 방향 idle와 착석을 각각 독립 생성하고 SE/NE 보행 접촉 포즈를 추가해 **마스터 10장, 각 1회** 생성했습니다. 생성은 한 번에 한 장, 이미지 열람은 참조·생성본·1×/3× 비교·최종 합성 포함 **14회**입니다. 최대 3회 제한을 넘긴 마스터가 없습니다.

sprite-gen 스킬을 읽고 workflow를 실행했습니다. 최초 샌드박스 로그인 검사는 홈 경로 제한으로 실패했고 읽기 전용 권한 확장 점검은 ready였습니다. 이후 provider 세션을 lane 내부로 격리하려던 스크립트의 auth.json 복사가 자동 승인 검토에서 토큰 노출·잔류 위험으로 거절됐습니다. 해당 스크립트는 실행되지 않았고 삭제했습니다. **내장 image_gen으로 마스터를 생성하고 sprite-gen cutout --key green으로 정리한 혼합 경로**입니다. 인증 파일은 복사하지 않았습니다. 도구의 자동 원본 캐시는 기본 CODEX_HOME/generated_images에도 남고, 선택 원본·작업 파일·보정 스크립트는 이 lane 안에 있습니다. API 키 경로로 전환하지 않았고 환경 설정·기본값도 저장하지 않았습니다.

각 마스터를 한 번만 비례 축소한 뒤 정확한 원본 정체성/고정 재질 색에 맞추고 alpha 0/255로 정리했습니다. 최종 사용 색은 17개입니다. 프레임마다 크기를 다시 맞추거나 bbox로 정렬하지 않았습니다. 서 있는 기준은 분홍 머리 포함 **42px**로, nominal 40과 고정 허용 범위 38–42 안에 있습니다. 셀 32×64, 발 (16,60), 골반 (16,44), 모든 키보드 손 접점, 전환 접점 일정은 S0 그대로입니다.

생성된 머리·재킷·하체 픽셀을 고정 부품으로 파생했습니다. 머리·머리 가르마를 좌우 반전하지 않았습니다. SW/NW 보행의 하체만 x′=32−x로 재사용하며 피부/부츠 명암을 보정했습니다. 해부학적 오른손 phone 포즈는 SW에서도 그쪽 팔에 따로 배치했습니다. 팔꿈치·손목·손가락·무릎 연결은 네이티브 픽셀로 보정했고 props/말풍선은 몸에 굽지 않았습니다. 248셀을 각각 생성한 그림으로 주장하지 않습니다.

## 실제 검증

`sprite-gen/.venv/Scripts/python.exe tools/iso_check.py assets/art/iso/characters/punk-girl --stage final --tolerance 0`: **PASS 12 / SKIP 5 / FAIL 0**. SKIP은 바닥 이음·가구 part 이름 등 캐릭터에 해당하지 않는 검사입니다. 행 순서/FPS/loop/aliases, 고정 접점, alpha/guard, palette, SE/SW eye-only 마스크 및 96셀 non-eye RGBA 동일성을 확인했습니다.

별도 국소 검증: 152 canonical head byte 비교, 셀 좌표 기준 머리 이동 최대 1px, 152 연결된 몸 실루엣, 네 방향마다 보행·타이핑 4단계 구별, sit/stand 정확한 역순 및 idle/rest 끝점 일치, idle grip 16회 실제 피부 확인. 추가로 pickup/handover/coffee/phone의 선언된 32개 손 접점 주변에 실제 피부가 있음을 확인했습니다. 첫 검증에서 SW/NW typing 중복과 두 보행 프레임의 분리된 다리를 발견해 수정했습니다. 최종 체커 로그 저장 중 CP949/UTF-8 혼선은 자식 Python UTF-8 강제로 수정했으며 이는 checker 실패가 아닙니다.

v2 desk/chair의 실제 manifest·split parts·depth queue로 네 방향을 합성했습니다. 골반과 키보드 metadata 오차는 **0px**, 32 typing fingertip 접점의 실제 피부는 **1px 이내**입니다. `runs/contact-proof.json`에 사용한 가구 해시와 실제 occluder 이름을 남겼습니다. 이 로컬 합성은 WPF depth 승인 대신 사용할 수 없습니다.

## 남은 약점과 승인 보류

**후면 가림은 개선 목표를 충족하지 못했습니다.** 최종 실제 머리 마스크 가시율은 NW **108/238 (45.4%)**, NE **73/267 (27.3%)**, SW **244/260 (93.8%)**, SE **251/262 (95.8%)**입니다. pilot과 비슷하게 y<43 상체 영역으로 계산하면 NW **134/359 (37.3%)**, NE **114/390 (29.2%)**로, 기존 pilot 42% 근처보다 좋다고 주장할 수 없습니다. 책상 전면과 후면-facing 의자 전면이 주요 occluder입니다. 해당 가구는 수정 권한 밖이며 캐릭터의 발·골반·손 앵커를 옮겨 가림을 숨기지 않았습니다.

셀 배치 원점 차이를 4px 보정해 월드 머리 높이를 고정하는 실험은 `runs/seated-origin-experiment.png`와 `contact-origin-experiment.json`에만 남겼습니다. 그 안은 셀 내부 1px 제한 해석이 필요한데도 가림을 충분히 해결하지 못해 채택하지 않았습니다. **최종 atlas/manifest는 source head offset 0 또는 −1만** 사용합니다.

분홍 머리와 의상은 읽히지만 몸이 가늘고 스터드·부츠 하이라이트가 거칩니다. 생성본의 자연스러운 손 형태가 접점 보정에서 단순해졌으며, 보행은 무릎/뒤꿈치 변화가 있어도 여전히 기계적입니다. sleep은 작은 착석 doze 수준으로, 충분히 자연스러운 sofa recline을 그렸다고 주장하지 않습니다. 세부 머리 가르마·장식·찢어진 자국의 해부학적 동일성, hero 수준의 매력과 동작 자연스러움은 독립적인 사람 승인이 필요합니다.

WPF/앱 실행, 실제 GIF 루프 재생, sofa에 대한 sleep 합성, 실제 prop atlas 장착, pack 소비, DPI·picking·runtime interruption은 검증하지 않았습니다. 브라우저·전체 빌드·전체 테스트·추가 에이전트는 실행하지 않았습니다.

## 실측 lane 시간

시작 2026-10-03T20:56:08Z, 종료 기록 2026-10-03T21:24:02Z: **0.465 lane-hours (27분 54초)**. 한 lane의 읽기·생성 대기·정리·수정·검수·체커·보고 시간을 포함합니다. 다른 lane의 병렬 시간과 합치지 않았습니다. provider 개별 시간을 전부 측정하지 않아 별도 생성 시간 합계를 주장하지 않습니다. 이 수치를 승인된 제작 처리량으로 일반화할 수 없습니다.

재생성 없이 재구성하려면 workspace root에서 sprite-gen venv Python으로 `runs/normalize.py`, `runs/build.py`, `runs/verify.py`, `runs/review_contacts.py`를 차례로 실행합니다. PYTHONDONTWRITEBYTECODE=1로 공유 도구의 pycache 쓰기를 피합니다.

