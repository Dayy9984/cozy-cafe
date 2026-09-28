# 개발자용 README·구현 계약
기획은 planning 5파일, 수치는 data, 아트는 art를 원본으로 쓴다. 중복 GDD를 만들지 않는다.
새 프로젝트 기본값은 Godot4 안정판/GDScript. 설치 가능한 안정판과 export template를 같은 버전으로 고정한다.
Mini Cozy는 첨부 REPORT상 Unity 기반이다. 우리 Godot 제안을 원작 엔진 확인 결과와 혼동하지 않는다.
원작 decompiled 코드/유료 플러그인을 가져와 빌드하지 않는다. 엔진 변경은 DECISIONS 기록 후 영향 확인.

## 구조
`game/` 게임 소스, `data/` 경제·규격, `art/` 제작 계약, `references/` 분석용(배포 제외).
Simulation(시간/수익/연구) / Layout(초안·검사·적용) / Character(표현) / Tools(메모·타이머) /
Platform(창·IME·절전) / Save(private) / UGC(public)를 분리한다.

## 상태·복귀
실행 시간은 monotonic, 복귀는 저장한 UTC·정산 checkpoint. 음수는0, 상한24h.
연구 완료/예약 자금 도달/직원 교체를 이벤트 경계로 나눠 정산. 정산ID·원자 저장으로 중복 지급 방지.
remaining_base_work로 연구 속도 변경을 처리. 1시간 마지막rate 곱하기로 연구 변화를 생략하지 않는다.
백업 복원/잘못된 JSON/전원중단/시간역행/버전 migration 시험. 개인자료를 공개 프리셋에서 분리.

## v0.8 논리 좌표와 시각 보정 계약
사용자 확정: top64×32 / space64×64 / visual thickness4 / table-chair offset(0,-8).
64×64는 공간/작업 기준이며 셀 피치가 아니다. 4px를 더한64×68, collider 두께4, 논리Z4는 모두 금지다.
현재 구현 기본값(사용자가 피치를 직접 지정한 것은 아님):
  sx=(x-y)*32, sy=(x+y)*16
  x=sx/32+sy/16, y=sy/16-sx/32
실수좌표에서 왕복하고 최종 카메라 합성에서만 공통 픽셀스냅을 한다. 16를 칸마다 정수화하지 않는다.
이 함수는 ground projection이며 z/thickness 인자를 받지 않는다. 외곽 side mask는 따로 그린다.
실제 래스터32행/뒷선 정렬은 첫 2×2/3×3 타일 시각검증에서 확인한다. 피치 조정 필요 시 원인과
새 기본값을 결정기록에 올리고 plan/gates/예제를 함께 버전업하며 사용자 확정 크기는 바꾸지 않는다.

p_ground=project(grid_x,grid_y)
target_offset=asset.render_offset_px  # tables/chair/stool=(0,-8), other=(0,0)
p_draw=(p_ground + target_offset) * camera_zoom + camera_origin

render_offset은 원본 아트픽셀이다. 줌2에서는8→16px. 물리 elevation 값이 아니다.
PNG/atlas source pivot의 baked_offset이 있으면 runtime_offset=target_offset-baked_offset;
최종 effective=baked+runtime=target가 되어야 한다. 기본 제작은 baked0/renderer-4, 허용된 기존 원화는
baked-4/renderer0. 검증된 metadata 없이 그림과 엔진에서 각각 -4하지 않는다.
footprint/collision/path/서비스 연결/depth_sort_key는 원래 논리 셀/기준점에서 계산한다.
가구 회전은 footprint·부착점만 돌리고 화면상 -4 방향은 돌리지 않는다. 저장 인스턴스에 시각 이동을 누적하지 않는다.
parent render correction은 자식의 seat/mount world transform에 이미 포함되므로 자식에 다시 -4하지 않는다.
픽킹/고스트/윤곽은 실제 시각 transform을 공유하되 좌석/셀 판정은 논리 좌표로 돌려 처리한다.

문은 외벽의 허용 슬롯. BFS4방향 path로 필수 앵커 연결 검사. 탁자-의자·host-parent 관계 검사.
Undo는 command 그룹, drag1회=command1개. host subtree의 move/rotate/remove는 단일 거래.
상세 packet-side 검산은 tools/contracts.py, 게임 테스트의 새 CASE 키는 gauntlet/gates.json을 따른다.

## 파츠/아틀라스
appearance={rig,preset,parts,palettes,seed}; stats 별도. 조합값만 저장, 동작별 완성 PNG 대량복제 금지.
manifest의 명시 frame rect·origin·offset·fps·loop를 읽고 균일시트라고 추측하지 않음.
모든 layer가 같은 phase/anchor를 사용. 후면에서 안경이 앞머리 위로 보이지 않게 방향별 occlusion.
palette map은 exact-match 기본, alpha와 geometry 불변. micro-jitter·frame count·중복/누락 검사.

## Mac/Windows
일반/미니/투명/always-on-top/마우스통과/회복 경로·한글IME·Ctrl/Cmd·DPI·모니터 분리/절전/오디오.
네이티브 두 OS에서 동일 scenario를 실행하고 증거. headless와 WSL로 GUI 검증 대체 금지.
완전히 클릭 통과 상태에서 복귀할 tray/단축키/항상 접근 control이 있어야 함.
Mac 서명/공증과 인증서는 외부배포 단계. 패키지에 토큰·인증서 없음.

