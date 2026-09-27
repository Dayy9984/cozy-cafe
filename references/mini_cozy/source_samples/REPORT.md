# COZYBUNKAI (Mini Cozy Room – Demo) 분해 분석 리포트

> 원본 경로: `C:\Users\minda\OneDrive\바탕 화면\COZYBUNKAI`
> 분석일: 2026-09-23 / 자동 분석(UnityPy + ILSpy)

---

## 1. 한눈에 보기

| 항목 | 값 |
|---|---|
| 제품명 | **MiniCozyRoom - Demo** (`app.info`) |
| 제작사 | **TesseractStudio** (인게임 크레딧: *Tesseract & Nugem Studio*) |
| 버전 | **1.04.00 demo** (`Version.txt`) |
| 엔진 | **Unity 6000.0.51f1 (Unity 6)** · Mono 스크립팅 백엔드 (`MonoBleedingEdge`) |
| 렌더 파이프라인 | **URP** (Universal Render Pipeline, 2D Renderer) |
| 그래픽 API | D3D12 / D3D11 (`D3D12\D3D12Core.dll`, `boot.config`) |
| 빌드 GUID | `8ed2669d265b40fcb000c606bf48a08d` |
| 빌드 타입 | Windows 64-bit Standalone (디버그 심볼 포함 Burst 정보 폴더 존재) |

**장르(코드·에셋으로 확인):** Lo-Fi 감성 데스크톱 힐링/집중 앱 게임
(미니 룸 꾸미기 + 음악 플레이어 + 타이머/투두/메모 + 펫 + 아바타 꾸미기 + 임베디드 웹 브라우저)

---

## 2. 연결된 기술 / 외부 서비스 (의존성)

`ScriptingAssemblies.json`, `RuntimeInitializeOnLoads.json`, `Managed\*.dll` 기준.

| 구분 | 항목 | 용도 |
|---|---|---|
| 플랫폼 | **Steamworks** (`steam_api64.dll`, `Heathen.Steamworks`, `com.rlabrecque.steamworks.net`) | 스팀 초기화·업적·클라우드 세이브·DLC·오버레이 |
| 웹브라우저 | **Vuplex WebView** (Chromium 내장, `libcef.dll` 238MB) | YouTube/Spotify 인앱 재생(임베디드 브라우저) |
| 세이브 | **Easy Save 3 (ES3)** — `Assembly-CSharp-firstpass` | 로컬/클라우드 세이브, 암호화 |
| 데이터 | **Odin Inspector / Sirenix Serialization** | 인스펙터·직렬화 |
| 비동기 | **UniTask (Cysharp)** + UniTask.{Addressables,DOTween,Linq,TextMeshPro} | async/await |
| 트윈 | **DOTween / DOTweenPro** | UI·오브젝트 애니메이션 |
| 리소스 | **Unity Addressables 2.5.0** + ResourceManager + ScriptableBuildPipeline | 에셋 번들 로딩 |
| 다국어 | **Unity Localization** (+ Addressables provider) | 11개 언어 |
| 텍스트 | TextMeshPro | 폰트 SDF |
| 오디오 | NAudio / NAudio.Flac | **사용자 로컬 음악(MP3/FLAC) 로딩** |
| 입력 | Unity Input System | 키보드/마우스 |
| 유틸 | CsvHelper, Newtonsoft.Json, Ookii.Dialogs(파일 대화상자), Interop.IWshRuntimeLibrary(바로가기), usm, VInspector, DemiLib, CodeMonkey, MTAssets.IngameLogsViewer | – |

> 에디터 코드가 아닌 **런타임 빌드**이며, `SingularityGroup.HotReload` 런타임 DLL이 포함되어 있습니다(빌드에 실수 포함 추정).

---

## 3. 기능 목록 (코드 클래스 기준)

`Assembly-CSharp`의 매니저/시스템 클래스로 확인된 실제 기능입니다.

