# game-platform - 메인 지시서

이 문서는 프로젝트의 메인 지시서입니다. 작업 시작 전 아래 보조 지시서를 모두 읽고 진행하세요.

이 문서는 상위 폴더의 형제 프로젝트(`image-readers`, `text-readers`, `english-training`, `video-vault`)들이 공통으로 따르는 지시서 구조와 작업 원칙을 뽑아 정리한 것입니다. 프로젝트별 세부 내용(프로젝트 한 줄 요약, 기술 스택 상세, doc/ 목록 등)은 아래 표를 채워 넣어 확장하세요.

## 프로젝트 한 줄 요약
썸네일 카드로 게임을 관리하고, 카드에서 바로 정보를 확인하거나 지정한 실행 파일을 실행하는 WPF 데스크톱 런처.

## 기술 스택
형제 프로젝트들의 공통 스택(참고용, 이 프로젝트 성격에 맞게 조정):
- .NET 8
- WPF (Windows Presentation Foundation) — C#
- MVVM 패턴 권장 (Repository → Service → ViewModel → View 계층 분리)
- 데이터 저장: JSON 파일 (`data/` 또는 `.data/` 폴더)

## 폴더 구조 (공통 패턴)

```
game-platform/
├── CLAUDE.md          # 메인 지시서 (이 파일)
├── game-platform.sln
├── .gitignore
├── doc/               # 기능별 상세 보조 지시서 (게임 목록/썸네일/설정 데이터는 %LOCALAPPDATA%\GamePlatform\ 사용 — doc/common-management.md 참고)
└── src/
    └── GamePlatform/  # 앱 본체 소스 (.NET 8 WPF)
```

## 보조 지시서 목록 (doc/ 폴더)

작업 성격에 맞는 문서를 참조하세요.

| 문서 | 내용 |
|---|---|
| [doc/game-management.md](doc/game-management.md) | 게임 카드 목록(메인 화면), 카드 추가/삭제, 대표 썸네일, 실행, 정보 창(`GameInfoWindow`), `GameItem` 데이터 모델 |
| [doc/common-management.md](doc/common-management.md) | 저장 경로(`AppPaths`), 설정 저장, video-vault에서 이식하는 썸네일/드래그앤드롭 공용 인프라, 오류 처리 |

## 작업 원칙

형제 프로젝트들에 공통으로 적용되는 규칙:

1. 각 기능을 구현/수정하기 전, 해당 작업과 관련된 보조 지시서(doc/ 폴더)를 먼저 확인한다.
2. 보조 지시서 간 내용이 충돌하거나, 아직 결정되지 않은 항목을 다뤄야 할 때는 임의로 확정하지 말고 합리적인 기본값을 제안한 뒤 사용자 확인을 받는다.
3. 코드는 MVVM 패턴을 따르며, View(XAML)와 로직(ViewModel/Service/Repository)을 분리한다.
4. 기능/구조/결정 사항이 변경되면 관련된 보조 지시서(doc/ 폴더 및 이 메인 지시서)를 함께 업데이트한다. 문서와 코드가 어긋난 상태로 두지 않는다.
5. 문서를 고칠 때 별도의 백업(history 폴더 등)은 두지 않는다 — git 커밋 이력이 변경 기록 역할을 한다. 의미 있는 단위로 커밋한다.
6. 사용자 명령으로 파일이 수정되고 작업이 성공적으로 끝나면, 별도 요청/확인 없이 git commit(커밋 메시지는 영어로 직접 작성)과 push까지 수행한다.
7. 명령 수행 후, 이번 작업에서 참조하거나 수정한 보조 지시서(doc/ 폴더) 목록을 사용자에게 알려준다.