## Acceptance
하네스는 `game/tests/gauntlet_entry.gd`를 headless로 실행하고 CASE<TAB>id<TAB>JSON value를 수집한다.
`tools/game_adapter.py`는 실제 엔진을 실행하며, 고정 기대값과 외부 gate가 비교한다.
처음에는 엔진/프로젝트/테스트가 없어 FAIL/BLOCKED가 정상. 테스트 출력만 위조하는 구현 금지.
각 task의 test cases는 gauntlet/gates.json에 있으며 실제 엔진 모듈을 통과한 값이어야 한다.
마지막 native 시험은 실제 Mac/Windows 빌드·영상·로그 및 별도 사용자/검토자 확인을 요구한다.

## 실제 이미지 증거
art-pipeline은 art/approved/qa_contact.png와 sprite-sheet-alpha.png를 commit해 critic이 실제 픽셀을 읽는다. UI/통합은 game의 --capture-stage <stage> --capture-output <absolute.png>를 처리하고 실제 viewport를 저장 후 종료한다. tools/capture_game.py가 이 경로를 직접 실행한다. headless 수학 시험으로 GUI 캡처를 꾸미지 않는다.

## v0.8.1 엔진 — Unity (사용자 지시가 Godot 기본값을 대체)

게임은 `game/` 아래 Unity 프로젝트로 만든다(Unity 6000.6.3f1 기준 — 원작과 동일
Unity 6 계열이며 호스트 실제 설치본과 동일 버전; 6000.0.51f1 기본값에서 상향,
사유는 DECISIONS.md 2026-09-28 항목 참조).
`game/ProjectSettings/ProjectVersion.txt`와 `game/Assets/`가 있어야 하며,
`Packages/manifest.json`에 필요 모듈만 둔다. URP/HDRP 미사용 — Built-in 2D.

### 하네스 계약 (gates 값 불변)
- `tools/game_adapter.py <stage>` → Unity `-batchmode -projectPath <staged copy of game>` -executeMethod CozyCafe.Editor.GauntletEntry.Run -quit`. 어댑터는 추적 game/을 임시 사본으로 복사해 에디터를 실행한다 — Unity는 프로젝트를 열 때 추적 파일을 in-place로 재작성하므로 평가 대상 트리는 byte-identical을 유지해야 한다.
- 진입점은 `static void Run()`으로, `GAUNTLET_STAGE` env로 스테이지 id를 읽고
  `GAUNTLET_RESULTS`(절대경로)에 `CASE<TAB>key<TAB>json` 라인을 쓴다.
  실패 시 `EditorApplication.Exit(2)`. CASE 값은 반드시 실제 게임 모듈 호출 결과.
- 캡처 게이트(ui-local-ugc, integration)는 `CozyCafe.Editor.CaptureShot.Run`을 호출한다.
  env `GAUNTLET_CAPTURE_STAGE`, `GAUNTLET_CAPTURE_OUTPUT`(절대경로 PNG).
  실제 카메라→RenderTexture→ReadPixels로 viewport를 렌더해 PNG로 저장하고 종료.
  headless 수학으로 캡처를 꾸미지 않는다.

### Unity 배치 주의
- 배치모드에서도 `-nographics`를 쓰지 않는다(렌더가 필요한 캡처 게이트 때문).
- 에디터 호스트(capture/case)는 항상 stage_unity_project()가 만든 임시 사본을 -projectPath로 연다. 추적 트리를 직접 열지 않는다. env로 넘기는 경로는 네이티브 형태여야 한다(MSYS는 env 값을 경로 변환하지 않음).
- 첫 실행은 Library 임포트+컴파일로 수 분 걸릴 수 있다 — 어댑터는 30분 타임아웃.
- 한글 IME·투명·항상위·DPI·복귀는 네이티브 빌드 검증(native-release)에서 다룬다.
- Windows 빌드는 macOS 에디터의 Windows Standalone 모듈로 cross-build 후 Windows에서 실행 검증.


## Engine direction v0.8.2 — dual-host C# core (Unity + .NET CLI)

Unity stays the ship engine (v0.8.1 decision). To keep every gate verifiable on
machines without a Unity license, the game is structured as ONE shared core
compiled by two hosts:

- `game/Assets/CozyCafe/Core/` — pure C# game logic. **No `UnityEngine`
  references allowed.** Grid math, layout rules, economy, research, staff,
  save/offline, rig descriptors, tool logic live here. Target .NET Standard 2.1
  APIs only so the same files compile under Unity 6 and .NET 8.
- `game/GameCli/` — .NET 8 console host (`GameCli.csproj`) that includes the
  core sources via `<Compile Include="../Assets/CozyCafe/Core/**/*.cs"/>` and
  implements the stage→CASE dispatcher. Invocation contract:
  `dotnet run -c Release --project game/GameCli -- case <stage>` prints
  `CASE\t<key>\t<json>` lines (one per case, values computed by real core code)
  and exits 0; nonzero on internal error.
- `game/Assets/CozyCafe/Unity/` — UnityEngine-dependent layer (MonoBehaviours,
  scenes, rendering, uGUI). `CozyCafe.Editor.GauntletEntry.Run` remains an
  accepted CASE host for machines with a licensed editor.

`tools/game_adapter.py` tries GameCli first, then the Unity host, else fails
NOT_IMPLEMENTED/BLOCKED. Never emit CASE text from Python — values must come
from the compiled game core.
