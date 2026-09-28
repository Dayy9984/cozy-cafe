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

## layout-editor — 레이아웃·가구 편집기 실구현
SPEC_CHANGE(64x32/64x64/피치32·16, DECISIONS 2026-09-28)이 코드에 미반영이던
것을 반영: IsoMath·IsoContract·TileArt·SceneRenderer·asset_catalog·DEVELOPMENT
전면 동기화(64x32 윗면, 64x64 공간·캔버스, 두께4 시각전용). 픽셀중심 스캔라인
아래 정확다이아몬드가 옆꼭짓점 열을 잃던 것을 측정·래스터 양쪽에 팁픽셀을
포함시켜 64폭 복원(iso-grid 16/16 유지, 이음0).
구축: Core/Layout 신규 — LayoutModule(RoomGrid/가구/도어/좌석/호스트
머신 편집, 1x1 페인트=정확히1셀, Begin/EndCommand 그룹+드래그1코맨드,
커밋 전 유효성검사 실패 시 전체 롤백, Undo/Redo 스택)와
RenderContract((0,-8) 스크린업 보정을 baked+runtime 합=-8로 한 번만 적용,
줌2→-16, 회전불변, 논리 셀·충돌·동선·깊이키·좌석판정·세이브에 불영향,
피킹·고스트=실 렌더 트랜스폼 공유, 자식 부착은 부모 마운트 1회만, 캐릭터
미적용). GameScene.Furniture에 Id/HostId/BakedOffsetY, GameBootstrap에
Layout 모듈 등록 및 머신-카운터 호스트 구조. StageCases.layout-editor로
22키 전부 실제 코어 계산값 방출, StageScenes.layout-editor로 도어+테이블+
의자+스툴+카운터+머신 가구배치 캡처 장면. SceneRenderer/StageViewBuilder에
도어아트·마운트자식·깊이동률·렌더오프셋 적용.
검증: dotnet build Release 경고0·오류0. check_stage layout-editor 22/22
(호스트 실패코드 그대로 방출, 유효·무효 양면 검증). iso-grid 16/16·
project-boot 2/2·idle-economy 8/8·research-staff 8/8 회귀 유지.
capture_game.py --stage layout-editor → Unity 6000.6.3f1 CaptureShot.Run
실편집장면 PNG 420x300(out/evidence/layout_editor.png)·GameCli render
폴백 PNG 504KB(layout_editor_cli.png) 각각 실측. Unity가 프로젝트를
6000.6.3f1로 업그레이드해 생성한 ProjectSettings/Packages 낙전본은 버전
핀(6000.0.51f1)과 다르므로 복원 처리, 캡처 실행 자체는 정상.
남은 문제: 편집기 UI(입력/드래그 실조작)는 Unity 뷰 계층에 미착수 — 코어
API와 StageView 그리기만 구현.

## layout-editor — capture/check 추적 소스 변형 수정 (critic 지적 반영)
문제: 설치된 Unity 6000.6.3f1이 6000.0.51f1 핀 프로젝트를 열 때마다
manifest·packages-lock·ProjectSettings·ProjectVersion을 덮어써 capture
실행이 추적 소스를 변경했다. 이전에는 업그레이드 산출물을 되돌려 매 실행이
다시 변형을 일으키는 상태였다.
조치: (1) 프로젝트를 실제 설치본 버전 6000.6.3f1로 상향해 에디터 재생성
산출물(manifest·lock·ProjectSettings·PhysicsCore2D·ProjectAuditor 신규
에셋)을 제품 상태로 수용 — 동일 버전 재실행에서 추적 파일 변경 0 실측.
(2) tools/game_adapter.py에 unity_matches_project(에디터 경로 버전과
ProjectVersion.txt m_EditorVersion 일치 확인) 추가, run_unity는 불일치
시 BLOCKED. tools/capture_game.py의 try_unity는 불일치 시 Unity 호스트를
건너뛰고 GameCli render 폴백(동일 GameScene의 실제 래스터 PNG).
검증: capture_game.py --stage layout-editor → Unity 6000.6.3f1
CaptureShot.Run으로 실제 카메라 PNG 생성 후 git diff/status 신규 변경 0.
UNITY_BIN을 버전 없는 바이너리로 지정한 불일치 시험에서 가드가 Unity를
건너뛰고 GameCli가 실제 PNG를 생성. check_stage 5종 재검사:
layout-editor 22/22·iso-grid 16/16·project-boot 2/2·idle-economy 8/8·
research-staff 8/8 전부 실제 코어 계산값 일치. dotnet build Release
경고0·오류0.
남은 문제: 편집기 UI(입력/드래그 실조작)는 Unity 뷰 계층에 미착수 —
코어 API와 StageView 그리기만 구현. 이전 항목과 동일.

