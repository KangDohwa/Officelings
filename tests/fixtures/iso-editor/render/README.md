# UI fake 렌더 golden

`IsoEditorSmoke`의 최종 PNG 79개를 검토한 뒤 대표 화면 14개를 선택했다. seed 42, epoch `2026-10-04T00:00:00Z`이며 `goldens.json`에 SHA-256, 원본 pack hash와 camera, 배율 비교 수치를 보관한다. 200% top/bottom은 글자 크기 주입에 대한 offscreen 검사다.

이 화면의 editor/importer/computer adapter는 UI-owned scripted fake다. 실제 L/C/O/P validation, custom catalog 저장, mounted computer, native DPI/창/접근성 수락을 증명하지 않는다. import 성공은 가짜 서비스 호출과 dialog 상태만 확인한다. actual source thumbnail은 현재 API의 검증 metadata가 없어 표시하지 않는다.

재현 명령, 공유 파일 member/줄, 미검증 항목과 계약 변경 요청은 [UI QA 기록](../../../../docs/iso-editor-ui.md)에 있다. root fixture index와 기존 flat/seated goldens는 변경하지 않았다.
