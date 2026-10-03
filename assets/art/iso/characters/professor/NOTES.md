> motion-rev1 현재 상태: 걷기·타이핑 32프레임 수정, 머리·눈·blink 보존. 최종 checker **PASS 11 / SKIP 5 / FAIL 1**(동결 blink 몸체 불일치). [현재 변경·검수·한계](CHANGES-motion-rev1.md)를 먼저 보세요. 아래 내용과 기존 iso-*.png 검수판은 이전 버전 기록입니다.

# Professor ISO — 파일 계약 완료, 시각 승인은 조건부

명시적인 참조 전송 승인을 받고 runs/v1의 준비 시트·크롭·프롬프트에서 재개했다. 준비를 다시 하지 않았다. 저장소 쓰기는 이 professor 폴더뿐이며 다른 lane, src, tests, docs, 가구, pack을 수정하지 않았다. git 쓰기와 dotnet 테스트를 실행하지 않았다.

완성 파일: professor-iso.png (128×2432, 38×4 / 152셀), professor-iso-blink.png (128×1536, 24×4 / 96셀), professor-eyes-se.png·professor-eyes-sw.png, 동일 내용의 professor.iso.json·professor-iso.manifest.json, iso-contact.png·iso-rows-review.png·iso-seated-review.png·iso-vs-flat.png, validation.txt.

sprite-gen Codex 생성은 7회, 한 번에 1개였다. SE idle은 2회 중 a2 선택, 나머지 NE idle·SE/NE walk·SE/NE seat은 각 1회다. 제공자 보고 시간 합계 330.687초는 lane-hours에 이미 포함된다. 실제 잠금 정체성 및 hero 교수 크롭을 참조로 전송했다. 원본 마스터·프롬프트·보고서·선택 해시는 runs/v1에 남아 있다.

뒤넘긴 흰 머리, 흰 수염, 갈색 조끼, 크림 셔츠, 작은 나비넥타이를 유지했다. 원본 README가 거절한 안경은 추가하지 않았다. 평면 정면 얼굴을 붙이지 않고 생성한 대각선 머리를 사용했다. 마스터를 한 번 균일 축소해 원본 정체성 팔레트와 이진 알파로 정리하고, 머리·몸통·굽힌 다리 부품과 팔꿈치·접촉 보정을 파생했다. 별도 248개 그림을 생성한 것은 아니다.

마스터 몸은 40px이며, 머리/상체를 고정적으로 2px 올려 연결한 최종 idle은 42px로 허용 범위 38–42px 상단이다. 발 (16,60), 골반 (16,44), 네 방향 손 접점과 전환 phaseContacts는 그대로다. 프레임별 재중심화나 피팅은 하지 않았다. SW/NW는 x′=32−x로 굽고 조명을 다시 칠했다. 뒤넘긴 머리 방향은 해부학적 뒤를 따르며 별도 가르마/액세서리가 없다. 전화는 같은 해부학적 손으로 별도 자세를 잡았다.

각 방향 머리는 동일 바이트다. 서기 상대 오프셋 0, 착석 −3, 내리기 0/−1/−2/−3, 일어서기 역순을 기록했다. 인접 단계는 1px지만 전환 전체 누적 이동은 3px이다. 기존 파일럿과 같은 해석으로, 최대 1px를 전환 전체/모든 동작 사이 총변위로 적용하면 충족한다고 주장하지 않는다. blink는 SE/SW만 있고 NE/NW 눈 마스크는 없다. 모든 action/facing 별칭, neutral phase0 대기, front-only 프로필과 소품/말풍선 바인딩을 명시했다. 소품은 몸체에 굽지 않았다.

최종 tools/iso_check.py assets/art/iso/characters/professor --tolerance 0:
**PASS 12 / SKIP 5 / FAIL 0, exit 0.**
SKIP은 바닥 이음새·가구 depth 및 blink 시트의 중복 face-lock 검사로, 품질 합격 뜻이 아니다. 20개 정확한 허용 RGB를 사용한다. 추가 검사는 152개 canonical 머리 바이트 비교·152개 연결 몸체, 네 방향별 서로 다른 4개 보행/타이핑 프레임, 전환 역순/양 끝 일치, 4개 idle 피부 그립, 32개 동작별 피부 소품 그립, 32개 타이핑 피부 접점 ≤1px를 확인했다. 증거: runs/v1/derivation-proof.json, contact-proof.json.

v2 가구의 원본 manifest/PNG와 고정 접점을 읽어 실제 순서로 합성했고 원본 파일 해시를 기록했다. 머리 노출률:

| 작업자 | 의자만 고려 | 책상·모니터 등 전면 가림 모두 고려 |
| --- | ---: | ---: |
| NW | 82.77% | **55.46%** |
| NE | 82.77% | **28.99%** |
| SW | 98.43% | 98.43% |
| SE | 97.24% | 97.24% |

NE 전체 노출은 파일럿의 약 42%보다도 낮다. 전체 장면의 가림 개선 성공을 주장하지 않는다. NW는 desk__front__se__00, NE는 특히 desk__front__sw__02가 추가로 가린다. 교수 폴더에서 허용 높이와 고정 접점을 지킨 자세 보정으로 해결하지 못했으며 다른 lane의 가구나 순서는 수정하지 않았다.

이번 재개에서 이미지 14개를 보았다. 마스터·최종 보행·38행·정체성/눈 감기·v2 착석을 1×/3× NEAREST 검수판으로 확인하고 실제 hero 교수 크롭과 비교했다. 정체성은 알아볼 수 있지만 hero와 같은 부드러운 볼륨/매력을 달성했다고 판단하지 않는다. 보행과 일부 팔/손이 기계적이고, 뒤쪽 팔의 층 분리가 약하며, 수면은 미약한 좌식 다리 기대기다.

마지막 프로그램 검사에서 커피 2단계 손이 수염에 덮인 것을 찾아 x22→25로 보정했다(SE/SW). 32개 피부 그립과 최종 checker는 통과했다. 이미지 예산 때문에 이 미세 보정의 최종 화면은 다시 보지 않았다.

검증하지 못한 것: GIF 실제 재생/루프 연결의 자연스러움, 독립적인 사람의 정체성/모션 승인, WPF/runtime/DPI/hover·실제 깊이 정렬, sofa 접촉, 컵·종이·전화의 실제 소품 합성. GIF 두 개는 runs/v1에 저장했지만 재생 합격은 주장하지 않는다. checker 통과는 시각/출하 승인과 별개다.

측정 시간: 이전 준비 0.1476 lane-hours (20:56:02–21:04:53 UTC), 이번 재개 0.3814 lane-hours (21:08:53–21:31:46 UTC), **합계 0.5290 lane-hours / 반올림 0.53시간**. 비활성 승인 대기 21:04:53–21:08:53은 제외했다. 읽기·생성 대기·수정·검증·검수를 포함하며 다른 lane 시간은 합산하지 않았다. 종료 관측 UTC: 2026-10-03T21:31:46Z. 생산 속도 승인치로 일반화하지 않는다.

재빌드: sprite-gen venv Python으로 runs/v1의 normalize.py → build.py → verify.py → review_contacts.py. curation UI와 기본 설정 저장은 하지 않았다. finish의 needs-input은 선택적인 전역 기본 설정 저장 질문뿐이며 출력 폴더 외 쓰기 금지에 따라 저장하지 않았다. 이전 pending-status.json은 승인 전 실패의 과거 기록이다.
