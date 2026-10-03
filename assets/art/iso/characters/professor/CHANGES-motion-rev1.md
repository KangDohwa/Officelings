# 교수 모션 rev1 — 시각 승인은 조건부, final 계약은 미통과

걷기 16프레임과 타이핑 16프레임을 수정했다. 38행 계약에서 걷기는 4–7행, 타이핑은 8–11행이며 그 밖의 30행은 RGBA 바이트가 그대로다. 머리 152개 프레임, 네 머리 PNG, 모든 headOffsets, 눈 마스크 두 개와 blink PNG는 보존했다. 행을 추가하지 않았다.

파일: `professor-iso.png`, 내용이 같은 `professor.iso.json`·`professor-iso.manifest.json`, `validation.txt`를 갱신했다. [수정 전](motion-rev1-before-review.png), [수정 후](motion-rev1-after-review.png), [3→0 연결](motion-rev1-walk-wrap-review.png), [손 전후](typing-hands-motion-rev1-before-after.png), [v2 가구 접촉 전후](motion-rev1-v2-seated-before-after.png)를 추가했다. 기존 `iso-contact.png`·`iso-rows-review.png`·`iso-seated-review.png`·`iso-vs-flat.png`는 이전 버전의 역사적 검수판이다.

3× 걷기 GIF: [SE](walk-se-motion-rev1-3x.gif), [SW](walk-sw-motion-rev1-3x.gif), [NE](walk-ne-motion-rev1-3x.gif), [NW](walk-nw-motion-rev1-3x.gif). 같은 이름의 `walk-<방향>-motion-rev1-strip-3x.png`도 네 개 있다. GIF는 4프레임, 120/130/120/130ms, 한 주기 500ms다. 원본 clip의 8fps와 loop 설정은 유지했다.

내장 image_gen으로 SE/NE 보행 마스터를 각각 1회, 순차 생성했다. 기존 교수·hero 크롭을 참조로 사용했고 `runs/motion-rev1/*-walk-prompt.txt`, 원본 `*-walk-master-a1.png`와 선택 해시를 남겼다. 생성본도 첫/마지막 자세가 너무 비슷해 원형 그대로 출하하지 않았다. sprite-gen의 canonical `extract_component_images`로 연결 부품을 분리하고, 행 전체에 공통 축소율을 적용해 native grid와 기존 20색·이진 알파로 정리했다. 조끼 군집을 가져온 뒤 무릎·발·팔의 관절과 손을 수작업 픽셀 보정했다. 네 방향을 각각 새로 생성하거나 32개 그림을 독립 생성한 결과라고 주장하지 않는다. SW/NW는 x′=32−x와 몸체 팔레트 재조명으로 파생했으며 머리는 원본에서 복원했다.

몸통 높이 변화는 0/+1/0/−1px, 머리는 고정이다. 다리와 팔의 반대 스윙, 교대 지지발, 굽힌 무릎·뒤꿈치와 네 개의 서로 다른 포즈를 넣었다. 보행 높이는 모두 42px, 발 기준 (16,60), y60의 지지발을 유지했다. 3→0도 동일한 순환 제어점으로 연결했다. NW의 wrap 픽셀 변화량은 253으로 가장 큰 내부 변화 248보다 5픽셀 많다. 이 수치는 연결의 자연스러움에 대한 합격 기준이 아니다.

타이핑은 소매 끝·굽힌 팔꿈치·피부색 전완·분리된 작은 손바닥과 손가락의 누름을 넣었다. SE/SW 손 (27,44)/(29,44), (3,44)/(5,44), NE/NW 손 (27,32)/(29,32), (3,32)/(5,32)를 유지했다. 접점 주변 실제 피부는 모든 32개 손/프레임에서 Chebyshev 거리 0 또는 1px다. 골반 (16,44) 영역과 하체 y47 이하 픽셀은 수정 전과 같다.

검사 명령:

```text
python -B tools/iso_check.py assets/art/iso/characters/professor/professor.iso.json --stage final --tolerance 0
```

**PASS 11 / SKIP 5 / FAIL 1, exit 1.** `validation.txt`와 `runs/motion-rev1/check-final.txt`가 실제 출력이다. 38/24행, 32×64 셀, 팔레트, 알파·패딩, 고정 앵커와 별칭 검사는 통과했다. SKIP은 바닥·가구 전용 항목 및 blink 중복 face-lock이다.

