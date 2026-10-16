# Contents Loader — 구현 핸드오프

> 이 문서는 설계 대화에서 합의된 내용을 Claude Code 세션으로 넘기기 위한 작업 지시서다.
> 이 폴더(`contents-loader`)에서 이 문서를 읽고 그대로 구현·테스트를 진행할 것.
> 대화와 커밋 메시지는 한국어로.

---

## 0. 한 줄 요약

**"Resources처럼 쓰기 쉬운데, 내부는 알아서 압축·중복 제거·다운로드·씬 단위 언로드를 해주는" 애셋 동적 로드 시스템을 Unity 패키지(UPM)로 만든다.**

Unity의 AssetBundle / Addressables / Content Directories가 파편화되어 있는 문제를, 사용자 표면은 극도로 단순하게 두고 백엔드를 교체 가능하게 감추는 방식으로 해결한다.

---

## 1. 산출물 형태: Unity 패키지 (필수)

- **UPM 패키지로 만든다.** 프로젝트 `Assets/`에 코드를 직접 넣지 말 것.
- 패키지 이름(가칭): `com.h.contents-loader` — 표시명 `Contents Loader`
- 저장소 구조:

```
contents-loader/
├─ HANDOFF.md                         ← 이 문서
├─ Packages/
│  └─ com.h.contents-loader/          ← 실제 패키지 (임베디드)
│     ├─ package.json
│     ├─ README.md
│     ├─ CHANGELOG.md
│     ├─ Runtime/
│     │  ├─ H.ContentsLoader.asmdef
│     │  └─ ...
│     ├─ Editor/
│     │  ├─ H.ContentsLoader.Editor.asmdef   (includePlatforms: Editor)
│     │  └─ ...
│     ├─ Tests/
│     │  ├─ Editor/  H.ContentsLoader.Editor.Tests.asmdef
│     │  └─ Runtime/ H.ContentsLoader.Tests.asmdef
│     └─ Samples~/
│        └─ Basic/                     ← Package Manager에서 임포트 가능한 샘플
└─ TestProject/                        ← 패키지 개발·테스트용 Unity 프로젝트
   ├─ Packages/manifest.json           ← "com.h.contents-loader": "file:../../Packages/com.h.contents-loader"
   └─ Assets/ ...                      ← 테스트용 Contents/ 폴더, 씬, 프리팹
```

- `package.json`의 `unity` 필드는 실제 설치된 에디터 버전에 맞춘다 (아래 2번에서 확인).
- `testables`에 패키지 이름을 넣어 TestProject에서 패키지 테스트가 돌도록 한다.
- 네임스페이스: `H.ContentsLoader` (런타임), `H.ContentsLoader.Editor` (에디터).

---

## 2. 첫 단계: 환경 확인

1. 설치된 Unity 에디터 버전과 CLI 경로 확인 (Unity Hub 설치 경로, 예: `C:\Program Files\Unity\Hub\Editor\<버전>\Editor\Unity.exe`).
2. 버전에 따라 백엔드 결정:
   - **Unity 6.6 이상**: Content Directories 사용 가능. 단, 공식 Addressables 기준으로도 원격 콘텐츠/콘텐츠 업데이트는 아직 AssetBundle 권장. `Unity.Content` / `Unity.Loading` 저수준 API의 실제 시그니처를 해당 버전에서 직접 확인할 것.
   - **6.6 미만**: AssetBundle 백엔드로 시작.
   - 어느 쪽이든 **MVP는 AssetBundle 백엔드(SBP 또는 `BuildPipeline.BuildAssetBundles`)로 먼저 완성**하고, Content Directory 백엔드는 같은 인터페이스 뒤에 나중에 추가한다.
3. `TestProject`가 없으면 CLI로 생성:
   ```
   Unity.exe -batchmode -quit -createProject "<경로>\TestProject" -logFile -
   ```

---

## 3. 사용자 경험 (가장 중요 — 이게 설계의 핵심)

Resources가 계속 살아남는 이유는 **"폴더에 넣으면 끝, 문자열로 부르면 끝"** 이기 때문. 이 사용성을 그대로 가져간다.

### 3.1 폴더 규칙
- 이름이 `Contents`인 폴더는 프로젝트 어디에 있든 자동으로 대상이 된다 (Resources처럼 여러 개 허용).
- 각 `Contents/` 아래의 **상대 경로**가 로드 경로가 된다. 확장자 제외.
  - `Assets/Game/Contents/Towers/Cannon.prefab` → `"Towers/Cannon"`
