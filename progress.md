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