### 3.1 코어/매니저 (`namespace cozyhouse`, `TS`)
- `GameManager` (싱글턴) — 게임 모드(`EGameMode`), 로케일, CSV 데이터 로드, 전체 초기화
- `UIManager` — Canvas_Main / Canvas_Side / Canvas_Minimode / Canvas_Common 및 각종 팝업 패널 관리
- `DataManager` — UI 스킨/아이콘/배경색/앨범순서 ScriptableObject 데이터, 비밀번호(암호화)
- `Player`, `PlayerSkinHandler` — 유저 데이터, 아바타(캐릭터) 외형
- `WindowManager` / `TS.Window` — 창 제어(최소화, 트레이, 항상 위)
- `TrayManager` + `ITrayModule`, `EmbeddedTrayModule` — **시스템 트레이 아이콘 + 컨텍스트 메뉴**
- `PopupManager`, `ToastManager`, `TooltipManager`, `RedDotManager` — 공통 UI
- `LogManager`, `LogsCatcher`, `FpsMonitor`, `TimeMonitor`, `GcMonitor` — QA/성능

### 3.2 음악 / Lo-Fi 플레이어
- `MusicPlayer` (`cozyhouse.musicplayer`) — 앨범/트랙 재생, 랜덤·반복
- `AlbumOrderData`, `LocalMusicSlot`, `UI_LocalAlbum`, `UI_Album` — 앨범 목록, **로컬 파일 추가**
- `NAudio` 연동 → 로컬 MP3/FLAC 로드 (`Error reading MP3 {0}` 문자열)
- **`EmbeddedBrowser` / `WebViewUI` / `SpotifyBrowserFix` / `CanvasWebViewPrefab`** — Vuplex 임베디드 브라우저로 YouTube/YouTube Music/Spotify 재생
- `VisualEqualizer` — 재생 시각화(이퀄라이저)
- `AudioManager`, `AudioSettings`, `SoundCategory`, `FadingAudioSource`, `WeightedAudioClip`, `AudibleClipList`

### 3.3 앰비언스(환경음)
- `AmbiencePlayer` (싱글턴 아님), `AmbienceItem`, `AmbienceTrayModule`, `AmbiencePresetData`, `AmbienceBgListData`
- 프리셋 예: 벽난로·비·천둥·바람·새소리·파도·화이트/핑크/브라운 노이즈·기차·도시 등

### 3.4 타이머 / 집중 도구
- `TimerManager` + `TimerSystem` + `SessionTimerSystem` + `StopwatchSystem`
- `TimerUIManager`, `UI_Timer`, `UI_Stopwatch`, `UI_SessionTimer`, `TimerTabType`, `TimerColorData`, `TimerColorData`
- 알람 30종(`Timer_Alarm_1..30`), `FloatingAlarmManager`, `FloatingAlarm_{Box,Buy,LP}` (플로팅 알림 창)
- `TutorialTimer*`, `Tutorial*` 시리즈 — 튜토리얼

### 3.5 투두 / 메모
- `TodoListManager`, `TodolistSlot`, `TodoListManager`, `UI_TodoList` (그룹/리스트, 최대 개수 제한)
- `MemoManager`, `UI_Memo`, `UI_Note`, `UI_NoteSlot`, `UserNoteData`, `MemoInputField`, `UndoRedoInputField` (리치 텍스트 에디터: 굵게/기울임/밑줄/취소선/크기)

