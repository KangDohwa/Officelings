[English](CONTRIBUTING.md) | [한국어](CONTRIBUTING.ko.md)

# 기여 안내

버그 제보, 문서 수정, 코드와 아트 기여를 받는다. 새 의존성이나 큰 기능은 작업 전에 이슈에서 논의한다. PR 하나에는 한 가지 변경을 담는다.

## 버그 제보

[이슈](https://github.com/KangDohwa/Vibedotcom/issues)에 Windows 버전과 OS 빌드(`winver`), 관련 도구 버전(Codex, Claude Code, Antigravity, Windhawk), .NET SDK·런타임 버전, 사용한 커밋이나 릴리스를 적는다. 재현 순서, 예상 동작, 실제 동작을 설명한다. 창 모드, 클릭 통과, 확대 비율, 별도 팩·배치를 썼다면 함께 적는다.

진단 자료는 민감한 내용을 지운 뒤 첨부한다. 실제 프롬프트와 개인 경로, 원본 세션 로그, 인증 정보, 실제 `state.json`은 올리지 않는다. 재현 예시와 fixture에는 만들어 낸 문구만 쓴다. 스크린샷은 `--demo`로 만들고 툴팁도 확인한다. [개인정보 안내](docs/privacy.md)를 참고한다. 보안 제보 경로가 마련돼 있다면 그 경로를 사용하고, 개인 데이터가 포함된 공격 재현 자료를 공개 이슈에 올리지 않는다.

## 빌드와 확인

Windows 11과 .NET 10 SDK를 사용한다. 저장소 루트에서 실행한다.

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
dotnet run --project src/Vibedotcom.App --no-build -- --demo
```

Python 아트 도구는 Pillow와 NumPy가 있는 기존 환경에서 실행한다. 변경과 관련된 검사를 고른다.

```powershell
python -B -m unittest discover -s tools -p 'test_*.py'
python -B tools/test_anim_normalize.py
# 설치된 Windhawk clang으로 모드와 네이티브 테스트 실행 파일을 빌드한다.
powershell -NoProfile -ExecutionPolicy Bypass -File windhawk/compile.ps1 -Tests
```

정규화 검사는 독립 스크립트라 unittest 검색만으로 실행되지 않는다. Windhawk 명령은 컴파일만 하며 Explorer에 주입하지 않는다. 네이티브 테스트 실행과 수동 셸 점검은 [Windhawk 안내](windhawk/README.md), 렌더링 검사는 [제한과 검증](docs/limits.md#verification)에 있다. 일반 테스트 과정에서 로그온 작업을 등록하거나 다른 앱의 창을 조작하지 않는다.

## 코드와 커밋

주변 코드의 스타일을 따른다. C# nullable 분석을 켠 상태로 유지한다. 기존 플랫폼 API를 우선 쓰고, 의존성 추가는 먼저 논의한다. 기존 검사로 오류를 잡지 못하는 로직 변경에는 작은 관련 테스트를 추가한다. 문구만 바꿨다면 내용과 링크를 검토한다.

[Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)를 사용한다. 예시는 `fix(tracker): 교체된 세션 로그 처리`와 `docs: 사용 안내 정리`다. 기존 커밋 요약은 주로 한국어지만, 다른 기여자의 영어 커밋과 PR도 환영한다.

## 아트

직접 만든 작업이나 [CC BY 4.0](assets/LICENSE.md)으로 기여할 권리가 있는 작업만 제출한다. 외부 자료라면 제작자, 출처, 라이선스를 적는다. 외부 로고와 브랜드 마크는 포함하지 않는다. AI를 활용한 아트는 모델·서비스와 적용 약관을 밝혀 관리자가 검토하도록 한다.

캐릭터 제작 셀은 32x64, 타일은 32x32다. 픽셀 경계와 명시된 프레임·앵커 구조를 유지한다. 방향별 눈을 고정하고, 알파와 검토된 얼굴 패치 바깥 픽셀을 보존하며, 제작 이력을 남긴다. 얼굴 고정 검사만으로 해부학이나 움직임 품질이 검증되는 것은 아니다. 시트를 바꾸기 전에 [아트 파이프라인](docs/art-pipeline.md), [얼굴 고정 규칙](docs/facelock.md), [앉은 자세](docs/seated.md), [에셋 형식](docs/asset-format.md)을 읽는다. 모아 보기 이미지나 데모 렌더를 첨부하고 실제로 수행한 검사를 적는다.

## PR 전 확인

- 문제와 변경 후 동작을 설명하고, 무관한 정리를 섞지 않는다.
- 실행한 검사와 실행하지 못한 검사를 적는다.
- fixture에는 가상 문구만 쓴다. 변경분과 이미지에서 개인 데이터와 공식 브랜드 파일을 제외한다.
- 관련 문서와 링크를 갱신한다. 아트는 크레딧, 제작 이력, 필요한 미리보기를 포함한다.
- 프로젝트 라이선스로 기여할 권리가 있는지 확인한다.

## 기여 조건과 대화

PR을 제출하면 코드는 MIT, 아트는 CC BY 4.0으로 제공하는 데 동의한 것으로 본다. 배포와 기여에 같은 라이선스를 적용한다(inbound = outbound). 기여자의 저작권은 유지되며, 이 조건으로 제공할 권리가 있어야 한다. 이 안내는 별도 기여 계약을 요구하지 않는다. DCO `Signed-off-by` 표기는 환영하지만 필수는 아니다. [Developer Certificate of Origin](https://developercertificate.org/)을 참고한다.

서로 예의를 지키고 사람보다 작업 내용에 집중한다. 개인정보를 존중하고 구체적인 피드백을 남긴다. 관리자가 답할 시간을 주며, 괴롭힘과 인신공격은 하지 않는다.
