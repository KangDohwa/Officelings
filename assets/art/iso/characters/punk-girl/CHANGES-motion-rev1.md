punk-girl motion-rev1 — 시각 승인 및 blink 통합 보류

기존 `punk-girl-iso.png`의 보행 4행(4–7, 16셀)과 타이핑 4행(8–11, 16셀)을 수정했습니다. “8 walk rows” 대신 고정 38행 계약의 실제 행 배치를 따랐습니다. manifest 두 파일에는 새 마스터·파생 근거를 기록했고 `validation.txt`를 현재 결과로 교체했습니다. 기존 NOTES와 기존 iso-* 검수 PNG는 이전 제작 기록입니다. 이번 수정의 검수 파일은 `motion-rev1-*`입니다.

`motion-rev1-walk-before-after.png`는 네 방향 모든 단계의 전후 1×/3×와 hero crop을 나란히 보여 줍니다. `motion-rev1-typing-before-after.png`는 손 네 단계를, `motion-rev1-contact-before-after.png`는 v2 가구 접점을 보여 줍니다. 각 방향 `walk-{se,sw,ne,nw}-motion-rev1-3x.png`와 같은 이름의 GIF를 남겼습니다. GIF는 125ms/프레임, 8fps, 원래 순서 0→1→2→3→0입니다.

sprite-gen 스킬·workflow를 읽고 실행했습니다. CLI 로그인 점검은 unavailable였으므로 명시적으로 요청된 내장 image_gen을 사용했습니다. SE/NE contact 마스터를 직렬로 각각 1회 생성했고, sprite-gen `cutout --key green`으로 정리했습니다. 마스터를 한 번만 네이티브 크기로 변환한 후 생성된 재킷/부츠 클러스터를 활용해 팔·다리 관절을 다시 그렸습니다. SW/NW는 몸만 x′=32−x로 파생하고 상좌 명암을 보정했습니다. 머리·가르마는 반전하지 않았습니다. 프로토콜의 gen-set/행 추출 전체 경로를 실행한 것으로 주장하지 않습니다. 원본, 프롬프트, 보정·검증 스크립트는 `runs/motion-rev1/`에 있습니다. 설정·인증 파일은 수정하거나 복사하지 않았습니다. 내장 도구가 원본 2장을 기본 `C:/Users/<user>/.codex/generated_images/01a103c3-4e7d-7e72-a508-8a986660eb2f/`에 자동 캐시했으며, 프로젝트에 쓰는 사본은 이 lane 안에 있습니다.

보행은 넓은 접촉 포즈, 굽힌 무릎/들린 뒤꿈치, 다리와 반대인 팔 스윙, 몸통 bob `[0,1,0,-1]`을 사용합니다. 머리는 bob하지 않습니다. 타이핑은 소매/손목/전완/납작한 두 손바닥/손가락을 구분하고 손가락 누름을 번갈아 배치했습니다. 접점 사이 어두운 틈은 두 손을 구분하기 위한 픽셀입니다.

실제 최종 명령: sprite-gen venv Python으로 `tools/iso_check.py assets/art/iso/characters/punk-girl/punk-girl.iso.json --stage final --tolerance 0`. **PASS 11 / SKIP 5 / FAIL 1**, exit 1. `validation-motion-rev1.txt`에 원문이 있습니다. FAIL은 `face-lock / blink: blink changes non-eye RGBA`입니다. blink 파일 보존 지시를 우선했으므로 변경된 SE/SW walk/typing의 16셀 몸이 옛 blink 몸과 다릅니다. 38 normal/24 blink 행 수, palette 17색, alpha/guard, 앵커, aliases는 통과했습니다. 따라서 최종 통합 승인이나 blink 계약 완성을 주장하지 않습니다. blink 몸 동기화는 별도 허용이 필요합니다. 눈·얼굴 변화는 이 FAIL의 원인이 아닙니다.

별도 픽셀 검증: 152셀의 원래 머리 RGBA 보존, headOffsetsByClip/faceLock/모든 고정 시트 메타데이터 동일, blink·SE/SW eye PNG SHA-256 동일, 나머지 30행 픽셀 동일, 대상 32셀 연결 실루엣, 방향별 walk/typing 4단계 구별. 16 보행 셀 모두 planted sole y=60, 32 타이핑 손 접점 모두 실제 피부 오차 0px. 마지막→첫 단계 픽셀 변화는 SE 233, SW 217, NE 220, NW 218로 각 방향 내부 전환 최댓값보다 작습니다. 이는 seam 지표일 뿐 자연스러운 애니메이션 승인 근거는 아닙니다. `motion-proof.json`, `blink-parity-conflict.json`, `publication.json`에 근거가 있습니다.

가구를 다른 lane이 수정 중이어서 22:04 UTC 무렵 읽은 v2 desk/chair manifest·필요 atlas를 lane 내부에 고정 사본으로 남기고 published depth queue를 합성했습니다. C/H 메타데이터 오차는 0px입니다. 해당 사본에서 NE 머리 215/267(80.5%), NW 201/238(84.5%)가 보입니다. 머리 가시율은 전후 동일하며 가구 lane의 변화에 따른 결과입니다. 이 lane의 개선으로 주장하지 않습니다. NE/NW hand ROI는 17/17 피부 픽셀이 보이지만 SE/SW는 각각 0/19, 0/16으로 CRT/책상에 가립니다. 네 방향 raw sprite에 손을 그렸지만 네 방향 모두 합성 화면에서 읽힌다고 주장하지 않습니다. `contact-proof.json`에 입력 해시·occluder·전후 수치가 있습니다.

남은 약점: 고정된 큰 머리 아래 네 프레임 몸 동작은 여전히 끊겨 보일 수 있고, 맨다리가 가늘며 부츠/손의 세부가 단순합니다. 4단계는 연속한 contact/down/passing/up 8장짜리 해부학적 gait를 대신하는 짧은 완전 루프의 샘플이므로 중간 자세가 생략됩니다. 양쪽 화면에 발이 제자리에서 움직이는 실제 이동 보폭/발 미끄러짐은 측정하지 않았습니다. hero 같은 풍부한 자세·매력에 도달했다고 주장하지 않습니다. 의상 비대칭 세부에 대한 독립 사람 승인도 남습니다.

이미지 열람은 원본·기존 검수·생성 마스터·최종 비교를 합해 14회이며, 마지막 전후 보행/손/가구 비교도 실제로 보았습니다. GIF 파일의 프레임 순서·타이밍을 만들었으나 애니메이션 플레이어에서 재생하지 않았습니다. WPF/앱 실행, pack 소비, 실제 이동/typing 재생, DPI/깜빡임 전환, runtime depth는 검증하지 않았습니다. src/tests/docs/가구/pack/build_pack을 수정하지 않았고, commit/add/stash/checkout/전체 dotnet 테스트/추가 에이전트도 실행하지 않았습니다.

실측 시간은 `runs/motion-rev1/timing.json`에 기록합니다. 첫 UTC 계측 2026-10-03T21:55:36Z부터 완료 기록까지 단일 lane 점유시간이며 생성 대기·검수·문서 작성이 포함됩니다. 그 첫 계측 전의 짧은 최초 읽기는 계측에서 제외되며 추정해 더하지 않았습니다. 다른 lane 시간이나 제작 처리량으로 일반화하지 않습니다.