### 3.6 룸 꾸미기 / 활동
- `DecoInfo`, `DecoSkinSlot`, `DecoCategoryTabButton`, `DecoTypeTabButton`, `TurntableDeco`, `UI_Deco`
- `ActivitySelectableSlot`, `ActivityCategoryTabButton`, `UI_Activity` — 캐릭터 활동(책읽기/게임/차마시기 등)
- 카테고리: Furniture/Activity/Function/Props/General 및 Floor·Carpet·Wall·Ceiling·Frame·Landscape 등
- 배치/정렬: `DragHandler`, `SlotDragHandler`, `DragOrderSystem`, `UIGridPositioner`, `UIBringToFront`
- `UndoRedo`(UndoRedoInputField), `World`/`RoomInfo`/`LockedRoomUI` — 방 잠금/해금, 레벨
- `DeliveryBox` — 택배 상자 보상(레벨업 시)
- **터릿테이블(TurnTable/`TurnTableManager`)** — LP(Lofi Point) 수급 미니요소

### 3.7 캐릭터 / 펫 / 스킨
- `Character`, `CharacterUI`, `CharacterUI_MiniMode`, `PetController`, `Pet`, `UI_Pet`, `PetSelectableSlot`, `PetReaction`(애니메이션)
- `PlayerSkinHandler`, `PcSkinSlot`, `PcSkinColorSlot`, `PcSkinCategoryTabButton`, `UiSkinListData`, `SkinColorListData` — 스킨/색상
- `UI_Wardrobe`(옷장), `UI_FirstSetting`, `PcSkinColorSlotFirstSetting` — 최초 설정

### 3.8 미니모드 / 데스크톱 통합
- `UI_Minimode`, `Canvas_Minimode`, `CharacterUI_MiniMode`, `PopupMinimization`, `MiniModeCharacterAnim`
- `WindowsAPI`, `TrayManager`, `Tray` 스프라이트 — **항상 위 위젯/트레이 상주형 데스크톱 모드**
- `PassScrollToParent`, `ReversedScrollRect`, `ScrollViewClickHandler`, `OnHoverCursor`, `CursorManager`, `CursorStack`, `CursorTexturesSO` — 커서/스크롤

### 3.9 진행/보상/스팀
- `TS.AchievementManager`, `AchievementObject` — **Steam 업적** (LEVEL_UP_n, COMPLETE_TODO_n, COMPLETE_TIMER_n …)
- `cozyhouse.dlc.DLCManager`, `DownloadableContentObject`, `DLCList`, `UI_DLCButton`, `UI_DLCButton` — **DLC 콘텐츠**
  - 스팀 번들 URL: `store.steampowered.com/bundle/54806/Mini_Cozy_Room__All_in_One/`
- `cozyhouse.SteamCloudSaveManager` — **Steam Remote Storage 클라우드 세이브** (`cozyhouse_save.dat`)
- `InitializeSteamworks`, `SteamSettings` — 스팀 초기화

### 3.10 국제화
- `LocalizationSettings`, `LanguageManager`, `LocalizeStringEvent`, `StringTable`, `SharedTableData`, `Locale`, `GameObjectLocalizer`
- **11개 언어**: en-US, ko-KR, ja, zh-Hans, zh-Hant, fr-FR, de, it-IT, es, pt-BR, ru

### 3.11 세이브 (ES3, firstpass)
- `ES3`, `ES3File`, `ES3Cloud`, `ES3AutoSave(Mgr)`, `ES3Serializable`, `ES3SlotManager` 등 — 로컬 슬롯 + 클라우드
- `PlayerPrefs` 삭제 유틸 `DeletePlayerPrefs`

---

## 4. 코드 구조 (디컴파일 결과)