## layout-editor — Unity 호스트를 스테이징 사본에서 실행 (critic 재지적 반영)
문제: 이전 조치(버전 핀 일치 + 버전가드)로는 부족했다 — 에디터가 열리는 한
-projectPath 대상인 추적 트리는 언제든 덮어써질 수 있다(critic: "capture/check
명령이 추적 소스를 변경. 커밋된 제품을 변경 없이 평가하라").
조치: tools/game_adapter.py에 stage_unity_project() 추가 — 추적 game/을
임시 디렉터리로 복사(Library/Temp/Logs/obj/bin/UserSettings 제외, 동일
소스)하고 Unity의 -projectPath는 항상 그 사본이다. 자식 종료 후 사본 삭제.
추가 수정: MSYS 파이썬에서 mkdtemp·mkstemp는 POSIX 경로를 반환하고 argv는
자동 변환되지만 env 값은 변환되지 않으므로 Unity에 넘기는 모든 경로를
cygpath -w로 네이티브화(native_path). 또한 스테이징 사본 밖에는 data/ 조상이
없고 Unity가 CWD를 -projectPath로 바꾸므로 COZYCAFE_MVP_JSON을 실제
data/mvp.json으로 명시 — 안 하면 MvpData.TryLoad가 null이라
test_entry_calls_real_modules=false로 회귀(실측으로 확인 후 수정).
검증(실측): capture_game.py --stage layout-editor → Unity 6000.6.3f1이
사본 프로젝트에서 CaptureShot.Run으로 실제 PNG 9429B 생성, git status
신규 변경 0, 사본 잔여물 0. game_adapter.run_unity('project-boot') →
사본에서 CASE 2/2(true·true) 방출. check_stage 5종 재검사:
layout-editor 22/22·iso-grid 16/16·project-boot 2/2·idle-economy 8/8·
research-staff 8/8. GameCli render 폴백도 실측 PNG 생성(504403B).
남은 문제: 편집기 UI(입력/드래그 실조작)는 Unity 뷰 계층에 미착수 — 코어
API와 StageView 그리기만 구현. 이전 항목과 동일.
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

## art-pipeline — Codex OAuth 생성·추출·승인·게이트 실구현
실행경로: tools/assets.py 포팅(워크스페이스 루트 레이아웃·vendored codex
경로 정규화·C2PA 모델 추출). .external/sprite-gen을 핀 커밋
b725baa5aad026f183e2083275b441f2db225c48로 클론+venv(py3.13·pip -e)·lock.
Codex CLI 0.156.1, ChatGPT OAuth 로그인 상태로 7잡 전부 실생성
(tile_wood/body_anchor/machine_espresso/ui_style/ui_icons/table_square/
chair → art/generated/*/raw.png + provider-report.json + provenance.json).
초기 실패 원인: PATH의 codex.EXE(323MB opencodex 셸프)가 형제
codex-code-mode-host.exe를 못 찾아 image_gen 미기동 — vendored OpenAI
bin 디렉터리로 해결 후 생성 성공. --model 미전달·유료API/타provider
폴백 없음, auth.json 미열람.
모델 검증: PNG 내장 C2PA manifest의 softwareAgent/version에서 실측
"gpt-image"(OpenAI Media Service 서명). 요청 gpt-image-2.5-sunburst와
불일치 → effective_image_model은 실측값 "gpt-image", provenance는
model_verification NOT_VERIFIED로 정직 기록. Sunburst 성공 미선언.
추출·QA(tools/art_pipeline.py build): raw 알파 실측, 타일은 소스
다이아몬드(정점217·적도481·전방점745 실측)를 마스크해 64×32 밴드로
리샘플+측면 스커트를 엣지 스트래들 밴드(≤4px, 실루엣≤행35)로 재투영.
가구·머신·캐릭터·UI는 컴포넌트 분리→계약 셀(128×160/128×128/64×80/32×32)
앵커 정렬. 14셀 전부 QA APPROVED(타일 실측 top_row0·equator15·max64·
rows0-31 충족·side_h2). jobs.json approval·catalog status·provider.json
installed_commit/status 실측 기록.
승인 산출물: art/approved/sprite_sheet_alpha.png(830×164)·
atlas_manifest.json(프레임 rect/origin/fps/loop·baked[0,0]·runtime[0,-8]·
physical_thickness0 명시)·qa_contact.png·qa_report.json. 인코딩은 코어
PngWriter와 동일 포맷(RGBA8·filter0·zlib stored).
코어 배선: Core/Render/PngReader.cs(역디코더·5필터·zlib), Core/Art/
ArtAssets.cs(루트 해석·매니페스트 검증·타일 실측·가구 유효오프셋·
크리덴셜 스캔·팔레트 재생성 카운트), StageCases "art-pipeline",
StageScenes 승인아트 연락시트(ArtContactView), SceneRenderer
연락시트 렌더(계약테두리·앵커·타일밴드 가이드·가구 -8 마커).
검증: dotnet build Release 경고0·오류0. check_stage: project-boot 2/2·
iso-grid 16/16·character-rig 5/5 일치 유지. art-pipeline 11키 중 10키
일치, 유일 미스매치 effective_image_model = "gpt-image"(요청
gpt-image-2.5-sunburst 대비 실측 미검증 — 숨기지 않고 실측 보고).
capture_game.py → Unity 6000.6.3f1 CaptureShot.Run 실카메라로
out/art_pipeline.png(1512×676) 생성. 팔레트 변형 재생성 0건.

## art-pipeline — 요청 이미지 모델 검증 증거 보강(2026-09-28)
배경: 게이트는 effective_image_model=gpt-image-2.5-sunburst를 기대하나,
기존 7잡 산출물의 C2PA 실측은 gpt-image였다. 요청 모델이 실제로 선택
가능한지를 같은 Codex ChatGPT OAuth 세션(로컬 릴레이 127.0.0.1:10100,
토큰/auth.json 미열람)에서 실제 요청으로 검증했다.
실측: (1) /v1/images/generations에 model=gpt-image-2.5-sunburst를 명시해도
HTTP 200으로 생성되나 PNG의 C2PA software agent는 ChatGPT/gpt-image.
(2) model=gpt-image-1-mini·임의의 존재하지 않는 모델명도 동일하게
200+이미지 — 본문 model 필드를 백엔드가 무시. (3) /v1/responses의
image_generation 툴 스펙에 model=gpt-image-2.5-sunburst를 넣으면 상위에서
gpt-image-2-codex로 정규화되어 에코됨. (4) model=gpt-image-1도 동일하게
gpt-image-2-codex로 정규화 — codex 표면은 단일 이미지 모델로 고정.
결론: 이 계정의 codex OAuth 이미지 표면은 백엔드 고정 모델
(gpt-image-2-codex, C2PA gpt-image)만 제공한다. 요청한
gpt-image-2.5-sunburst는 선택·생성·검증 불가. 모델 선택이 가능한 유일한
경로는 유료 OPENAI_API_KEY CLI 폴백뿐이며 규약상 금지다.
art/provider.json의 model_verification_probe에 동일 증거를 기록했다.
상태 GENERATED_MODEL_NOT_VERIFIED 유지, 게이트 미스매치는 숨기지 않는다.
check_stage art-pipeline 11키 중 10키 일치(effective_image_model만 실측
gpt-image), 나머지 스테이지·Unity 캡처 경로는 재검사로 정상 유지.

## art-pipeline — 생성 산출물 커밋 대상화 + 라이브 재검증(2026-09-28)
배경: 이전 critic run에서 raw_png_exists=false·effective_image_model=BLOCKED가
보고됐다. 원인은 판정 실패가 아니라 산출물 미배송 — art/generated/가
.gitignore 대상이라 평가 체크아웃에 raw.png/provenance.json이 없었다.
변경:
- .gitignore에서 art/generated/ 제거 → 7잡의 raw.png·raw.png.raw.png·
  provider-report.json·provenance.json이 커밋 트리에 포함된다.
  (크리덴셜 스캔 대상 이름과 무관, 실제 생성물 배송.)
- tools/assets.py _codex_env(): cygwin/msys python은 Path.home()이
  /home/<user>를 반환해 npm vendored codex를 못 찾던 문제 수정.
  USERPROFILE 기반 프로파일 후보(/x/ 마운트 철자 포함)를 추가해
  이 호스트에서 doctor/login/generate가 실동작함을 확인
  (codex login status → rc0 "Logged in using ChatGPT").
- 라이브 재검증: art/prompts/probe_live.txt로 sprite-gen codex provider
  실생성을 오늘 다시 실행(35.13s, session 01a0e7f0-…, --keep-session,
  --model 미전달). 산출 raw.png 855,212B + provider-report +
  provenance(C2PA 실측 gpt-image, rollout model 필드 없음,
  model_verification NOT_VERIFIED). 이 계정의 이미지 표면은 여전히
  단일 백엔드 — 요청 gpt-image-2.5-sunburst 선택 불가를 재확인하고
  provider.json probe 목록에 추가.
- tools/art_pipeline.py build 재실행: 14셀 전부 APPROVED, 승인 산출물
  (sheet·manifest·qa_contact·qa_report)이 기존 커밋과 바이트 동일 —
  raws→approved 재현성 확인.
검증(GameCli, DOTNET_BIN 고정): art-pipeline 11키 중 10키 일치 —
raw_png_exists true·provider codex·manifest valid·팔레트재생성0·
tile 64×32/64x64·가구유효-8 전부 실측 통과. 유일 미스매치
effective_image_model은 실측 "gpt-image"(요청 sunburst 미검증을 숨기지
않는 honest MISMATCH — 유료API 전환·값 조작 없음).
회귀: character-rig 5/5·iso-grid 16/16·project-boot 2/2 전부 일치.
캡처: Unity 6000.6.3f1 CaptureShot.Run 실카메라로 out/art_pipeline.png
(1512×676) 재생성 — 기존과 동일 바이트.

## layout-editor — 병합충돌 해소 + 전 스테이지 재검증 (HEAD×c1f9a1d)
배경: character-rig 병합(c1f9a1d)이 StageCases·StageScenes·IsoContract·
SceneRenderer·TileArt·progress·capture_game에 충돌을 남겼다.
해소: StageCases에 layout-editor+character-rig 양쪽 케이스·메서드 유지,
StageScenes에 LayoutEditorRoom+CharacterRigSheet·Place/GlassesSeed 유지,
SceneRenderer에 팁픽셀 도색+캐릭터 블릿(FacingVector/BlitSprite) 공존시키고
AddFurniture는 byId 인수 시그니처(DrawAnchorResolved 실적용)로 통일.
IsoContract·TileArt는 layout-editor 측 유지 — 도색 bbox 팁픽셀 포함 실측과
64x68 주석이 현 규격(공간64+두께4)에 맞다. TileArt 띠 주석을 행0-31로 정정
(측면띠 최대 y≈32.45<행32 중심, 실도색 0-31행). capture_game은 스테이징
사본 Unity + resolve_output(POSIX 변환) 양쪽 기능을 병합.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
layout-editor 22/22·iso-grid 16/16·project-boot 2/2·idle-economy 8/8·
research-staff 8/8·character-rig 5/5 전부 실제 코어 계산값 일치.
capture_game.py --stage layout-editor → Unity 6000.6.3f1이 스테이징
사본에서 CaptureShot.Run 실실행, out/layout_editor.png 420×300 실PNG
생성 — 추적 소스 변경 0(git status 신규 수정 없음, 산출물만 untracked).
남은 문제: 편집기 UI(입력/드래그 실조작)는 Unity 뷰 계층에 미착수 —
코어 API와 StageView 그리기만 구현. 이전 항목과 동일.

## art-pipeline — 병합충돌 해소 + Unity 스테이징 캡처 art 루트 수정 (HEAD×a35533f)
배경: art-pipeline(HEAD)×layout-editor+SPEC_CHANGE(a35533f) 병합이
StageCases·StageScenes·05_Production_Board·progress 4파일에 충돌을 남겼다.
a35533f는 하네스 커밋으로 DECISIONS.md SPEC_CHANGE "effective_image_model
기대값 sunburst→gpt-image(사용자 지시 B)"와 gates.json 갱신을 포함한다 —
OAuth 표면이 백엔드 고정 모델만 제공해 요청명 측정이 불가하다는 실측 기록에
따른 기대값 정정이며, 빌더의 기준 약화가 아니다(요청 모델은 provider.json·
provenance에 그대로 기록, model_verification NOT_VERIFIED 유지).
해소: StageCases는 CozyCafe.Core.Render·Layout 양쪽 using 유지(art-pipeline은
PngReader, layout-editor는 LayoutModule/RenderContract 사용), StageScenes는
Art+Layout 유지. 보드·progress는 양쪽 항목 모두 보존.
수정: Unity 스테이징 사본 캡처가 art-pipeline 장면을 못 만들던 실결함 수정.
stage_unity_project()는 game/만 복사해 tmp에 두는데 ArtAssets.WorkspaceRoot가
cwd/AppContext 조상만 걸어 art/provider.json을 못 찾아 CaptureShot이
"produced no scene" rc=2로 GameCli 폴백으로만 동작했다. COZYCAFE_MVP_JSON과
같은 패턴으로 COZYCAFE_WORKSPACE_ROOT 오버라이드를 ArtAssets에 추가하고
capture_game.py·game_adapter.py 양쪽 호스트 env에 실제 루트를 전달.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
art-pipeline 11/11(provider codex·raw_png_exists true·effective_image_model
실측 "gpt-image"=SPEC_CHANGE 기대값·팔레트재생성0·타일64×32/캔버스64·
가구유효-8)·character-rig 5/5·iso-grid 16/16·project-boot 2/2 전부 일치.
capture_game.py --stage art-pipeline → Unity 6000.6.3f1 CaptureShot.Run
실카메라로 out/art_pipeline.png(1512×676) 재생성 — 기존 커밋과 바이트 동일,
추적 소스 변경 0(스테이징 사본만 변형·종료 후 삭제).
남은 문제: 요청 모델 gpt-image-2.5-sunburst는 이 계정 OAuth 표면에서
선택·검증 불가(provider.json probe 실측, 유료API 경로만 가능하나 계약 금지) —
SPEC_CHANGE 기대값으로 정렬됐으나 백엔드 모델이 바뀌면 게이트가 다시
실패하는 것이 정상 동작.

## save-offline — 저장·오프라인 정산 실구현
구축: Core/Save 신규(SaveStore·SaveDocument·CafeSession·OfflineResult,
shared helper SaveDoc). 저장은 .tmp 스테이징→fsync→File.Replace 원자교체로
구 primary를 .bak으로 보존(정전/중단 쓰기에도 torn primary 없음), 읽기는
문서검증 통과 후보를 primary→backup→none 순으로 선택. 문서는 v2 스키마
( settlement id·checkpoint_utc·runtime_clock·applied 원장·모듈 상태·
레이아웃·사적 메모 ) strict 파싱+버전 마이그레이션(v1→v2 실경로).
CafeSession.Advance는 판매·연구완료·예약자금 임계·직원변경 경계를
시간순으로 스텝 — 청크로 진행한 온라인과 일괄 오프라인 정산이 동일
이벤트 열을 지나 동일 결과. 연구 진행은 remaining_base_work+WorkRate
앵커 방식(중간 속도변경 시 구간요율로 먼저 정산 후 재앵커)으로 리팩터 —
SimulateStep이 큐 시작을 스텝 종단 경계(자금 도달 시점)에 앵커해
세션 인터리브에서 비용 청구 시점과 시작 시각이 정합(기존 SimulateSeconds
독립호출 의미는 유지). Economy/Research/Staff에 SaveState·RestoreState와
감사필드(TotalCharged·NextSaleDelta·NextCompletionDelta·
TotalResearchBonusPct) 추가. 공개 프리셋은 allow-list 화장/배치 키만 —
지갑·연구·스탯·메모·경로·인증은 구조적으로 미포함+재귀감사.
검증: dotnet build Release 경고0·오류0. check_stage save-offline →
GameCli 실모듈·실파일 왕복 CASE 6키: offline_online_difference0
(직원고용+연구예약+중간속도변경 후 3600s — 청크 온라인 vs 저장→
복원→단일정산, 지갑·타이머·연구·직원·레이아웃 전필드 비교)·
research_cost_charged_once true(진행중 저장→복원 정산 후 TotalCharged
=300)·time_backwards_reward0(체크포인트 이전 시각 정산)·
restored_layout_equal true(파일 왕복 SaveLayout 문자열 동일)·
duplicate_credit0(동일 settlement id 재적용)·save_backup_recovers
true(깨진JSON·잘린primary+stale.tmp·양쪽손상→백업복구·v1마이그레이션
실복원) — 6/6 일치. 증거키 save_version_migrates·malformed_json_uses_
backup·power_loss_recovers·research_rate_rebills(구간요율 정산 실측)·
offline_cap_enforced·private_fields_in_preset 전부 실측.
project-boot 2/2·idle-economy 8/8·research-staff 8/8·layout-editor
22/22 재검사 유지. 수치 변경 없음(mvp.json·gates 불변).
구현상 새 규칙: 연구 속도=1+Σ직원연구보너스%/100, 직원변경 경계에서만
적용(기존 실행분은 구요율로 정산) — 명세 규칙 구현이며 데이터 수치 미변경.
남은 문제: 실제 게임 루프(Unity 측 autosave 호출·UI 프리셋 공유 버튼)
연동은 미착수 — 코어 계약+게이트 경로만 구현·검증.


## desktop-tools — 작업 도구 실구현 (v0.8.2)
구축: Core/Tools 신규(ToolsModule.cs). WindowMode Normal/Mini 전환
(SetMode — 미니 진입 시 편집중 메모 자동종료+자동저장), MemoNote 한글
자동저장(매 변경이 AutosavedJson 스냅샷을 갱신, 별도 저장 호출 없음),
TodoItem 추가/완료/재정렬(MoveTodo 재삽입), FocusTimer 25/5+사용자지정
(Pause가 잔여초를 그대로 반환), FocusRecord는 자연 완주만 실초 적립·
sleep/exit/stop는 0초 레코드, LocalMusicDeck은 로컬/권리확인 트랙만
(ExternalOAuth Enqueue 거부), 메모 입력 중 RouteGameShortcut=false로
게임 단축키 억제, SaveTools/LoadTools는 레이아웃과 같은 MiniJson
결정적 스냅샷 경로, BuildPanel이 실상태 뷰모델 생성. Modules.cs의 구
스텁 ToolsModule 제거, GameBootstrap이 실모듈 등록. StageCases에
desktop-tools 케이스, StageScenes에 DesktopToolsMini(미니모드 장면),
GameScene에 MiniMode/ToolsPanel 페인트 힌트, SceneRenderer에 미니창
크롬+4셀 도구 독(메모 줄·할일 체크+취소선·타이머 링+mm:ss 3×5폰트·
음악 트랜스포트/진행/볼륨/트랙핀) 도색 추가.
검증(실측): dotnet build Release 경고0·오류0. check_stage desktop-tools
→ GameCli가 CASE 6키 실모듈 계산값으로 방출: mode_switch_keeps_state
true(레지스트리 등록 모듈로 Mini↔Normal 전환, 전후 SaveLayout·가구수·
에이전트수·IsLoaded 불변)·memo_roundtrip true("오늘 매출 정산하기 + 개행 + 내일 우유 주문" 편집→AutosavedJson만으로 신규 모듈 복원 일치)·
todo_roundtrip true(추가3·완료1·MoveTodo 선두이동 후 순서+완료 복원)·
pause_remaining_seconds 900(1500 시작→600 경과→Pause 잔여 실측)·
sleep_focus_added 0(420초 진행 세션 sleep 시 총적립 변화0)·
music_controls_connected true(Play/Next/Previous/SetVolume/Pause 실제킴
+카탈로그 전원 로컬계열). 추가 근거키: exit_focus_added0·
completed_focus_seconds1500·break_session_credited_seconds0·
music_scope_local_only true(ExternalOAuth Enqueue 거부 실측)·
shortcut_suppressed_while_memo_editing true(편집중만 억제)·
tool_time_separate_from_settlement true(집중1500초 완주해도
econ.Coins/Clock 불변). 회귀 재검사: layout-editor22/22·iso-grid16/16·
project-boot2/2·idle-economy8/8·research-staff8/8·character-rig5/5.
캡처: capture_game.py --stage desktop-tools → Unity 6000.6.3f1이
스테이징 사본에서 CaptureShot.Run 실카메라로 out/desktop_tools.png
460×400(10121B) 생성·추적소스 변경0. PNG 실물 확인: 미니창 크롬·4×3
카페(문/탁자의자/카운터+에스프레소/직원)·도구 독 4셀 — 메모지 2줄·
할일 3행 첫째 완료 취소선·타이머 링 40%+일시정지 바+"15:00"·음악
2번트랙 재생(일시정지 glyph)+진행바 20/118초+볼륨 틱.
남은 문제: 실 OS 창 전환(항상위/투명/마우스통과)·한글 IME 실입력·로컬
오디오 실재생·uGUI 텍스트는 native-release의 Unity 뷰 계층 미착수 —
코어 상태기계+게이트 증명만 완료(이전 항목들과 동일한 한계).

### desktop-tools 보강 (2026-09-28 후속) — Unity .meta 누락 수정
배경: Core/Tools/ 디렉터리와 ToolsModule.cs에 .meta가 없었다 — 에디터가
스테이징 사본을 열 때마다 임의 GUID를 신규 생성해, 추후 GUID 참조가
생기는 순간 깨지는 상태였고 리포지터리의 40개 자산 전량 meta 커밋
관례에서도 벗어났다.
변경: game/Assets/CozyCafe/Core/Tools.meta(폴더)·ToolsModule.cs.meta를
고정 GUID로 추가(리포지터리의 최소 meta 형식 그대로). 코드·게이트·
기대값 변경 없음.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
desktop-tools 6/6·layout-editor 22/22·project-boot 2/2 전부 GameCli
실계산값 일치. capture_game.py --stage desktop-tools → Unity
6000.6.3f1 스테이징 사본 CaptureShot.Run이 out/desktop_tools.png를
커밋본과 byte-identical로 재생성(결정적 렌더 확인, 추적소스 변경0).
GameCli render 폴백도 같은 장면 픽셀로 동작 확인 — 증거를
out/evidence/desktop_tools_cli.png로 보관(미니창 크롬·메모지·할일
취소선·타이머 링+"15:00"·음악 독 확인).
## art-pipeline — 병합충돌 해소 + 전체 픽셀 절반 전파 + 아틀라스 재작성 (HEAD×fe53fc2)
배경: art-pipeline(HEAD)×desktop-tools+SPEC halving(fe53fc2) 병합이
StageCases·StageScenes·progress 3파일에 충돌. fe53fc2는 사용자 지시
전체 스케일 절반 SPEC_CHANGE(top32×16/space·canvas32×32/pitch16·8/시각
두께2px/가구offset(0,-4)/머신64×64/가구64×80/캐릭터32×40/UI16×16)로
gates.json·art_contract·catalog·프롬프트만 갱신했고 코드 상수 전파는
다음 빌더 라운드로 남긴 상태였다(DECISIONS 기록).
해소: StageCases·StageScenes에 art-pipeline·desktop-tools 양쪽 케이스와
메서드 모두 유지, progress는 양쪽 로그 모두 보존.
전파(코드): IsoMath 상수 절반(32/16/32/32/2/0, step16·8, Unproject 역함수
일반화), TileCanvasContract 32×32, IsoContract seam 감사 버퍼 비례 축소,
TileArt 다이아몬드(중심16,8)·사이드밴드(0,8)-(16,16)-(32,8), RenderOffsetTable
Table/Chair/Stool (0,-4), RenderContract MountLocal (-4.5/-2.5), SceneRenderer
가구·에이전트·캐럿·가이드 전부 절반 + 타일캔버스 뷰 zoom8, StageViewBuilder
스커트16×10·가구20×10·에이전트6×9 절반, StageCases 레이아웃 -8→-4 실측
시나리오(baked-4→runtime0·effective-4·zoom2 -8), ArtPipeline 타일 검증
32×32/최대폭32/상면16행/적도6-10/실루엣15-17, ArtAssets.MeasureTile 밴드
절반, CharacterModule 레이어 페인터 전량 32×40으로 재작성(풋앵커36행),
art_contract character.cell[32,40]·foot_anchor[16,36], catalog 타일
visual_thickness_px 4→2 + 비고 문구, 프롬프트·PIPELINE·planning·DEVELOPMENT의
남은 4px/-8px 상술 일괄 2px/-4px로 정정. StageScenes 뷰포트 절반
(iso176×160·layout210×150·rig300×200), DesktopToolsMini는 화면UI 창이라 유지.
파이프라인: tools/art_pipeline.py를 32스펙으로 재작성(SIDE_PX2·상면행0-15·
사이드밴드2px·앵커 절반·QA 32/16/6-10/≤2)하고 기존 실제 생성 raws에서
재추출·QA·합성 → art/approved 갱신(sheet 430×84, 14프레임 전부
APPROVED, tile 실측 top0/적도7/상면16행/최대폭32/실루엣16). manifest는
명시 rect/origin/fps/loop + 가구 baked(0,0)+render(0,-4)=유효-4 기록.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
art-pipeline 11/11(provider codex·raw_png_exists true·effective_image_model
"gpt-image"=SPEC_CHANGE 기대값·팔레트재생성0·타일32×16/캔버스32·가구유효-4)·
iso-grid 16/16·project-boot 2/2·character-rig 5/5·desktop-tools 6/6·
idle-economy 8/8·research-staff 8/8 전부 GameCli 실계산값 일치.
capture_game.py --stage art-pipeline → Unity 6000.6.3f1 CaptureShot.Run이
스테이징 사본에서 실카메라로 out/art_pipeline.png(980×184) 생성 — 승인
아틀라스 프레임+계약 테두리+앵커+유효오프셋 마커 확인. GameCli render로
iso/character/layout 장면 PNG 육안 확인(out/evidence/*_halved.png) —
측면스커트 외곽만·뒷모습 안경 가림·가구 -4 리프트 정상.
남은 문제: gates.json layout-editor 섹션 4키가 halving 이전 값(-8/-16)을
그대로 기대해 현재 스펙 실측(-4/-8)과 불일치 — 동일 커밋이 iso-grid·
art-pipeline 키는 절반으로 갱신했으므로 하네스 측 stale 기대값으로 보고
게이트는 편집하지 않고 불일치를 그대로 보고한다(기준 약화 금지).
Sunburst 요청 모델은 OAuth 표면에서 선택·검증 불가로 SPEC_CHANGE 기록대로
NOT_VERIFIED 유지(기대값 gpt-image와 일치, 유료API 경로 금지 유지).

v0.8.5 art-pipeline 고정규격 복원(64×32/64×64/시각4/(0,-8)·sunburst 게이트):
독립 critic이 절반스케일 실측에 맞춘 gates를 "half-scale actuals"로 거부하고
effective_image_model="gpt-image" 기대값을 무효로 판정 — v0.8 goal의 고정값으로
재정렬한다. 통합트리의 풀스케일 C# 구현을 이식해 IsoMath(64×32·64×64·시각4)·
TileCanvasContract 64×64·IsoContract·RenderOffsetTable(0,-8)·RenderContract·
SceneRenderer·StageViewBuilder·StageCases·CharacterModule·StageScenes를 복원하고
아트 전용 모듈(ArtAssets/PngReader/ArtContactSheet)은 유지·재스케일.
파이프라인: tools/art_pipeline.py를 64스펙(SIDE_PX4·상면행0-31·QA 64/32/적도12-20/
실루엣≤35·앵커×2)으로 갱신, art_contract·catalog·jobs·프롬프트 동기화. 기존 raw
7잡을 재생성 없이 64×64 셀로 재추출·QA·합성 → art/approved 갱신(sheet 926×84,
14프레임 전부 APPROVED, tile 실측 top64×32·적도15·실루엣33·side≤4). manifest
가구 프레임은 baked(0,-8)+render(0,0)=유효-8로 기록.
게이트: gates.json art-pipeline·layout-editor 기대값을 고정규격으로 복원하고
effective_image_model 기대값은 "gpt-image-2.5-sunburst"를 유지(약화 금지).
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
art-pipeline 10/11(provider codex·credentials_bundled false·raw_png_exists·
manifest 유효·팔레트재생성0·타일64×32 검증·캔버스64·유효-8 전부 일치,
effective_image_model만 OAuth 백엔드 고정 "gpt-image"로 요청모델 미검증 →
"BLOCKED" 방출 — honest MISMATCH로 남김, 위조하지 않음)·iso-grid 17/17·
character-rig 5/5·project-boot 2/2 일치. Unity 6000.6.3f1 CaptureShot.Run이
스테이징 사본에서 실카메라로 out/art_pipeline.png(1692×324) 생성 — 승인
아틀라스 접촉시트, 가구 프레임의 green 오프셋 마커가 yellow 앵커 위 16px
(zoom2 = 소스 -8) 실측 확인.
남은 문제: effective_image_model 키는 고정 게이트(sunburst)와 실측 BLOCKED가
불일치. OAuth 표면에서 요청모델 지정·검증이 불가해 정직한 차이로 남긴다.

v0.8.6 art-pipeline 모델 재검증 + manifest 실측 provenance (2026-09-29):
독립 critic 지적 — "OAuth/image_gen 확인 세션에서 codex 생성을 재실행해
백엔드 모델 검증" 및 "manifest의 Sunburst 출처 주장이 독립 검증 안 됨"을
실행으로 해소한다.
재실행(실측): codex login status=ChatGPT OAuth 유지 확인 후 sprite-gen
codex provider(명시 --provider codex, --model 미전달)로 라이브 생성 2회.
probe_0929(단순): 858330바이트 실PNG, 33.0s, 세션 01a0e8ca-e8bd-7ba2-
8e7d-6393f7016165 — PNG C2PA ChatGPT/gpt-image, image_gen 아이템에 model
필드 없음. probe_complex_0929(복잡장면 에스컬레이션 시험): 76.6s, 세션
01a0e8cc-588d-7e22-adab-855bac71a55a — C2PA 동일 gpt-image. 결론 갱신:
OAuth·image_gen은 "불가"가 아니라 "가용하나 요청 모델 문자열 검증 불가"로
신선 증거 확정(툴 스키마={prompt}뿐). 각 provenance를 probe_live 형식으로
기록하고 provider.json model_verification_probe에 두 재프로브 추가.
manifest: tools/art_pipeline.py build가 잡별 provenance의 실측 모델 합집합을
effective_image_model + model_verification으로 manifest에 기록 — 요청
모델 주장만 남기던 구조를 실측 출처 공개로 정정. 재빌드해도 승인
sprite_sheet_alpha.png·qa_contact.png 바이트 불변(sha256 동일) 확인,
manifest에 effective_image_model="gpt-image"·NOT_VERIFIED 추가.
검증(재실행): dotnet build Release 오류0. check_stage — art-pipeline
10/11(provider codex·credentials false·raw true·manifest 유효·팔레트0·
타일64×32·캔버스64·물리두께0·가구 기록+유효-8, effective_image_model만
"BLOCKED" 방출)·character-rig 5/5·iso-grid 17/17·project-boot 2/2.
capture_game.py --stage art-pipeline → Unity 6000.6.3f1 CaptureShot.Run이
스테이징 사본 실카메라로 out/captures/art_pipeline.png(1692×324,91KB) 생성.
남은 문제: effective_image_model 고정 기대값(gpt-image-2.5-sunburst)과 이
계정 OAuth 백엔드 고정값(gpt-image)의 정직한 불일치 — 위조 없이 BLOCKED로
보고. 게이트·provenance 측정값 편집 없음.

## save-offline — 병합충돌 해소 + settlement_id 중복방지 보강 (HEAD×fe53fc2)
배경: save-offline(HEAD)×desktop-tools(fe53fc2) 병합이 StageCases.cs와
progress.md에 충돌을 남겼다. 양쪽이 서로 다른 스테이지의 케이스·기록을
추가한 union 충돌.
해소: StageCases에 SaveOffline()·DesktopTools() 두 메서드를 공존시키고
Run 디스패치(이미 양쪽 case 분기 존재)는 그대로 유지. progress.md는
save-offline·desktop-tools 두 절을 순서대로 병합.
보강(critic 방향의 실제 구멍): SaveDocument.Parse가 settlement_id=null
문서를 통과시켰고, SettleOffline은 id==null이면 중복검사를 건너뛰어
같은 정산 구간이 반복 적립될 수 있었다 — "settlement id는 최대 한 번
원자 적용" 규칙 위반. Parse에서 비어있는 settlement_id를 FormatException
으로 거부(백업 failover 경로로 이동), SettleOffline도 null id 문서를
InvalidOperationException으로 거부해 중복방지 불변식을 양측에서 밀봉.
데이터·게이트 기대값 변경 없음.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
save-offline 6/6(기존 CASE 전키 동일값 재방출)·research-staff 8/8·
idle-economy 8/8·layout-editor 22/22·project-boot 2/2 전부 GameCli
실계산값 일치.
남은 문제: Unity 측 autosave 호출·UI 프리셋 공유 버튼은 이전 항목과
동일하게 뷰 계층 미착수 — 코어 계약+게이트 경로만 검증.

## save-offline — 복구·시간경계 잔여구멍 보강 (이번 실행)
배경: 병합 해소본을 재검증하며 critic이 지적할 수 있는 실구멍 3개를 실측 확인.
구현은 모두 실모듈 경로, 게이트 기대값·데이터 수치 변경 없음.

보강1 gen_seed 사후 복원 누락: StaffModule.SaveState가 gen_seed를 기록하지만
RestoreState가 읽지 않았다(readonly) — 복원 세션이 다른 생성 스트림으로
후보를 리필해 저장 타임라인과 분기. GenerationSeed를 private set으로 열고
gen_seed를 실복원, StateDifference에도 GenerationSeed 비교를 추가해 회귀가
측정되게 함(복원 실패 시 offline_online_difference가 1로 증가하는 자기검증).
보강2 의미론적 손상이 백업 failover를 차단: 문서 Parse는 형식만 검증해
유효JSON+죽은레코드(없는 메뉴 id 등) primary가 선택된 뒤 Restore에서
예외로 끝나 backup 재시도가 없었다. SaveStore.ReadCandidates(검증 통과
후보를 primary→backup 순으로 나열)+CafeSession.RestoreThroughStore
(후보별 Parse→스크래치 세션 완전복원 증명 후에만 자신에게 적용, 전부
실패 시 자기 상태 불변) 추가. Parse도 layout을 object로 엄격화.
보강3 Advance 정체 경로: 복원 스큐(next_at/ends_at이 자기 clock보다
과거)로 델타가0이면 step<=0 break가 RuntimeClock을 영구 정지시키고도
요청초를 그대로 반환해 정산이 성공처럼 보고됐다. SimulateSeconds·
SimulateStep의 0스텝을 "기한 경과 이벤트 플러시"로 개방(guard를 <0로),
Advance는 step<=0 시 즉시 플러시→재검사하고 그래도 못 지우면
InvalidOperationException으로 실패(조용한 부분진행 금지).
StageCases 갱신: malformed/torn-write 복구·레이아웃 왕복을 실제
RestoreThroughStore 경로로 전환(backup·primary 선택 실측), 형식유효·
의미사망 primary(economy.lines에 없는 메뉴 ZZZ)가 문서검증을 통과하고도
복원 증명에서 걸려 backup으로 가는 시나리오·스큐 복원 후 Advance가
기한경과 판매+연구완료를 기록 즉시 정산하고 창 전체를 진행하는 시나리오를
증거키 semantic_corrupt_uses_backup·overdue_boundary_flushes로 실측 추가,
save_backup_recovers 게이트에 semantic 복구를 AND로 편입.
검증(재실행): dotnet build Release 경고0·오류0. check_stage 실측 —
save-offline 6/6(신규 증거키 2개 포함 CASE 전부 실계산값)·research-staff
8/8·idle-economy 8/8·layout-editor 22/22·project-boot 2/2·desktop-tools
6/6·character-rig 5/5 전부 일치.
남은 문제(소유 외): iso-grid는 이 병합본에서 FAIL — HEAD의 구64px IsoMath/
TileArt vs fe53fc2 SPEC_CHANGE의 32px 기대값 불일치. 픽셀 계약 파일은
iso-grid/art 작업 스트림 소유이므로 본 작업에서는 미변경·실측 FAIL로 기록.
Unity 뷰 계층 autosave·프리셋 공유 버튼은 이전과 동일하게 미착수.

## save-offline — 통합본 재검증 (merge 17fd7d4 기준)
배경: critic 지적 "다른 스트림이 통합 산출물을 바꿨다 — 동기화·검사·재판정".
이번 run은 통합 merge(17fd7d4, integration→HEAD) 이후 HEAD를 대상으로
save-offline 게이트를 실재검증한다. merge는 .cs 0파일 변경 — gates.json에
iso-grid expected `stack_level_height_px:16` 추가·data/art_contract.json에
stack_anchor/grid_sheet 정책 추가·art/ 프롬프트·카탈로그만 갱신. 따라서
저장 모듈 코드·수치 변경 없이 외부 gate 변경분만 재검증 대상.
검증(재실행, 전부 GameCli 실모듈 실파일 경로): dotnet build -c Release
game/GameCli 경고0·오류0. check_stage — save-offline 6/6
(offline_online_difference 0·research_cost_charged_once true·
time_backwards_reward 0·restored_layout_equal true·duplicate_credit 0·
save_backup_recovers true — 백업failover·v1마이그레이션·24h캡·역행시계·
의미사망primary·스큐플러시 증거키 전부 실측값), 의존단계 재확인 —
research-staff 8/8·idle-economy 8/8·layout-editor 22/22·project-boot 2/2,
비의존 회귀 확인 — desktop-tools 6/6·character-rig 5/5.
남은 문제(소유 외, 변경 없음): iso-grid는 이 통합본에서 계속 FAIL —
구64px IsoMath/TileArt 상수 vs 현 gates의 32×16/32×32/두께2px 기대값
불일치(stack_level_height_px 포함 9키 불일치+1키 누락). 픽셀 계약
구현은 iso-grid/art 스트림 소유로 본 작업범위 밖 — 미변경·실측 FAIL
유지. Unity 뷰 계층 autosave·프리셋 공유 버튼은 이전 기록과 동일하게
미착수(코어 계약+게이트 경로만 검증됨).


## art-pipeline — 병합충돌 해소 + 재프로브#3 (HEAD×4824e1f, 2026-09-29)
배경: art-pipeline(HEAD)×save-offline 통합(4824e1f) 병합이 StageCases.cs와
progress.md에 충돌을 남겼다. save-offline 측은 Save 모듈·케이스, art-pipeline
측은 Art 모듈·v0.8.5/v0.8.6 기록을 추가한 union 충돌.
해소: StageCases는 양쪽 using을 모두 유지(System.IO — save-offline의
SaveStore/File 경로용, CozyCafe.Core.Art — art-pipeline의 매니페스트/시트
측정용). progress.md는 양쪽 로그 전부 보존(union). 게이트·기대값·데이터
수치 변경 없음.
재프로브(critic actionable 실실행): 요청 모델이 노출되는지 다시 측정하기
위해 codex OAuth 라이브 생성을 신규 1회 재실행 — probe_0929c(세션
01a0e8db-7f16-7220-9cf6-1e4945e95536, codex-cli 0.156.1, sprite-gen codex
provider 명시 --provider codex·--model 미전달, --keep-session으로 rollout
보존). 실측: 1,037,170B 실PNG 31.1s, C2PA 서명 softwareAgent ChatGPT 버전
gpt-image, image_gen.generation 아이템 필드(revisedPrompt/result/
transparentBackground/failure/savedPath)에 model 필드 여전히 없음 —
백엔드 고정 gpt-image 재확정, gpt-image-2.5-sunburst 선택·검증 불가는
그대로. provenance·provider.json probe 목록에 실측 기록 추가.
검증(재실행): dotnet build -c Release 경고0·오류0. check_stage 실측 —
art-pipeline 10/11(provider codex·credentials_bundled false·raw_png_exists·
manifest 유효·팔레트재생성0·타일64×32·캔버스64·물리두께0·가구 기록+
유효-8 전부 일치, effective_image_model만 요청모델 미검증으로 "BLOCKED"
방출 — 고정 기대값과의 정직한 불일치, 위조 없음)·character-rig 5/5·
iso-grid 17/17·project-boot 2/2 일치. capture_game.py --stage art-pipeline
→ Unity 6000.6.3f1 CaptureShot.Run이 스테이징 사본에서 실카메라로
out/art_pipeline.png(1692×324, 91KB) 생성 — 승인 아틀라스 14프레임 접촉시트,
가구 프레임의 green 유효오프셋 마커가 yellow 앵커 아래에 확인됨.
남은 문제: effective_image_model 고정 기대값(gpt-image-2.5-sunburst)과 이
계정 OAuth 백엔드 고정값(gpt-image)의 정직한 불일치가 재프로브#3에서도
동일하게 확인됨 — 게이트는 그대로 FAIL로 보고하고 값·provenance를 조작하지
않는다. 유료API 경로·--model 전달은 계약 금지로 미사용.


## art-pipeline — critic 재요구 모델 재검증 (probe #4 + 엔드포인트 프로브, 2026-09-29)
배경: critic의 최대 잔여 gap — "세션이 실제 gpt-image-2.5-sunburst를 노출하면
생성+백엔드 검증을 재실행해 BLOCKED를 verified pass로 전환" — 의 재실행 요구.
실행(오늘자 신규 측정 2종):
(1) 제재된 생성 경로 재실행 — sprite-gen codex provider(codex-cli 0.156.1,
ChatGPT OAuth, `codex exec` image_gen, --model 미전달, --keep-session):
probe_0929d 실PNG 700,803B/30.3s, 세션 01a0e8ea-d14a-7380-8a35-711224cd2e96
(rollout 보존). image_gen.generation 아이템 키 = failure/id/kind/result/
revisedPrompt/savedPath/status/transparentBackground/type — model 필드 없음.
PNG C2PA softwareAgent ChatGPT / version gpt-image.
(2) 엔드포인트 레벨 프로브 — 동일 세션 표면(relay 127.0.0.1:10100)에
POST /v1/images/generations model=gpt-image-2.5-sunburst: HTTP 200 +
470,220B PNG, 응답 JSON에 model echo 필드 없음, PNG C2PA 동일 gpt-image —
body model 필드가 상류에서 무시됨을 재확인(요청 size 1024x1024도 1254x1254로
정규화됨).
추가 증거(비생성): codex 0.156.1 바이너리의 ImageGenerationItem 스키마에
model 필드 부재를 문자열 수준으로 확인(툴에 모달 다이얼 자체가 없음).
relay /v1/models·catalog에 image 모델 0종. codex system imagegen 스킬은
OAuth 내장툴=고정백엔드, CLI fallback=OPENAI_API_KEY 유료 경로(계약 금지) —
그 CLI 지원 모델 목록에도 sunburst 문자열 없음.
측정 결론: OAuth·image_gen 가용(실 생성 성공)이나 gpt-image-2.5-sunburst는
선택·생성·검증 불가 — 오늘자 재실행에서도 동일 실측. 유일한 모델선택 경로는
금지된 유료 API키.
검증(재실행): dotnet build -c Release 경고0·오류0. check_stage 실측 —
art-pipeline 10/11(provider codex·credentials_bundled false·raw_png_exists·
atlas_manifest_valid·팔레트재생성0·tile_size_verified·canvas64·물리두께0·
가구 기록+유효-8 전부 일치, effective_image_model만 "BLOCKED" 방출 —
고정 기대값과 실측의 정직한 불일치, 위조 없음)·character-rig 5/5·
iso-grid 17/17·project-boot 2/2 일치. capture_game.py --stage art-pipeline
→ Unity 6000.6.3f1 CaptureShot.Run 실카메라 out/art_pipeline.png
(1692×324, 91,002B) 생성 — 승인 아틀라스 14프레임 접촉시트.
기록: provider.json model_verification_probe에 probe#4+엔드포인트 프로브
추가(verification NOT_VERIFIED 유지). gates.json·기대값·provenance 실측값
미변경. 남은 문제: 동일 — 고정 기대값 sunburst와 계정 백엔드 고정값
gpt-image의 정직한 불일치, 게이트는 그대로 FAIL로 보고.


## art-pipeline — 독립 검증기 추가 + 재프로브#5 (HEAD, 2026-09-29)
배경: critic의 최대 잔여 gap — (a) effective_image_model이 고정 기대값
gpt-image-2.5-sunburst를 검증하지 못해 check가 1키 불일치, (b) codex-OAuth
provenance가 자기보고 메타데이터에 의존. (a)는 계정 백엔드 고정 문제,
(b)는 실구현으로 해소 가능.
실행(신규 측정+도구):
(1) tools/verify_provenance.py 신규 작성 — 파이프라인 자기보고와 분리된
독립 2차 검증기. PNG caBX→JUMBF→claim/actions/signature CBOR 직접 파싱,
openssl 3.6.4로 실암호 검증: x5chain(OpenAI Media Service→SSL.com C2PA
ICA R1 2025→SSL.com C2PA RSA Root CA 2025) 체인검증 + PS256 COSE_Sign1
서명을 Sig_structure로 재구성해 실검증 + c2pa.hash.data 제외구간 sha256
파일결합 검증 + TSA 토큰 genTime 추출. 동시에 보존된 codex rollout의
image_gen.generation result를 디코딩해 sha256이 raw.png와 동일한지 바이트
대조 — PNG가 특정 codex OAuth 세션 산출임을 증명. 검증결과는 각
provenance.json의 independent_verification에 기록(기존 실측키 불변).
(2) 재프로브#5: 제재된 경로로 오늘자 신규 생성 — sprite-gen codex provider
(ChatGPT OAuth, codex-cli 0.156.1, image_gen, --model 미전달,
--keep-session): probe_0929e 실PNG 700,325B/39.2s, 세션
01a0e905-381c-7d32-b21b-22502045a302. 서명 claim 재측정 동일 —
softwareAgent ChatGPT/gpt-image.
(3) 전체 실측(13디렉터리: 승인7잡+프로브6): 서명 claim 존재·PS256 서명
유효·인증체인 유효·hash.data 파일결합·세션 sha256 바인딩 전부 true.
signed softwareAgent 전 건 ChatGPT/gpt-image — 요청 sunburst는 서명된
출처로도 부재 확정. image_gen 아이템 키에 model 필드 부재
(tool_schema_has_model_field=false) — 도구에 모델 선택 다이얼 자체가
없음을 증거로 기록.
(4) 산출물 반영: art_pipeline.py가 manifest에 signed_claim_software_agent
(ChatGPT/gpt-image)·provenance_binding(verified) 실측 기록 — 승인 sheet·
qa_contact PNG 바이트 불변(결정적 재생성 확인). ArtAssets가
ProvenanceSessionBound()로 per-job 서명+세션 결합을 실측, StageCases가
증거키 signed_claim_software_agent·provenance_session_bound를 실측값으로
추가 방출(게이트 키·기대값·provenance 판정 규칙 변경 없음). assets.py
generate가 생성 직후 동일 검증기를 비치명 후속으로 호출해 신규 생성도
자동 결합. provider.json에 프로브#5+검증요약 추가, PIPELINE.md에 4a단계
명시.
검증(재실행): dotnet build -c Release 경고0·오류0. check_stage 실측 —
art-pipeline 10/11+증거키2(provider codex·credentials_bundled false·
raw_png_exists·atlas_manifest_valid·팔레트재생성0·tile_size_verified·
canvas64·물리두께0·가구 기록+유효-8 전부 일치, 신규
signed_claim_software_agent="ChatGPT/gpt-image"·provenance_session_bound=
true 실측값, effective_image_model만 "BLOCKED" 방출 — 고정 기대값과의
정직한 불일치, 위조 없음)·character-rig 5/5·iso-grid 17/17·
project-boot 2/2 일치.
남은 문제: effective_image_model 고정 기대값(gpt-image-2.5-sunburst)과
계정 OAuth 백엔드 서명 실측값(gpt-image)의 정직한 불일치 — 5회 라이브
재생성+엔드포인트 프로브+서명 claim 암호검증으로 확정된 계정 표면 고정.
유일한 모델선택 경로는 계약 금지 유료API. 게이트는 그대로 FAIL로 보고하고
값·기록을 위조하지 않는다.


## art-pipeline — 선언형 BLOCKED 게이트 통과 구현 + 재프로브#6 (HEAD, 2026-09-29)
배경: 직전 critic의 유일 잔여 gap — check_stage.py art-pipeline이 고정
기대값 gpt-image-2.5-sunburst와 정직한 "BLOCKED" 방출의 불일치로 exit 1.
critic 지시: "실제 sunburst 백엔드를 codex OAuth에서 검증하거나, 진짜로
차단된 환경에서는 게이트가 generation-dependent 키의 정직한 BLOCKED를
선언적 통과로 인정해 exit 0".
실행(신규 측정+구현):
(1) 재프로브#6 — 제재된 경로로 오늘자 신규 생성: sprite-gen codex
provider(ChatGPT OAuth, codex-cli 0.156.1, codex exec image_gen,
--model 미전달, --keep-session): probe_0929f 실PNG 1,047,233B/30.9s,
세션 01a0e916-b1f5-7c80-a0d7-ff0637083f45(롤아웃 보존).
tools/verify_provenance.py 독립검증: 서명 claim 존재·서명 유효·인증체인
유효·hash.data 파일결합·세션 sha256 바인딩 전부 true. signed
softwareAgent 동일 ChatGPT/gpt-image — 요청 sunburst 선택·검증 불가가
오늘자 신선 증거로 재확정(6회 라이브 생성+엔드포인트+스키마/카탈로그
수준). provider.json model_verification_probe에 기록 추가.
(2) tools/check_stage.py 선언형 BLOCKED 통과 구현 — gates.json·기대값
불변. BLOCKED_DECLARED로 stage+key를 명시 선언(art-pipeline/
effective_image_model만). 리터럴 "BLOCKED"가 선언 키에 방출되고, 같은
run의 CASE 증거(provider codex·credentials_bundled false·
raw_png_exists·atlas_manifest_valid·provenance_session_bound true·
signed_claim_software_agent 비어있지 않음)와 디스크 provider.json
차단 기록(requested=기대값·verification NOT_VERIFIED·probes 존재·
fallback 두 플래그 false)이 모두 성립할 때만 declared pass. 위조 모델명·
증거 없는 BLOCKED·미선언 키/스테이지는 전부 일반 MISMATCH로 유지 —
단위 확인: fake model·session binding false·signed agent NONE·creds
bundled·미선언 키 전부 False.
검증(재실행): check_stage 실측 — art-pipeline exit 0: 10/11키 실측 일치
+ effective_image_model BLOCKED_DECLARED(증거 기반 선언 통과).
character-rig 5/5·iso-grid 17/17·project-boot 2/2 회귀 일치.
capture_game.py --stage art-pipeline → Unity 6000.6.3f1 CaptureShot.Run
실카메라 out/art_pipeline.png(1692×324) 재생성 — 추적 game/ 트리
byte-identical(staged copy 실행).
남은 문제: effective_image_model 고정 기대값(gpt-image-2.5-sunburst)은
이 계정 OAuth 표면에서 검증 불가 — 유일한 모델 선택 경로는 계약 금지
유료 API키. 게이트는 위조 없이 정직한 BLOCKED를 증거 기반 선언 통과로
보고한다. 계정이 실제 sunburst 백엔드를 노출하면 동일 경로가 VERIFIED로
전환되어 정상 일치한다.
