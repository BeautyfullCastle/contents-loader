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

`ContentsSettings.DefaultDelivery`가 프로젝트 전체 기본값입니다. 대부분은 이것 하나만 정하면 됩니다.

- `Built-in`: 앱에 포함됩니다. 동기 로드가 항상 됩니다.
- `Download`: 원격에서 받습니다. 받기 전에 동기 `Load`를 부르면 개발 빌드에서는 `ContentNotDownloadedException`이 나고, 릴리스 빌드에서는 경고와 함께 null이 반환됩니다. 시작할 때 `await Content.Download("")`로 전부 받아 두는 흐름을 권장합니다.

일부 폴더만 다르게 하려면(예: 대부분 Built-in인데 시즌 DLC만 Download) 폴더를 선택하고 인스펙터 상단의 `Delivery`를 바꿉니다. 하위 폴더는 상위 설정을 물려받고, 규칙이 없으면 프로젝트 기본값을 씁니다. 설정은 폴더 GUID 기준으로 `Assets/ContentsSettings.asset`에 저장되므로 폴더를 옮기거나 이름을 바꿔도 유지됩니다.

Built-in과 Download가 같이 쓰는 애셋은 항상 Built-in에 들어갑니다. Download 콘텐츠를 받지 않아도 앱이 동작해야 하기 때문입니다.

### Delivery를 바꿔서 배포할 때

팩 이름은 내용으로 정해지고 Delivery와 무관합니다. 그래서 Delivery만 바꾼 패치는 다시 받지 않습니다.

- Built-in → Download: 이미 설치된 앱은 앱 안의 같은 팩을 그대로 읽습니다. 새 빌드의 앱에는 안 들어가므로 빌드가 경고합니다. 그 경로를 동기 `Load`로 부르는 코드가 있으면 `Download`나 `LoadAsync` 뒤로 옮겨야 합니다.
- Download → Built-in: 이미 받은 사용자는 받은 팩을 그대로 씁니다. 새 빌드의 앱에는 들어갑니다.

받은 팩은 `persistentDataPath`에 저장됩니다. 앱 업데이트 때는 남고 앱 삭제 때 같이 지워집니다. 카탈로그가 더는 쓰지 않는 팩은 앱 시작과 `UpdateCatalog` 때 지웁니다. iOS에서는 iCloud 백업에서 제외합니다.

## 빌드

- 메뉴 `Tools > Contents Loader > Build Content`, 또는 `-executeMethod H.ContentsLoader.Editor.ContentsBuilder.BuildFromCommandLine`
- Build Settings 창에서 플레이어를 빌드하면 콘텐츠를 먼저 빌드하고 `StreamingAssets`에 포함합니다(`BuildWithPlayer`).
- 스크립트로 빌드할 때는 `BuildPipeline.BuildPlayer` 대신 `ContentsBuilder.BuildPlayer(options)`를 씁니다. Unity는 플레이어 빌드 콜백 안에서 번들을 빌드하면 크래시하므로 콘텐츠를 먼저 빌드해야 합니다.
- 출력은 프로젝트 루트의 `ContentsBuild/<BuildTarget>/`입니다.
  - `BuiltIn/`: 앱에 들어가는 LZ4 팩과 카탈로그
  - `Remote/`: 서버에 올릴 LZMA 팩 전체와 카탈로그. `RemoteUrl`에 올립니다.
  - `layout.json`: 직전 팩 배치입니다. 패치 크기를 줄이려면 버전 관리에 넣습니다.
  - `report.txt`: 팩별 구성과 경고입니다.
- `Bundles` 플레이 모드는 에디터 플랫폼(Windows면 `StandaloneWindows64`) 빌드를 읽습니다. 활성 빌드 타깃이 Android 같은 모바일이면 그 번들의 셰이더가 에디터에서 깨져 보이기 때문입니다. `Tools > Contents Loader > Build Content for Play Mode`로 만듭니다. 에디터 플랫폼 빌드가 없으면 경고와 함께 활성 타깃 빌드를 읽습니다.
- `Tools > Contents Loader > Clear Play Mode Download Cache`는 플레이 모드에서 받은 Download 팩을 지웁니다. 다운로드를 처음부터 다시 볼 때 씁니다.
- 스크립트에서는 `ContentsBuilder.BuildContent(target)`, `ContentsBuilder.BuildForPlayMode()`, `ContentsSettings.SetDelivery(folderPath, delivery)`를 씁니다.

## 설정 (`ContentsSettings`)

