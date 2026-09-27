# 결정 기록
USER_FIXED: 단순방치형·머신5·재료연구·랜덤인물·도감/단골/의뢰X·윗면64×31·공간64×62·시각두께4·탁자의자-8px·Mac/Windows·UI/UX제작·sprite-gen 참고.
MVP_DEFAULT: Godot·8메뉴·5연구·1body/3프리셋/4hair/3옷/2안경·8×8룸·가격/시간·직원상한.
REFERENCE_FACT: 첨부 실행기는 Devin Gauntlet Runner. 단순 prompt skill 아님.
UNVERIFIED: 실제 OAuth 생성/정확한 호출 모델/네이티브 빌드/아트 가독성/출시 페이스.
다른 기획으로 새로 시작하지 않는다. 변경 시 ID·이유·전후값·시뮬레이션·승인 여부를 추가한다.

## 2026-09-27 v0.8 — 사용자 정정 적용
USER_FIXED: 두께는 시각 표현만, 논리·물리 높이 없음. 탁자/의자는 기준점에서 화면 위8px.
SUPERSEDED: assistant가 제안했던 두께 추가 캔버스64×66, 바닥/가구 일괄 bottom-center 정렬,
            실제 z로 해석하는 방식은 폐기한다. old top64×32도 활성 계약에서 제거한다.
ENGINE_DEFAULT_NOT_USER_FIXED: 윗면 연속 영역 반폭32·반높이15.5, 바닥 작업 캔버스64×62,
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