- 서로 다른 `Contents/` 폴더에 같은 상대 경로가 있으면 **빌드 전 에러**.

### 3.2 체크박스 하나
- 폴더 인스펙터에 `Delivery: Built-in / Download` 토글 하나만 노출.
- 하위 폴더는 상속, 필요 시 하위에서 덮어쓰기.
- 설정은 **폴더 GUID 기준**으로 설정 애셋(ScriptableObject, 예: `ContentsSettings.asset`)에 저장 → 폴더를 옮기거나 이름을 바꿔도 유지.

### 3.3 API

```csharp
// 빌트인: Resources.Load처럼 동기 호출 가능
var cannon = Content.Load<GameObject>("Towers/Cannon");

// 다운로드: 처음 부르면 알아서 받고, 캐시에 있으면 즉시 로드
var dragon = await Content.LoadAsync<GameObject>("Bosses/Dragon");

// 선택: 로딩 화면에서 미리 받기 (진행률)
await Content.Download("Season01", progress);

// 선택: 씬을 넘어 유지해야 하는 것만 명시
var shopUi = Content.Load<GameObject>("UI/Shop", ContentLife.App);
```

- **`Release` / `Unload` API는 사용자에게 노출하지 않는다.** (디버그/테스트용 내부 API는 허용)
- 오타 방지용으로 빌드 시 폴더 구조로 상수 코드젠(`ContentPaths.Towers.Cannon`) 제공 — 선택 기능, 문자열 API가 기본.
- 에디터 플레이 모드 기본값: **빌드 없이 AssetDatabase에서 바로 로드** (Resources급 반복 속도). 설정으로 "빌드 시뮬레이션" 모드 전환 가능(실제 번들에서 로드).

### 3.4 동기 로드 제약 (명확히 처리)
- Download 애셋에 동기 `Content.Load`:
  - 이미 기기 캐시에 있으면 정상 동작
  - 없으면 에디터/개발 빌드: 명확한 예외 (`"Bosses/Dragon은 Download 애셋입니다. LoadAsync를 쓰거나 Download()로 미리 받으세요"`)
  - 릴리스 빌드: null + 경고 로그

---

## 4. 수명 관리: 씬 귀속 자동 언로드

### 4.1 소유자 결정
- 기본 소유자 = **로드 호출 시점의 씬**.
  - 호출자가 GameObject 컨텍스트를 가지면 그 오브젝트가 속한 씬 (`gameObject.scene`) — 확장 메서드 `this.LoadContent<T>(path)` 같은 형태로 제공 가능.
  - 컨텍스트가 없으면 `SceneManager.GetActiveScene()`.
  - 호출자가 `DontDestroyOnLoad` 씬에 있으면 → App 소유.
- 애디티브 씬은 각자 자기 씬에 귀속.
- `ContentLife.App`으로 명시하면 앱 수명.

### 4.2 씬 전환 시 지연 언로드 (스래싱 방지)
씬 A → B 전환에서 같은 애셋을 "언로드 → 즉시 재로드"하면 안 된다.

1. A 씬 언로드(`SceneManager.sceneUnloaded`) → A 소유 애셋을 `Cached` 상태로 강등
2. B 로드 중 같은 애셋 요청 → 소유권만 B로 이전 (로드 비용 0)
3. B 로드 완료 다음 프레임 → 아무도 회수하지 않은 `Cached` 애셋만 실제 언로드
4. 추가로 메모리 예산(설정값)을 넘으면 LRU로 `Cached`부터 축출

상태 머신: `Loading → Loaded → Cached → Unloaded` (Cached → Loaded 복귀 가능)

### 4.3 금지 사항
- GC/파이널라이저 기반 자동 해제 금지 (비결정적, 파이널라이저 스레드에서 Unity API 호출 불가).

---

## 5. 내부 처리 (사용자에게 숨김)

| 사용자 선택 | 내부 동작 |
|---|---|
| Built-in | StreamingAssets(또는 Content Directory)에 포함, **LZ4**, 동기 로드 허용 |
| Download | 전송용 **LZMA**(최대 압축) → 기기 캐시 저장 시 **LZ4로 재압축** (`AssetBundle.RecompressAssetBundleAsync` 등) |
| Built-in과 Download가 공유하는 의존성 | **자동으로 Built-in에 귀속** (다운로드 없이도 앱이 항상 동작해야 함. Built-in → Download 의존은 구조적으로 불가능하게) |
| 여러 루트가 공유하는 애셋 | **도달 시그니처 클러스터링**으로 자동 분리 (5.2) |
| 셰이더 | 시그니처와 무관하게 **항상 Built-in 전용 Shader 팩 하나** |

