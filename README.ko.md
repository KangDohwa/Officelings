[English](README.md) | [한국어](README.ko.md)

# Vibedotcom

Vibedotcom은 Windows에서 최근 Codex, Claude Code, Antigravity 세션을 작은 픽셀 사무실로 보여준다. 캐릭터는 책상에서 일하고, 기다리고, 보고서를 전달하거나 잠깐 쉰다. 마우스를 올리면 모델, 프로젝트, 최근 활동이 보이고, 확인된 부모 세션은 점선으로 연결된다. 세션을 관찰할 뿐 작업을 배정하거나 에이전트를 조작하지 않는다. 각 도구 소유자와 제휴하거나 승인을 받은 프로젝트는 아니다.

## 현재 상태

2026-10-04 작업 트리 기준이다. 테스트 결과는 기존 검토 기록을 참고했으며 이번 문서 정리에서 새로 실행하지 않았다.

| 영역 | 상태 |
| --- | --- |
| 트래커와 시뮬레이션 | 구현됨. 파서·활동·경로 탐색 테스트가 있다. Antigravity는 DB 복사본과 로컬 API도 조회한다. |
| WPF 앱과 편집기 | 구현됨. 오프스크린 검증 기록이 있다. 실제 셸 동작과 편집기 직접 사용 검증은 남아 있다. |
| 아트와 배치 | 고정 책상, 앉은 자세, 측면 책상, 얼굴 고정, 소품 정규화가 연결돼 있다. 캐릭터 아트는 계속 수정 중이다. |
| Windhawk | 컴파일과 렌더러·자체 창 테스트 기록이 있다. Explorer 주입과 수동 셸 검증은 아직이다. |
| 릴리스 | 로컬 패키징과 해시 검사가 있다. 리뷰에서 발견한 문제와 공개 전 점검은 남아 있다. |

[제한 사항](docs/limits.md), [앱 리뷰](docs/review-app.md), [공개 전 체크리스트](docs/release-checklist.md)를 참고한다.

## 두 가지 실행 방식

두 방식은 .NET 10 추적 코어와 아트 팩을 공유한다.

- 단독 앱: WPF 앱이 세션 자료를 직접 읽는다. Windhawk가 필요 없다.
- Windhawk: 헤드리스 트래커가 만든 `state.json`을 C++ 모드가 읽어 Explorer 안에 그린다.

공유 파일 형식은 [상태 계약](docs/state-schema.md)에 정리돼 있다.

## 빠르게 시작하기

Windows 11과 .NET 10 SDK가 필요하다. 런타임을 포함하지 않은 빌드는 .NET 10 Windows Desktop Runtime을 사용한다. 앱과 트래커에는 별도 NuGet 런타임 패키지가 없고, 테스트 패키지는 Git에서 제외한 `.packages/`에 복원된다.

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
dotnet run --project src/Vibedotcom.App --no-build
# 미리보기와 스크린샷에는 가상 세션을 사용한다.
dotnet run --project src/Vibedotcom.App --no-build -- --demo
# Windhawk용 헤드리스 트래커. Ctrl+C로 종료한다.
dotnet run --project src/Vibedotcom.Tracker --no-build
```

사무실을 오른쪽 클릭하고 **Edit office**를 선택하면 배치를 편집한다. [편집기 안내](docs/editor.md)와 [앱 사용법](docs/app-usage.md)에 배치, 설정, 배포용 빌드 방법이 있다. 단독 앱에는 헤드리스 트래커가 필요 없다. Windhawk를 쓰려면 [설치 안내와 수동 점검](windhawk/README.md)을 따른다.

세션 정보에는 개인 경로와 프롬프트가 포함될 수 있다. 화면이나 버그 보고를 공유하기 전에 [개인정보 안내](docs/privacy.md)를 읽는다. 서명 없는 빌드는 [Windows 보안 안내](docs/windows-security-notice.md)를 참고한다.

## 조작

- 마우스 휠(Ctrl 유무 무관), `+` / `-`로 1~6배 확대한다. 드래그로 화면을 옮기고, 캐릭터에 마우스를 올려 세션 정보를 본다.
- 사무실이나 트레이를 오른쪽 클릭하면 화면 맞춤, 일반·항상 위·바탕화면 모드, 클릭 통과, 테두리, 배경, 불투명도를 바꾼다.
- 트레이 아이콘을 두 번 클릭하거나 **Show normal, clickable office**를 선택하면 클릭 가능한 일반 창으로 돌아온다. 앱을 닫으면 종료되고 트레이 아이콘도 사라진다.

Windhawk는 자체 설정을 쓰며 트레이 아이콘이 없다. 어느 방식도 보안 잠금 화면에는 표시되지 않는다.

## 스크린샷

아래는 `--demo` 데이터만 사용한 이전 오프스크린 렌더다. 현재 아트 변경이 모두 반영된 화면은 아니다. 출처와 캡처 명령은 [앱 사용법](docs/app-usage.md#screenshots)에 있다.

![이전 데모 사무실, 2배 확대](docs/img/demo-office-2x.png)
![가상 문구와 경로를 사용한 데모 툴팁](docs/img/demo-tooltip.png)

`docs/img/`에 남은 TODO: 고정 사무실과 앉은 자세 화면 갱신, 편집기 화면 추가, 수동 셸 검증 뒤 `--demo`로 바탕화면 모드 캡처.

## 문서

- 사용 안내: [앱](docs/app-usage.md), [트래커](docs/tracker.md), [편집기](docs/editor.md), [개인정보](docs/privacy.md), [Windows 보안](docs/windows-security-notice.md), [제한과 검증](docs/limits.md), [가상 세션 QA](docs/simulator.md).
- 형식: [상태 스키마](docs/state-schema.md), [에셋과 배치](docs/asset-format.md), [정보 출처와 추론](docs/sources-and-inference.md), [프로젝트 명세](SPEC.md).
- 아트: [파이프라인과 크레딧](docs/art-pipeline.md), [얼굴 고정](docs/facelock.md), [앉은 자세](docs/seated.md), [소품 정규화](docs/anim-normalize.md). 파이프라인 문서에서 제작 매니페스트와 캐릭터별 기록도 찾는다.
- Windhawk와 릴리스: [Windhawk 안내](windhawk/README.md), [패키징](tools/release/README.md), [공개 전 체크리스트](docs/release-checklist.md).
- 기여: [English](CONTRIBUTING.md), [한국어](CONTRIBUTING.ko.md). 권리 관련 안내: [상표](TRADEMARKS.md), [한국어 요약](TRADEMARKS.ko.md), [외부 구성요소 고지](THIRD_PARTY_NOTICES.md).
- 조사 기록: [Antigravity](docs/antigravity-notes.md), [Pixel Agents](docs/pixel-agents-notes.md), [Windhawk](docs/windhawk-notes.md). 각 기록 시점의 코드와 관찰 결과를 다룬다.
- 리뷰 기록: [코어와 트래커](docs/review-core.md), [앱](docs/review-app.md), [Windhawk](docs/review-windhawk.md).

## 라이선스

코드는 [MIT](LICENSE), 저작권 표기는 Copyright (c) 2026 KangDohwa다. 프로젝트 아트와 에셋은 [CC BY 4.0](assets/LICENSE.md)으로 제공한다. 아트는 AI 이미지 모델로 생성했다. 외부 로고와 브랜드 아이콘은 이 라이선스에 포함되지 않는다. [상표 안내](TRADEMARKS.md)와 [로컬 오버레이 안내](assets/brand/README.md)를 참고한다.