`_UNPACKED\code\` 에 원본 C# 소스로 복원. 클래스/인터페이스/열거형 **2,159개**.

| 어셈블리 | .cs 파일 | 클래스 | 설명 |
|---|---|---|---|
| `Assembly-CSharp` | 399 | 452 | **게임 본체 로직** |
| `Assembly-CSharp-firstpass` | 271 | 303 | DOTween + Easy Save 3 + MTAssets 로그뷰어 |
| `Heathen.Steamworks` | 240 | 328 | 스팀 통합 (에셋스토어) |
| `com.rlabrecque.steamworks.net` | 462 | 466 | Steamworks.NET 래퍼 |
| `Vuplex.WebView` | 115 | 115 | 임베디드 브라우저 |
| `Unity.Localization` | 258 | 279 | Unity 공식 다국어 |
| `Unity.ResourceManager` | 93 | 106 | Addressables 런타임 |
| `Unity.Addressables` | 39 | 55 | Addressables |
| `DemiLib` / `VInspector` / `usm` / `Vuplex.WebViewDemos` | 51 | 55 | 유틸/에디터 헬퍼 |

게임 코드 네임스페이스: `cozyhouse`(.data/.dlc/.musicplayer/.RedDot/.scriptableobjects/.tray/.unit/.util/.world[.item]) · `TS`(.Cursor/.QA/.UI/.Window) · `CodeMonkey` · `Utils` · `SFB`

> 핵심 진입점: `cozyhouse/GameManager.cs`, `cozyhouse/UIManager.cs`, `cozyhouse/DataManager.cs`

---

## 5. 에셋 인벤토리 (언팩 결과)

`_UNPACKED\assets\` 및 `data\` 아래에 개별 파일로 저장됨.

| 종류 | 개수 | 용량 | 위치 | 비고 |
|---|---|---|---|---|
| 스프라이트(UI/외형) | **13,867** | 20.1 MB | `assets\sprites\` | 헤어 5,895 · 이모지 1,853 · 모자 1,250 · UI 884 · 상의 626 · 신발 422 · 눈 410 · 몸 383 · 데코스킨 376 · 액세서리 375 … |
| 텍스처(원본) | 421 | 81.0 MB | `assets\textures\` | 아틀라스/배경/스플래시(빈 0×0 폰트 텍스처 8개는 제외) |
| 오디오 | 92 | **1,130 MB** | `assets\audio\` | BGM 트랙(Lo-Fi/Acoustic/Bossa/Cafe Jazz/Lounge/Classic 5곡씩) + 환경음(Fireplace/Rain/WhiteNoise/Keyboard…) + 타임알람 30종 + SFX |
| 폰트 | 8 | 44.7 MB | `assets\fonts\` | NotoSans{JP,SC,TC}·PerfectDOSVGA437·LegacyRuntime |
| 메시 | 13 | 0.4 MB | `assets\meshes\` | 기본 도형(Cube/Sphere/Plane/…) + `pCylinder1`, `pSphere1`, `polySurface2` |
| 애니메이션 | 63 | 12.9 MB | `assets\animations\` | AnimationClip 37 + AnimatorController 22 + SpriteAtlas 4 |
| 머티리얼 | 36 | – | `assets\materials\` | URP 2D, TMP SDF, WebView 머티리얼 |
| TextAsset | 6 | – | `assets\textassets\` | `ActivityInfo`, `PcSkinInfo` 등 CSV/데이터 |
| 스크립트 데이터(JSON) | 283 | 82.7 MB | `data\scripts\` | MonoBehaviour 필드 직렬화 덤프 (TMP_FontAsset, Image, Button, StringTable, RoomInfo …) |
| 매니저 설정(JSON) | 24 | – | `data\gamemanagers\` | Input/Tag/Graphics/Quality/AudioMixer/SpriteAtlas 등 |

### 5.1 다국어 문자열
`_UNPACKED\localization\`
- `strings_<locale>.csv` 11개 언어 (키, 번역)
- `strings_all.csv` — 언어 통합 매트릭스 (키 397개 × 11개 언어)
- 예: `Timer`, `Todolist`, `Memo`, `DecoSkin`, `Pet`, `TurnTable`, `MiniMode`, `Album`, `Ambience` 관련 UI 문자열 전부 포함

### 5.2 이미지 에셋의 성격(코드 연결)
- `Hair/Hat/Top/Bottom/Shoes/Eyes/Mouth/Body/Accessory` → `PlayerSkinHandler` / `UI_Wardrobe`(아바타 꾸미기)
- `DecoSkin*` → `DecoInfo` / `UI_Deco`(룸 꾸미기)
- `Pet*` → `PetController` / `UI_Pet`
- `MiniMode*` / `Tray*` → `UI_Minimode` / `TrayManager`
- `LP*` / `TurnTable*` → `TurnTableManager`(Lofi Point 재화)
- `Room*` / `RoomOpenEffect*` → `RoomInfo` / `LockedRoomUI`

---

## 6. 원본 파일 구조 (분해 대상)

```
COZYBUNKAI\
├─ MiniCozyRoom.exe                실행 파일 (672 KB)
├─ UnityPlayer.dll                 Unity 런타임 (33.7 MB)
├─ UnityCrashHandler64.exe
├─ Version.txt                     "1.04.00 demo"
├─ D3D12\D3D12Core.dll
├─ MonoBleedingEdge\               Mono 런타임 (mono-2.0-bdwgc.dll)
├─ MiniCozyRoom - Demo_BurstDebugInformation_DoNotShip\   Burst 디버그 심볼
└─ MiniCozyRoom_Data\
   ├─ globalgamemanagers[.assets] (+.resS)   전역 매니저/내장 리소스(셰이더 38, 텍스처 49, MonoScript 3,456)
   ├─ level0                                  메인 씬 (GameObject 2,421 / MonoBehaviour 3,254)
   ├─ resources.assets (+ .resS/.resource)    스프라이트 291·텍스처 290·오디오 92·StringTable 등
   ├─ sharedassets0.assets (+ .resS)          스프라이트 13,575 (아바타/데코 아틀라스), Font 6
   ├─ Managed\                                .NET 어셈블리 (게임/플러그인 DLL) ← 디컴파일 대상
   ├─ Plugins\x86_64\
   │  ├─ steam_api64.dll
   │  ├─ VuplexWebViewWindows.dll + VuplexWebViewChromium\ (libcef.dll 등 238 MB)
   │  └─ lib_burst_generated.dll
   ├─ Resources\  (unity default resources, unity_builtin_extra)
   └─ StreamingAssets\aa\  ← Addressables
      ├─ catalog.bin / catalog.hash / settings.json
      └─ StandaloneWindows64\  *.bundle (로컬라이제이션 테이블 11개 + monoscripts)