실패는 `face-lock / blink: blink changes non-eye RGBA`다. 사용자의 **blink 아틀라스 수정 금지**를 지켜, 변경된 normal 4/5/8/9행의 총 16개 프레임이 과거 blink 몸체와 다르다. 눈·머리 불안정으로 생긴 실패가 아니다. 현재 상태에서 눈을 감으면 이 네 행의 몸체가 이전 자세로 순간 교체된다. final 계약 또는 출하 합격을 주장하지 않는다. 별도 승인 범위의 blink 몸체 동기화가 없으면 이 실패를 해결할 수 없다.

추가 직렬 출력 검증: 152개 머리 픽셀 비교, 7개 고정 파일의 SHA-256 보존, 30개 비대상 행 보존, 152개 연결된 실루엣, 네 방향의 4개 보행/타이핑 구분, 32개 손 피부 접점, 골반·하체 보존을 확인했다. 증거는 `runs/motion-rev1/motion-proof.json`이다. 반대 스윙 수치는 직접 작성한 관절 제어점의 수치 증거이며 외부의 독립적인 품질 판정은 아니다.

v2 가구를 원본 접점·part 깊이 순서로 합성해 C/H 좌표의 정확한 일치를 확인했다. 참조 시각 2026-10-03T22:07:15.131635+00:00; 사용한 가구 파일 해시는 `runs/motion-rev1/contact-proof.json`에 있다. 이 스냅샷에서 뒤쪽 머리 노출은 NW 91.18%, NE 88.66%, 의자만 고려하면 둘 다 100%다. 앞쪽은 100%다. 머리를 바꾸지 않았으므로 이 수치를 이번 캐릭터 모션의 가림 개선 성과로 돌리지 않는다. 병렬 가구 작업의 후속 변경에 대해서는 검증하지 않았다.

남은 약점: 다리·신발이 각지고 4프레임 리듬에 딱딱함이 남는다. 고정 머리와 움직이는 어깨 사이가 일부 단계에서 뻣뻣하다. 손 군집은 이전 단일 점보다 읽히지만 작고, 전완이 특히 앞쪽에서 길게 보인다. **v2 합성에서 NE/NW 손은 보이지만 SE/SW의 접점 주변 손 피부는 책상 전면에 전부 가려진다.** 모든 방향의 실제 장면 손 가독성을 달성했다고 주장하지 않는다. 다른 lane의 가구나 앵커는 수정하지 않았다. hero의 부드러운 볼륨·매력과 같은 수준이라고도 판단하지 않는다.

이미지 13개를 보았고 hero 크롭과 native1×/NEAREST3×를 비교했다. GIF 파일의 프레임 수·전체 주기는 확인했으나 실제 재생의 자연스러움, 독립 사람의 시각 승인, WPF/runtime/DPI/실제 깊이 순서는 검증하지 않았다. dotnet 전체 테스트, git 쓰기, pack 빌드·업로드·배포는 하지 않았다.

실측 **0.2434 lane-hours / 반올림 0.24시간**, 2026-10-03 21:55:53–2026-10-03 22:10:29 UTC. 읽기·생성 대기·픽셀 보정·검증·검수를 포함한다. 첫 시계 관측 전의 초기 지침 읽기는 측정에서 제외했고 다른 lane 시간은 합산하지 않았다. 이 기록을 승인된 생산 속도로 일반화하지 않는다.

직접 파일 쓰기는 교수 폴더에만 했다. 내장 이미지 도구가 자동 저장한 원본 캐시는 `$CODEX_HOME/generated_images`에 남으며, 선택본을 교수 폴더에 복사했다. 공유 `/tmp` 이름이나 tools helper를 사용하지 않았다. sprite-gen workflow CLI의 로그인 확인은 차단됐지만 사용자가 지정한 현재 세션의 내장 이미지 도구는 두 번 성공했다. CLI gen-set 완료를 주장하지 않는다. finish의 남은 질문은 선택적 전역 기본 설정 저장뿐이며 저장하지 않았다.

현재 결과 재생성: sprite-gen venv Python의 `-B`로 `runs/motion-rev1/build_motion.py` → `verify_motion.py` → `review_wrap.py`를 실행한다. `verify_motion.py`의 exit 0은 로컬 보존/접촉 검사에 예상 밖 오류가 없다는 뜻이며, 내부에서 실행한 final checker의 exit 1과 FAIL 1을 그대로 기록한다. 이전 `runs/v1/build.py`는 이번 모션을 되돌리므로 현재 결과 재빌드에 사용하지 않는다.