### 5.1 의존성 수집
- 루트 = `Contents/` 폴더 안의 애셋 (프리팹, 씬, ScriptableObject, 텍스처 등 직접 로드 대상).
- 그 외 애셋(머티리얼, 메시, 텍스처 등 `Contents/` 밖)은 **암묵적 의존성** — 경로를 받지 않는다.
- 가능하면 파일 단위(`AssetDatabase.GetDependencies`)가 아니라 **오브젝트 단위**로 수집:
  SBP의 `ContentBuildInterface.GetPlayerObjectIdentifiersInAsset` / `GetPlayerDependenciesForObjects`.
  MVP에서 파일 단위로 시작해도 되지만, 인터페이스는 오브젝트 단위로 확장 가능하게.
- 하드 참조(직렬화 필드에 직접 할당) = 의존성. 소프트 참조(경로 문자열/키) = 의존성 아님, 런타임 로드.

### 5.2 자동 중복 제거: 도달 시그니처 클러스터링
1. 각 의존 오브젝트에 대해 "이 오브젝트에 도달하는 루트들의 집합"(시그니처)을 계산.
2. **시그니처가 같은 오브젝트끼리 하나의 팩(번들)으로 묶는다.**
   - 결과: 중복 0 / 어떤 루트를 로드해도 그 루트가 실제로 쓰는 팩만 로드.
3. 후처리:
   - **작은 팩 병합**: 임계값(기본 64KB, 설정 가능) 미만 팩은 시그니처가 상위집합인 팩으로 흡수.
   - **셰이더**: 별도 Shader 팩.
   - **Built-in/Download 경계**: Built-in 루트가 하나라도 도달하면 Built-in.
4. **패치 안정성**:
   - 시그니처 단위를 개별 루트가 아닌 **폴더(그룹) 단위**로 집계하는 옵션 (같은 폴더에 루트 추가 시 시그니처 불변).
   - **이전 빌드 배치를 사전값으로 재사용**: 직전 빌드의 `오브젝트 → 팩` 매핑을 읽고, 여전히 유효하면 유지. 새 오브젝트 / 규칙 위반 오브젝트만 재배치.
   - 팩 파일명 = 내용 해시 → 안 바뀐 팩은 클라이언트가 재다운로드 안 함.

### 5.3 중첩 프리팹 주의
- 빌드 시 중첩 프리팹은 루트 프리팹 안으로 평탄화됨 → 독립 루트이기도 한 프리팹을 중첩하면 **GameObject 계층이 중복**되며 이건 dedup 불가.
- 빌드 리포트에서 "계층 중복 N KB" 경고로 표시.

### 5.4 카탈로그
- 경로 해시 → (팩 ID, 애셋 이름, 의존 팩 목록, Delivery) 의 **작은 바이너리 카탈로그**.
- Resources처럼 시작 시 전체 인덱스를 무겁게 올리지 않을 것.
- Download 팩: 원격 URL 베이스 + 해시 + 크기. 원격 카탈로그가 로컬 카탈로그를 덮어쓰는 오버레이 구조(패치).

---

## 6. 레이어 구조 (내부)

```
L4  Game API     Content.Load / LoadAsync / Download          ← 사용자는 여기만 앎
L3  Lifetime     씬 귀속 소유권, Cached/LRU, 메모리 예산
L2  Resolve      Catalog: path → location (패치 오버레이)
L1  Storage      IContentBackend: AssetDatabase(에디터) / AssetBundle / ContentDirectory(추후)
```

```csharp
interface IContentBackend {
    // 동기/비동기 로드, 해제, 부분 언로드 지원 여부
    bool SupportsPartialUnload { get; } // AssetBundle=false, ContentDirectory=true
}
```
- 위층은 어떤 백엔드인지 모른다. 새 Unity 포맷이 나와도 L1 구현 하나 추가로 끝나게.
- AssetBundle 백엔드는 번들 단위 언로드라, 같은 번들의 다른 애셋이 살아있으면 축출 보류 + 디버그 경고.

---

## 7. 에디터 툴 (MVP 이후, 우선순위 낮음)

`Window > Contents Loader` 에디터 윈도우 (UI Toolkit):
- **Runtime 탭**: 메모리 예산 바(Loaded/Cached/Pinned), 씬별 소유 애셋 트리, 애셋 상태·크기·백엔드, 이벤트 로그(Load / 캐시 적중 / Evict).
- **Catalog 탭**: `Contents/` 폴더 트리 + Delivery 체크박스, 자동 생성 팩 목록과 각 팩의 시그니처, 이전 빌드 대비 diff(추가/삭제/재배치 사유/중복 경고), 원격 패치 크기.