```

---

## 7. 알려진 한계 / 미추출 항목

- `PlayerSettings` 타입트리(Unity 6) 4바이트 정렬 불일치로 JSON 미생성 → 제품명/버전은 `app.info`·`Version.txt`로 대체.
- `catalog.bin`(Addressables 카탈로그, 이진 포맷)은 표준 직렬화 파일이 아니라 주소 목록 자동 추출 불가. 단, 번들 *내용물*(로컬라이제이션)은 전부 추출됨.
- Shader 오브젝트의 소스(`m_Script`)는 빌드에서 제거되어 텍스트 추출 불가(URP/TMP 셰이더는 바이너리).
- `Font Texture` 8개는 크기 0×0(동적 폰트의 빈 아틀라스)이라 이미지 저장 불가/불필요.
- 일부 `MonoBehaviour` 305개는 참조 스크립트가 `Managed`에 없어 필드 덤프 실패(서드파티 런타임 전용).

---

## 8. 요약

- **Mini Cozy Room – Demo (v1.04.00)**, TesseractStudio 제작, **Unity 6 + URP** 기반 Windows 데모.
- 게임 본체는 **`Assembly-CSharp`**에 있으며, **Steam(업적/클라우드/DLC) + Vuplex 임베디드 브라우저(YouTube/Spotify) + Easy Save 3 + Addressables + Localization**로 구성.
- 추출물: **이미지 14,288장, 오디오 92개(1.13 GB), 폰트 8, 애니메이션 63, C# 소스 약 2,159 클래스, 다국어 11종**.
- 모든 결과물은 `_UNPACKED\` 폴더에서 직접 열람 가능.
