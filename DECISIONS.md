# 결정 기록
USER_FIXED: 단순방치형·머신5·재료연구·랜덤인물·도감/단골/의뢰X·윗면64×32·공간64×64·시각두께4·탁자의자-8px·Mac/Windows·UI/UX제작·sprite-gen 참고.
MVP_DEFAULT: Godot·8메뉴·5연구·1body/3프리셋/4hair/3옷/2안경·8×8룸·가격/시간·직원상한.
REFERENCE_FACT: 첨부 실행기는 Devin Gauntlet Runner. 단순 prompt skill 아님.
UNVERIFIED: 실제 OAuth 생성/정확한 호출 모델/네이티브 빌드/아트 가독성/출시 페이스.
다른 기획으로 새로 시작하지 않는다. 변경 시 ID·이유·전후값·시뮬레이션·승인 여부를 추가한다.

## 2026-09-27 v0.8 — 사용자 정정 적용
USER_FIXED: 두께는 시각 표현만, 논리·물리 높이 없음. 탁자/의자는 기준점에서 화면 위8px.
SUPERSEDED: assistant가 제안했던 두께 추가 캔버스64×68, 바닥/가구 일괄 bottom-center 정렬,
            실제 z로 해석하는 방식은 폐기한다. old top64×32도 활성 계약에서 제거한다.
ENGINE_DEFAULT_NOT_USER_FIXED: 윗면 연속 영역 반폭32·반높이16, 바닥 작업 캔버스64×64,
            source top-origin(0,0)와 명시 top-center pivot, 스툴을 의자류로 처리.
이 기본값의 뒷선/이음은 실물 아트 검토 대상이며 실제 원작의 좌표 규칙을 확인했다고 주장하지 않는다.
SCOPE_UNCHANGED: 경제·메뉴·연구·머신5·인물 파츠·UI·공급자·감독 프로그램은 확장하지 않음.

## 2026-09-27 v0.8.1 — 엔진 결정: Unity (사용자 지시)

USER_FIXED: 게임 엔진은 **Unity**다. 사용자가 이번 세션 지시로 명시했으며
("게임은 유니티로 만들어야 한다", 2026-09-27) 우선순위 규칙상 최신 사용자 확정이
문서 기본값을 대체한다. 원작 Mini Cozy도 Unity 6000.0.51f1 기반이었다(REPORT 확인).

SUPERSEDED: DEVELOPMENT.md의 "새 프로젝트 기본값은 Godot4"는 MVP 제안값으로 폐기된다.
`tools/game_adapter.py`는 Unity 프로젝트(game/ProjectSettings + Editor/GauntletEntry.cs)를
호출하도록 재작성되었고, packet 수준 `tools/capture_game.py`에는 Unity 디스패치를 추가했다
(원본 Godot 경로 보존 — MANIFEST에서 이 1개 파일이 변경 표시된다. gates.json·plan.json·
기대값·CASE 계약은 전부 불변).

UNCHANGED: CASE<TAB>key<TAB>JSON 계약, gates.json 전체 기대값, 측정 모드, 비주얼 증거 요구.
UNITY_BIN 환경변수 또는 표준 Hub 경로에서 에디터를 찾는다.

## v0.8.2 — 2026-09-27 — CASE contract host: .NET 8 GameCli (dual-host with Unity)

Decision: the gate CASE contract is hosted by a .NET 8 console app
(`game/GameCli`) that compiles the same pure-C# core sources Unity compiles.
The Unity `-executeMethod` host remains supported when a licensed editor is
present, and remains required for native player builds and captures.

Reason: Unity editor licensing was not yet installed on the build host; gates
must exercise real game code, not fabricated JSON, and must not sit blocked on
a license. A UnityEngine-free core + CLI host satisfies both: identical logic
in both hosts, gates run anywhere `dotnet` exists.

Constraints kept: no gate expectations changed; no CASE strings emitted from
Python; Unity remains the ship engine; `game/GameCli` may never reference
UnityEngine APIs the core lacks.

Record: DEVELOPMENT.md v0.8.2 section; adapter dispatch in
`tools/game_adapter.py`.

## SPEC_CHANGE 2026-09-28 — 공간 규격 64×62→64×64 / 윗면 64×31→64×32
사유: 사용자 지시(2.5D 아이소 매트릭스: 윗면 중심이 캔버스 상단 16px, 하단 영역 48px의 가상 박스 64×64 모델).
전: top 64×31 / space·canvas 64×62 / pitch 32·15.5 / 두께 금지 조합 64×66.
후: top 64×32 / space·canvas 64×64 / pitch 32·16 / 두께 금지 조합 64×68.
유지: visual_thickness 4px(물리0), 탁자·의자 (0,-8) 한 번, 캐릭터 셀/앵커.
전파: art_contract.json(데이터 원본)·gates.json·문서를 갱신하고 코드 상수는 새 건틀릿 런의 빌더가 게이트에 맞춰 변경한다.

## SPEC_CHANGE 2026-09-28 — Unity 에디터 버전 6000.0.51f1→6000.6.3f1 (호스트 설치본 일치)
사유: 빌드 호스트에 설치된 유일한 에디터가 6000.6.3f1이다. 다른 버전으로 프로젝트를
여는 실행(capture/case host)이 Packages·ProjectSettings·ProjectVersion.txt를 매번
덮어써 추적 소스를 변경했다(독립 critic 지적: "capture/check 명령이 추적 소스를 변경").
전: m_EditorVersion 6000.0.51f1 — 패키지 기본값(원작과 동일 계열), 호스트 미설치.
후: m_EditorVersion 6000.6.3f1 — 실제 설치본과 동일. 에디터가 재생성한
manifest.json·packages-lock.json·ProjectSettings.asset 및 신규
PhysicsCoreProjectSettings2D.asset·ProjectAuditorSettings.asset을 제품 상태로 수용.
결과: 동일 버전 재실행에서 추적 파일 변경 0을 실측. tools/game_adapter.py와
tools/capture_game.py에 버전 불일치 에디터 실행 금지 가드 추가 — 불일치 시 캡처는
GameCli 폴백(동일 장면 상태의 실제 래스터 PNG), 케이스 호스트는 BLOCKED로 보고해
향후 불일치 에디터의 추적 소스 무단 변경을 차단한다.
불변: Unity 6 엔진 결정, gates.json 기대값, CASE<TAB>key<TAB>json 계약.
