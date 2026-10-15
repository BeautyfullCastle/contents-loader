# Contents Loader

Resources처럼 폴더에 넣고 경로 문자열로 부르는 애셋 로더입니다. 번들 분할, 중복 제거, 다운로드, 씬 단위 언로드는 패키지가 처리합니다.

## 사용법

1. 프로젝트 어디에든 `Contents` 폴더를 만들고 애셋을 넣습니다. `Contents` 폴더는 여러 개여도 됩니다.
2. `Contents` 아래의 상대 경로(확장자 제외)로 로드합니다.

같은 `Contents` 폴더 안에서는 확장자만 다른 애셋(`Cannon.prefab`, `Cannon.png`)을 함께 둘 수 있습니다. `Load<T>`는 `Resources.Load`처럼 타입 `T`를 가진 애셋을 고릅니다. 서로 다른 `Contents` 폴더끼리 같은 경로가 겹치면 빌드 전에 오류가 납니다.

```csharp
using H.ContentsLoader;

// Assets/Game/Contents/Towers/Cannon.prefab
var cannon = Content.Load<GameObject>("Towers/Cannon");

// 기기에 없으면 받은 뒤 로드합니다.
var dragon = await Content.LoadAsync<GameObject>("Bosses/Dragon");

// 로딩 화면에서 폴더 단위로 미리 받습니다.
await Content.Download("Season01", progress);

// 씬을 넘어 유지할 애셋만 App 수명으로 표시합니다.
var shopUi = Content.Load<GameObject>("UI/Shop", ContentLife.App);
```

해제 API는 없습니다. 애셋은 로드한 시점의 활성 씬에 귀속되고, 그 씬이 언로드되면 해제됩니다. `this.LoadContent<T>(path)`를 쓰면 호출한 컴포넌트의 씬에 귀속됩니다.

## Delivery

폴더를 선택하면 인스펙터 상단에 `Delivery` 선택이 나옵니다.

- `Built-in`: 앱에 포함됩니다. 동기 로드가 항상 됩니다.
- `Download`: 원격에서 받습니다. 받기 전에 동기 `Load`를 부르면 개발 빌드에서는 `ContentNotDownloadedException`이 나고, 릴리스 빌드에서는 경고와 함께 null이 반환됩니다.

하위 폴더는 상위 설정을 물려받습니다. 설정은 폴더 GUID 기준으로 `Assets/ContentsSettings.asset`에 저장되므로 폴더를 옮기거나 이름을 바꿔도 유지됩니다.

## 빌드

- 메뉴 `Tools > Contents Loader > Build Content`, 또는 `-executeMethod H.ContentsLoader.Editor.ContentsBuilder.BuildFromCommandLine`
- Build Settings 창에서 플레이어를 빌드하면 콘텐츠를 먼저 빌드하고 `StreamingAssets`에 포함합니다(`BuildWithPlayer`).
- 스크립트로 빌드할 때는 `BuildPipeline.BuildPlayer` 대신 `ContentsBuilder.BuildPlayer(options)`를 씁니다. Unity는 플레이어 빌드 콜백 안에서 번들을 빌드하면 크래시하므로 콘텐츠를 먼저 빌드해야 합니다.
- 출력은 프로젝트 루트의 `ContentsBuild/<BuildTarget>/`입니다.
  - `BuiltIn/`: 앱에 들어가는 LZ4 팩과 카탈로그
  - `Remote/`: 서버에 올릴 LZMA 팩 전체와 카탈로그. `RemoteUrl`에 올립니다.
  - `layout.json`: 직전 팩 배치입니다. 패치 크기를 줄이려면 버전 관리에 넣습니다.
  - `report.txt`: 팩별 구성과 경고입니다.

## 설정 (`ContentsSettings`)

| 항목 | 설명 |
|---|---|
| `RemoteUrl` | Download 팩을 받을 주소입니다. `{Platform}`은 빌드 타깃 이름으로 바뀝니다. |
| `MemoryBudgetMB` | 넘으면 씬 전환 중 대기 상태(Cached) 애셋부터 오래된 순으로 해제합니다. 0은 제한 없음입니다. |
| `SmallPackThresholdKB` | 이보다 작은 팩은 상위 팩에 합칩니다. 기본 64KB입니다. |
| `SignatureByFolder` | 공유 애셋 분류를 애셋이 아닌 폴더 단위로 합니다. 폴더에 애셋을 추가해도 다른 팩이 안 바뀝니다. |
| `PlayModeSource` | `AssetDatabase`는 빌드 없이 바로 읽습니다. `Bundles`는 마지막 빌드 결과에서 읽고, 다운로드는 로컬 `Remote/` 폴더에서 받습니다. |
| `GeneratePathConstants` | 빌드할 때 `ContentPaths.Towers.Cannon` 같은 상수를 생성합니다. |

## 다운로드

- 중단된 다운로드는 다음 요청에서 받은 지점부터 이어 받습니다. 앱을 껐다 켜도 같습니다. 서버는 HTTP Range 요청을 지원해야 하고, 지원하지 않으면 처음부터 다시 받습니다.
- Android에서는 OS의 `DownloadManager`가 받습니다. 앱이 백그라운드로 가거나 꺼져도 계속 받고, 앱을 다시 켜면 그 작업을 이어서 기다립니다. 받는 동안 시스템 알림이 표시됩니다.
- Android에서 `DownloadManager`를 쓰려면 원격 주소가 HTTPS여야 합니다. Unity의 HTTP 허용 설정은 OS 서비스에 적용되지 않기 때문입니다. HTTP 주소는 경고를 남기고 앱 안에서 받습니다.
- `GetDownloadSize`는 이미 받은 부분을 뺀 남은 크기를 반환합니다.

## 패치

새 빌드의 `Remote/`를 서버에 올리고 앱에서 `await Content.UpdateCatalog()`를 부릅니다. 원격 카탈로그가 더 새로우면 적용됩니다. 팩 파일명이 내용 해시라서 바뀐 팩만 받습니다.

`UpdateCatalog`는 콘텐츠를 로드하기 전, 앱 시작 직후에 부릅니다. 이미 열린 팩과 같은 팩의 새 버전은 Unity가 동시에 열 수 없습니다.

## 현재 제약

- 씬(`.unity`)은 아직 `Contents` 대상이 아닙니다.
- iOS와 데스크톱은 앱이 실행 중일 때만 받습니다. 중단되면 다음 실행에서 이어 받습니다.
- 의존성은 파일 단위로 수집합니다.
- AssetBundle은 번들 단위로 언로드됩니다. 같은 팩의 다른 애셋이 쓰이는 동안은 메모리가 해제되지 않습니다.
- `Assets/` 아래의 `Contents` 폴더만 대상입니다.
- 스크립트로 플레이어를 빌드할 때는 `ContentsBuilder.BuildPlayer`를 써야 콘텐츠가 함께 빌드됩니다.

## 기기 검증

저장소 루트의 `run-device-check.ps1`은 연결된 Android 기기에 검증 앱을 설치합니다. 그다음 백그라운드 다운로드와 앱 종료 후 재개를 확인합니다. PC의 로컬 서버(`tools/range_server.py`)는 `adb reverse`로 연결합니다.