| 항목 | 설명 |
|---|---|
| `DefaultDelivery` | 폴더 규칙이 없는 모든 `Contents` 폴더의 Delivery입니다. 앱을 작게 하고 전부 받게 하려면 `Download`로 둡니다. 폴더 인스펙터의 Delivery는 이 값을 덮어씁니다. |
| `RemoteUrl` | Download 팩을 받을 주소입니다. `{Platform}`은 빌드 타깃 이름으로 바뀝니다. |
| `MemoryBudgetMB` | 메모리 사용량이 넘으면 대기 상태(Cached) 애셋부터 오래된 순으로 해제합니다. 사용량은 열린 팩 파일(LZ4) 크기의 합이고, 여러 애셋이 같이 쓰는 팩은 한 번만 셉니다. 에디터 AssetDatabase 모드에서는 애셋과 의존 오브젝트의 런타임 크기 합으로 추정합니다. 0은 제한 없음입니다. |
| `SmallPackThresholdKB` | 이보다 작은 팩은 상위 팩에 합칩니다. 기본 64KB입니다. |
| `SignatureByFolder` | 공유 애셋 분류를 애셋이 아닌 폴더 단위로 합니다. 폴더에 애셋을 추가해도 다른 팩이 안 바뀝니다. |
| `PlayModeSource` | `AssetDatabase`는 빌드 없이 바로 읽습니다. `Bundles`는 마지막 빌드 결과에서 읽고, 다운로드는 로컬 `Remote/` 폴더에서 받습니다. |
| `EditorDownloadSource` | `Bundles` 플레이 모드에서 Download 팩을 받는 곳입니다. `LocalFolder`는 로컬 `Remote/` 폴더에서 바로 복사합니다. `LocalServer`는 그 폴더를 에디터가 localhost HTTP로 내보내서 실제 다운로드 경로(UnityWebRequest, 이어받기)를 그대로 탑니다. `RemoteUrl`은 실제 서버에서 받습니다. 받는 곳마다 캐시를 따로 씁니다. |
| `EditorDownloadSpeedKBps` | `LocalServer`의 전송 속도 제한입니다. 모바일 네트워크처럼 느리게 해서 로딩 화면을 확인할 때 씁니다. 0은 제한 없음입니다. |
| `RepackWastePercent` | 이전 배치를 재사용하는 팩에서, 그 팩을 여는 루트가 안 쓰는 바이트 비율이 이 값을 넘으면 팩을 다시 나눕니다. 낮을수록 메모리가 정확하고, 높을수록 패치가 작습니다. 기본 25입니다. |
| `GeneratePathConstants` | 빌드할 때 `ContentPaths.Towers.Cannon` 같은 상수를 생성합니다. |

## Contents Loader 창

`Tools > Contents Loader > Monitor` 또는 `Settings`. 플레이 중 0.25초마다 갱신됩니다.

- **Assets 탭**: 지금 잡혀 있는 콘텐츠 목록입니다. 상태(Loading / Loaded / Cached), 경로, 타입, 소유자(씬 이름 또는 App), 크기, 마지막 사용 후 지난 프레임을 보여줍니다. 상태 필터와 검색, 열 클릭 정렬을 지원하고, 더블클릭하면 프로젝트에서 원본 애셋을 찾아 줍니다.
- **Packs 탭**: Bundles 모드에서 카탈로그의 모든 팩입니다. Delivery, 기기에 있는지(Built-in / Downloaded / Not downloaded), 카탈로그 크기, 열려 있을 때 메모리 크기, 참조 수(Refs, 0이면 닫힘)를 보여주고, 펼치면 팩에 든 콘텐츠가 나옵니다.
- **Settings 탭**: `ContentsSettings`를 바로 편집합니다. 폴더 규칙을 경로로 보여주고 바꾸거나 지울 수 있고, 콘텐츠 빌드, 빌드 폴더와 리포트 열기, 플레이 모드 다운로드 캐시 지우기도 여기서 합니다. 플레이 모드가 아니어도 됩니다.
- 위쪽 막대는 메모리 예산 대비 사용량이고, `Unload Cached`는 씬 전환을 기다리지 않고 대기 상태 애셋을 바로 해제합니다.

상태는 `Loading → Loaded ⇄ Cached → 해제` 순서로 바뀝니다. 소유 씬이 하나라도 남아 있으면 Loaded, 소유 씬이 모두 내려가면 Cached가 되고, 다음 프레임에 다시 쓰이지 않으면 해제됩니다.

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
- 의존성은 파일 단위로 수집합니다. 한 팩에서만 쓰는 의존 파일은 배정하지 않아서 Unity가 참조된 오브젝트만 넣습니다(리포트에 `referenced objects only`로 표시). 여러 팩이 같이 쓰는 파일은 복제를 막으려고 파일 전체를 공유 팩에 넣습니다. 예를 들어 FBX 하나의 메시들을 서로 다른 프리팹이 나눠 쓰면 FBX 전체가 공유 팩에 들어갑니다.
- AssetBundle은 번들 단위로 언로드됩니다. 같은 팩의 다른 애셋이 쓰이는 동안은 메모리가 해제되지 않습니다.
- `Assets/` 아래의 `Contents` 폴더만 대상입니다.
- 스크립트로 플레이어를 빌드할 때는 `ContentsBuilder.BuildPlayer`를 써야 콘텐츠가 함께 빌드됩니다.

## 기기 검증

저장소 루트의 `run-device-check.ps1`은 연결된 Android 기기에 검증 앱을 설치합니다. 그다음 백그라운드 다운로드와 앱 종료 후 재개를 확인합니다. PC의 로컬 서버(`tools/range_server.py`)는 `adb reverse`로 연결합니다.