---

## 8. 테스트 (반드시 CLI로 돌려서 통과시킬 것)

### 8.1 EditMode 테스트
- 폴더 스캔: 여러 `Contents/` 폴더 병합, 상대 경로 계산, 경로 충돌 시 에러.
- Delivery 상속/덮어쓰기, 폴더 이동 후 설정 유지(GUID).
- 클러스터링 알고리즘 (순수 C# 로직으로 분리해서 Unity 없이도 테스트 가능하게):
  - 공유 애셋이 별도 팩으로 분리되는지
  - 모든 오브젝트가 정확히 한 팩에만 존재(중복 0)
  - 각 루트의 필요 팩 집합 = 실제 도달 오브젝트를 포함하는 팩만
  - 작은 팩 병합, 셰이더 분리, Built-in 귀속 규칙
  - 이전 배치 재사용 시 변경 없는 팩의 ID 유지
- 빌드 결과 검증: 실제 번들 빌드 후 의존 애셋이 두 번들에 중복 포함되지 않았는지 (`AssetBundleManifest` / 빌드 리포트로 확인).

### 8.2 PlayMode 테스트
- AssetDatabase 모드: `Content.Load`로 로드 성공.
- 번들 모드(빌드 후 StreamingAssets에서): 같은 테스트 통과.
- Download 모드: 로컬 파일 서버 또는 `file://` 경로를 원격으로 가정해 다운로드 → 캐시 → 로드, 두 번째 로드 시 다운로드 없음.
- 씬 귀속:
  - 씬 A에서 로드 → 씬 B로 전환(single) → A 전용 애셋 언로드됨
  - A와 B가 공유하는 애셋은 언로드되지 않고 재로드도 없음(로드 카운트로 검증)
  - `ContentLife.App` 애셋은 유지
  - 애디티브 씬 언로드 시 그 씬 소유 애셋만 해제
- 동기 로드 제약: 미다운로드 Download 애셋 동기 로드 시 예외.

### 8.3 CLI 실행 예시
```
Unity.exe -batchmode -projectPath "<경로>\TestProject" -runTests -testPlatform EditMode -testResults "<경로>\results-edit.xml" -logFile "<경로>\log-edit.txt"
Unity.exe -batchmode -projectPath "<경로>\TestProject" -runTests -testPlatform PlayMode -testResults "<경로>\results-play.xml" -logFile "<경로>\log-play.txt"
Unity.exe -batchmode -quit -projectPath "<경로>\TestProject" -executeMethod H.ContentsLoader.Editor.ContentsBuilder.BuildFromCommandLine -logFile -
```
- `-runTests`에는 `-quit`을 붙이지 않는다.
- 테스트 결과 XML을 파싱해서 실패 건이 0이 될 때까지 수정.
- 반복 실행 편의를 위해 `run-tests.ps1` 스크립트를 저장소 루트에 둔다.

---

## 9. 진행 순서

1. 환경 확인 (Unity 버전, CLI 경로) → TestProject 생성 → 패키지 골격(`package.json`, asmdef 4개) 생성 → 빈 테스트 1개가 CLI로 도는지 확인
2. 폴더 스캔 + 설정(Delivery) + 카탈로그 모델
3. AssetDatabase 백엔드 + `Content.Load` / `LoadAsync`
4. 씬 귀속 수명 관리 (Loaded/Cached/지연 언로드/LRU)
5. 클러스터링 알고리즘 (순수 로직 + 테스트)
6. AssetBundle 빌드 파이프라인 + 번들 백엔드 + 빌드 검증 테스트
7. Download (원격 → 캐시 → LZ4 재압축) + 패치 오버레이
8. 경로 상수 코드젠
9. 에디터 윈도우
10. (Unity 6.6+이면) Content Directory 백엔드

각 단계마다 CLI로 테스트를 돌려 통과시킨 뒤 다음 단계로. 단계별로 커밋.

---

## 10. 프로젝트 맥락

- 사용자는 Unity + Photon Quantum으로 타워 디펜스 게임을 개발 중. 이 패키지는 게임 전용이 아니라 범용으로 만든다.
- Quantum 연동은 이번 범위 밖. 다만 나중에 "시뮬레이션 애셋(결정론적, AssetDB)은 별도 경로, 뷰 애셋만 이 시스템으로" 분리할 수 있게 확장 여지만 남겨둘 것.
