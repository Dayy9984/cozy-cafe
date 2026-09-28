# 진행 — v0.8 새 규격
HANDOFF_ONLY / actual game NOT_RUN / native Windows NOT_RUN / native macOS NOT_RUN.
변경: top64×32·space64×64·visual4 only·table/chair effective offset(0,-8) once.
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
구축: Core/Iso(IsoMath 투영 sx=(x-y)*32·sy=(x+y)*16·역변환 x=sx/64+sy/31·
y=sy/31-sx/64, IsoContract 실래스터 측정), Core/Render(SoftwareCanvas·
TileArt 64×64 작업캔버스·SceneRenderer). 스냅은 최종 합성 1회, 두께4는
외곽 스커트 도색 전용(physics0·collider·동선 미사용·64×68 캔버스 없음).
StageCases iso-grid는 전부 실모듈 호출값; 측면판출 RoomGrid 경계 규칙.
검증: dotnet build Release 경고0·오류0. check_stage iso-grid → GameCli가
CASE 16키를 실래스터/실측으로 방출, 16/16 일치(tile_top 64×32·space·
canvas 64×64·visual4·physical0·step 32/16·interior_side_faces 0·
roundtrip_error 0·seam_or_overlap 0·pitch 구분 true).
capture_game.py → Unity 6000.0.51f1 CaptureShot.Run 실카메라 경로로
iso_grid.png 352×320(4×4 무이음 바닥·하이라이트·원점 캐럿)와
tile_canvas.png 320×310(64×64 캔버스 4배율·윗면띠 구분선) 생성,
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
## character-rig — 공유리그·파츠/팔레트·랜덤외형 실구현 (v0.8.2)
구축: Core/Character 신규(CharacterModule.cs). RigDescriptor 1개(셀64×80·발앵커32,72·
방향SW/SE/NW/NE·상태idle1/walk4/sit1/work2)는 data/art_contract.json에서,
시작 프리셋3(casual_01/02·staff_01)은 data/character_presets.json에서 로드·id를
데이터 파츠표(hair_01..04/outfit_01..03/apron_01/glasses_01..02)로 해석.
파츠·팔레트 수(4/3/1/2, 6/8/8) 전부 데이터 원본 참조·하드코딩 없음. 절차적
레이어 래스터라이저(공통몸체+파츠)·정확일치 팔레트 변환(RGB만 교체, 알파 바이트
무기록·기하 불변)·공유 프레임시계 phase(모든 레이어 동일 앵커/위상)·후면시
안경을 머리카락 아래 그리는 방향인지 가림·seed→외형콤보 결정론 롤(손님/직원,
스탯과 무관)·임시 스폰/디스폰(코덱스·단골·의뢰·변형PNG 없음). StaffModule의
후보 외형 추첨을 동일 리그 롤로 통합(독립시드 유지). StageCases에
character-rig 케이스, StageScenes에 4방향 조립 장면, SceneRenderer에
캐릭터 블릿+향위 표시 추가. Modules.cs의 구 스텁 CharacterModule 제거,
GameBootstrap이 실모듈 등록.
검증: dotnet build Release 경고0·오류0. check_stage character-rig → CASE
5키 실모듈 측정으로 방출: rig_count1·starter_presets3·
palette_moves_geometry false(전 프리셋×4방향×계약 전 상태idle/walk/sit/work
알파+커버리지 바이트 비교, 변환 실작업 recolored_px82432)·
layer_phase_synced true(전 레이어 앵커32,72·위상 공유·전 상태 프레임간
도형이동 동일)·customer_collection_added false(24명 스폰→디스폰→스냅샷
잔여0·코덱스 섹션 없음) — 5/5 일치.
추가 근거키: glasses_visible SW18·SE18·NW0·NE0(전 방향 실측, 후면 2방향
모두 머리카락 아래 가림 확인)·part 4/3/1/2·palette 6/8/8·
variant_png_assets0(상수0이 아니라 배송 자산 루트art/+game/Assets/CozyCafe
실제 *.png 콤보서명 스캔 결과)·appearance_seed_replay true.
capture_game.py → Unity 6000.6.3f1 CaptureShot.Run 실카메라 경로로
out/character_rig.png 600×400(GameScene.Zoom=2, 양 호스트 동일 픽셀;
4방향 조립 캐릭터+바닥·향위 틱, 파츠 조립 식별 가능한 배율) 생성·PNG 검증
통과; GameCli render fallback도 동일 픽셀 출력.
강화(보강): 가림 증거를 SW/NW 한 쌍에서 전 4방향으로 확장, 위상·팔레트
불변 스윕을 계약 전 상태로 확장, variant_png_assets을 실측 스캔으로 전환,
capture_game.py가 Windows형 절대경로(X:/…)를 cygwin/msys에서 /x/…로
정규화해 수용하도록 보강(판정 기준·게이트 불변, 증거 범위만 확장).
## iso-grid — v0.8 신규격 코어 상수 갱신(SPEC_CHANGE 전파)
배경: 8563b32 SPEC_CHANGE(윗면64×32·공간64×64·피치32/16)가 gates·데이터·문서를
갱신했고 코드 상수는 신규 런 빌더 몫으로 남겨졌다(DECISIONS.md 명시). 구 코어는
64×31/64×62/15.5라 iso-grid가 4키 미스매치로 실패 상태였다.
변경: IsoMath 상수·역변환 분모(31→32)·TileCanvasContract 64→64·TileArt
다이아몬드 반높이16·측면띠 변 위치·MeasureFloorSeams 버퍼·관련 주석 전면 갱신.
MeasureTopFace는 투영 코너의 연속 구간(64×32)을 실측 — planning/04의
"연속 투영 영역" 해석 그대로. 단일셀 도색 bbox는 공유 half-open 규칙상
적도 끝 픽셀이 상하 이웃 소유라 62로 나오며(바닥 합집합이 무이음인 정확한
이유), 래스터 감사는 MeasureFloorSeams가 계속 담당한다.
art/asset_catalog.json 타일 6항목 수치도 계약값으로 정합.
검증: check_stage iso-grid 16/16·project-boot 2/2·research-staff 8/8·
character-rig 5/5 전부 GameCli 실측 일치. Unity 에디터 실기동 시
6000.6.3f1으로 프로젝트 마이그레이션됨(ProjectVersion·manifest·
packages-lock·신규 ProjectSettings 자산은 에디터 자동 변경분, 실상 기록).
수치 변경: SPEC_CHANGE 전파 외 없음(데이터 원본·게이트 불변).
