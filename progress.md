# 진행 — v0.8 새 규격
HANDOFF_ONLY / actual game NOT_RUN / native Windows NOT_RUN / native macOS NOT_RUN.
변경: top64×31·space64×62·visual4 only·table/chair effective offset(0,-8) once.
패키지 정적검산은 루트 VALIDATION.md를 참조. 게임 시험은 새 gates로 실행 전이다.
reference-audit: 매니페스트 43항 sha256 전수검증 완료, source_samples→배송 디렉터리 byte-identical 0건, 폰트 0건. 감사 기록 references/audit_v0.8.md 생성(사실/가정 구분). git 커밋은 runner 소관.

## project-boot — v0.8.2 dual-host 스켈레톤 구축
구축: game/ Unity 프로젝트(ProjectVersion 6000.0.51f1, Packages 최소 모듈),
Assets/CozyCafe/Core 순수 C# 코어(UnityEngine 무참조 10파일, asmdef),
GameCli .NET8 호스트(case/render), Editor GauntletEntry/CaptureShot,
tools/check_stage.py·tools/capture_game.py. game_adapter에 Windows형
DOTNET_BIN/UNITY_BIN 환경경로 정규화 추가.
검증: dotnet build Release 경고0·오류0. check_stage project-boot →
GameCli가 CASE game_scene_loaded=true·test_entry_calls_real_modules=true를
실제 코어 호출로 방출, 2/2 일치. Unity 6000.0.51f1 에디터 실기동:
GauntletEntry.Run이 동일 CASE 기록, CaptureShot.Run이 960×600 실제 뷰포트
PNG(.evidence/boot.png) 저장. GameCli render fallback도 532×272 PNG 생성.

## idle-economy — 자동판매·강화 실구현
구축: Core/Economy 신규(MvpData·EconomyModule). mvp.json을 런타임 로드하는
MiniJson.Parse 추가(외부패키지 없음, Unity/net8 공통컴파일). 메뉴별 독립
판매타이머, 단계배수(1/10/25/50), 강화비=ceil(Lv1분당수익×base_minutes)×
(112/100)^(L-1)을 decimal로 계산, 1/10/MAX는 단계별 올림 후 합산, 지갑은
정수코인+소수 잔여분(1/100) 보존·음수 충전 거부, 판매 이벤트ID 해시셋으로
중복정산 불가. GameBootstrap 레지스트리에 economy 모듈 등록(실제 Probe).
검증: dotnet build Release 경고0·오류0. check_stage idle-economy → GameCli가
CASE 8키를 실제 코어 호출로 방출: base_rate44·coins_25m1100·
espresso_lv10_bonus5_rate420·cost_lv1 80·cost_lv9 199·cost_lv10 222·
negative_wallet false·npc_pays_coins_again false — 8/8 일치.
project-boot 재검사 2/2 유지. 수치 변경 없음(DECISIONS 기록 대상 없음).

## iso-grid — v0.8 격자 계약 실측 게이트
구축: Core/Iso(IsoMath 투영 sx=(x-y)*32·sy=(x+y)*15.5·역변환 x=sx/64+sy/31·
y=sy/31-sx/64, IsoContract 실래스터 측정), Core/Render(SoftwareCanvas·
TileArt 64×62 작업캔버스·SceneRenderer). 스냅은 최종 합성 1회, 두께4는
외곽 스커트 도색 전용(physics0·collider·동선 미사용·64×66 캔버스 없음).
StageCases iso-grid는 전부 실모듈 호출값; 측면판출 RoomGrid 경계 규칙.
검증: dotnet build Release 경고0·오류0. check_stage iso-grid → GameCli가
CASE 16키를 실래스터/실측으로 방출, 16/16 일치(tile_top 64×31·space·
canvas 64×62·visual4·physical0·step 32/15.5·interior_side_faces 0·
roundtrip_error 0·seam_or_overlap 0·pitch 구분 true).
capture_game.py → Unity 6000.0.51f1 CaptureShot.Run 실카메라 경로로
iso_grid.png 352×320(4×4 무이음 바닥·하이라이트·원점 캐럿)와
tile_canvas.png 320×310(64×62 캔버스 4배율·윗면띠 구분선) 생성,
PNG 구조검사 통과·frozen ref 형상 일치. 병합충돌 해소: StageCases에
iso-grid+idle-economy 양쪽 케이스 유지, project-boot 2/2·idle-economy
8/8 재검사 유지. 수치 변경 없음.
수정(critic gap): artifact-002 타일이 캔버스 아래로 ~4px 치우쳐 보이던
문제 — TileArt가 옆면 스커트를 윗면 밖으로 4px 내리 그려 도형 하단 꼭짓점이
가이드선(31행) 아래로 넘어감. 이제 윗면 마름모를 먼저 채우고 두 아래 변에
걸치는 얇은 옆면 띠(법선 방향 안쪽 ~0.8px·바깥 ~0.5px, 실측 게이트의 4px
두께를 띠 폭으로 표현)를 칠해 도색 슬래브가 캔버스 0~31행에 정확히 들어감:
꼭짓점 상단 접합·하단 꼭짓점 미드라인. Unity CaptureShot로 두 PNG 재캡처,
check_stage iso-grid 16/16·project-boot 2/2·idle-economy 8/8·
research-staff 8/8 재검사 유지. 바닥 스커트·측면판출 규칙·수치 불변.
## research-staff — 연구·메뉴·직원 실구현
구축: Core/Research 신규(ResearchModule)·Core/Staff 신규(StaffModule·StaffCandidate).
MvpData에 staff.candidates 파싱 추가. 연구는 진행1+대기3, 사용자 예약 순서,
조건(선행AND)+코인 충족 시 자동 시작, 앞 막히면 뒤 건너뛰지 않음, 시작 시 비용
1회 충전, 대기 취소/순서변경·시작 후 취소 불가. 완료는 이벤트 경계: 연구ID는
CompletedResearch 영구 플래그, 머신류 해금(ice/steam/blender)은 OwnedMachines,
재료류 해금(milk/chocolate)은 UnlockedFlags, InventoryItems는 MVP 범위상 항상
비어 있어 재고 항목이 되지 않음. 메뉴는 머신+연구 AND게이트로 자동 Lv1 해금.
직원은 세대시드로 후보3 영구 생성, 창 열기/재시작으로 재추첨 없음, 채용 후 해당
슬롯만 보충, 무료1+최대3·추가 고용2000을 실제 지갑으로 청구, 외형(헤어/옷/안경/
팔레트)과 판매·연구 보너스(각0~10%)는 독립 시드 스트림으로 추첨. 두 모듈을
GameBootstrap 레지스트리에 등록(실제 Probe가 프로젝트 부트 게이트에 포함).
검증: dotnet build Release 경고0·오류0. check_stage research-staff → GameCli가
CASE 8키를 실제 코어 호출로 방출: machines5·menus8·research5·
milk_is_inventory false·ice_milk_unlocks_M04 true·missing_steam_blocks_M05 true·
reopen_rerolls_staff false·stat_appearance_independent true — 8/8 일치.
project-boot 재검사 2/2 유지, idle-economy 재검사 8/8 유지. 수치 변경 없음.