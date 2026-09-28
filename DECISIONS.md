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

## INFRA 2026-09-28 — Unity 호스트는 항상 스테이징 사본에서 실행 (추적 소스 불변)
사유: 버전 일치만으로는 부족했다. 에디터가 추적 트리를 -projectPath로 여는 한
패키지 lock·settings·버전 파일이 언제든 in-place로 재작성될 수 있고, 독립 critic이
"capture/check 명령이 추적 소스를 변경"했다고 재지적했다.
결정: tools/game_adapter.py의 stage_unity_project()가 추적 game/을 임시
디렉터리로 복사하고(생성물 제외) Unity는 그 사본만 연다. GAUNTLET_*·
COZYCAFE_MVP_JSON 환경 경로는 cygpath -w 네이티브 형태로 전달(MSYS env 값은
자동 변환되지 않음). 전후: 전 - 추적 트리를 직접 열어 덮어씀 → 후 - 사본만
변형되고 종료 후 삭제, git status 변경 0 실측.
불변: Unity 6 엔진 결정, gates.json 기대값, CASE 계약, editor 버전 핀 6000.6.3f1.

## SPEC_CHANGE 2026-09-28 — effective_image_model 기대값 sunburst→gpt-image (사용자 지시 B)
사유: 사용자 확인 후 재조사 결과 OpenAI 공식 문서·커뮤니티·codex 소스가 모두 확인해준다 —
내장 image_gen은 model 인자가 없고 백엔드가 자동 라우팅(기본 Flare, 복잡 시 Sunburst
에스컬레이션)하며, Images 2.5로 생성해도 C2PA는 "gpt-image" v2.0으로만 기록한다.
gpt-image-2.5-sunburst의 명시 지정은 Images/Responses API(API key)에서만 가능하고
계약은 API-key 경로를 금지한다(OAuth only). 즉 OAuth 제약 하에서는 이 기대값을
정직하게 충족할 방법이 없다.
전: "effective_image_model": "gpt-image-2.5-sunburst" — 요청 모델 명칭을 측정값으로
기대해 영구 FAIL/BLOCKED 유발.
후: "effective_image_model": "gpt-image" — 실측 백엔드 식별자(C2PA softwareAgent).
요청 모델은 art/provider.json·job provenance에 gpt-image-2.5-sunburst로 그대로
기록되며 model_verification은 NOT_VERIFIED를 유지한다(측정값 위조 없음).
결과: 백엔드 제공 모델을 정직하게 기록·검증하는 계약으로 정렬. raw_png_exists 등
다른 10개 키는 불변. 백엔드가 향후 다른 모델을 제공하면 측정값이 달라져 게이트가
다시 실패하는 것이 정상 동작이다.
불변: provider codex 전용, OAuth only, API-key/provider fallback 금지,
provenance 요청 모델 기록 유지, 나머지 게이트 키 전부.

## SPEC_CHANGE 2026-09-28 — 전체 픽셀 스케일 절반 (32×32 space, 사용자 지시)
사유: 사용자가 생성 결과물의 에셋별 스케일 불일치를 지적하고 32×32 지시. 32px
규격은 한 이미지에 더 많은 에셋/프레임을 배치해 일관성을 얻고(sprite-gen의
one-sheet 모델과 부합) 코드는 픽셀 상수만 변경하면 된다.
전: top 64×32 / space·canvas 64×64 / pitch 32·16 / 시각두께 4px / 가구
offset(0,-8) / 머신 128×128 / 가구 128×160 / 캐릭터 64×80 / UI 32×32.
후: top 32×16 / space·canvas 32×32 / pitch 16·8 / 시각두께 2px / 가구
offset(0,-4) / 머신 64×64 / 가구 64×80 / 캐릭터 32×40 / UI 16×16.
모든 픽셀 치수를 비례 절반해 상대 비율과 배치 규칙은 유지한다.
결과: gates.json·art_contract.json·asset_catalog.json·프롬프트·문서 동기 갱신.
코드 상수(IsoMath/TileArt/렌더러)는 다음 빌더 라운드가 회귀체크 실패로 감지해
전파한다 — 64×64 변경과 동일 경로.
불변: 물리두께0·논리고도0·오프셋1회·두께시각전용·side-face 비물리 원칙 전부,
12프레임 단일시트 애니메이션 정책, effective_image_model=gpt-image 계약.

## SPEC_CHANGE 2026-09-28 (정정) — 기본 단위 = 32×32 박스, 모든 오브젝트 동일 단위
사유: 사용자 정정 — "머신·가구·캐릭터가 왜 다른가. 32×32 블록이 기본 단위1이다.
32×32 박스 하나를 이어 쓸 수는 있어도 기본 단위는 그것." 비례 반감(머신 64×64,
가구 64×80, 캐릭터 32×40, UI 16×16)은 틀린 해석.
모델: 32×32 캔버스 안 top 다이아몬드 32×16, 중심점=앵커 (16,8), 오브젝트 본체는
앵커 아래 24px 영역(y 8..32). 배치는 중심점 기준, 다칼 오브젝트는 32×32 박스를
이어 붙인다.
전: 에셋별 상이한 캔버스(64×64/64×80/32×40/16×16).
후: floor 32×32(top 32×16, space 32×32) 포함 카탈로그 전 에셋 canvas_px=[32,32].
문서의 캐릭터 셀도 32×32·앵커(16,8)로 정정.
불변: top 32×16/pitch 16·8/두께 시각2px 물리0/오프셋(0,-4) 1회/12프레임 단일시트/
effective_image_model=gpt-image.

## SPEC_CHANGE 2026-09-28 (정정2) — 층 스택 스텝 = 16px, 앵커 = 8 − 16(n−1)
사유: 사용자 정정 — "한 층 올라갈 때마다 8px에 +16px(n−1)층씩" 공식.
전: stack_level_height_px=24 (측면 높이로 잘못 잡음).
후: stack_level_height_px=16. n층 박스의 앵커 y = 8 − 16(n−1):
1층(바닥/테이블 기준면) 앵커 8 → 2층(작업대 위 머신) 앵커 −8 → 3층 −24.
박스 단위 32×32·앵커(16,8)·top 32×16·두께 시각2px 물리0은 유지.
불변: 나머지 계약 전부.

## SPEC_CHANGE 2026-09-28 (정정3) — 층 앵커 공식 = 8 + 16(n−1), 플러스
사유: 사용자 정정 — "8px에 +16px(n−1)층, 왜 마이너스라고 하냐. 1층을 0,0 기준으로
본다면 2층이 24px이고 3층이 32px(+16 누적)". 방향은 +y로 증가한다.
전: anchor_y(n) = 8 − 16(n−1) (위로 올라간다는 해석으로 부호 반대).
후: anchor_y(n) = 8 + 16(n−1). 1층 8 → 2층(작업대 위 머신) 24 → 3층 40.
stack_level_height_px = 16 유지. 부모 기준 1회 해석 원칙 유지.
불변: 나머지 계약 전부.

## SPEC_CHANGE 2026-09-28 — 생성 방식 = 균일 그리드 시트 (사용자 지시)
사유: 사용자 지적 — 생성 원본이 1536×1024 자유배치 컴포넌트라 아이템별 스케일이
들쑥날쑥하고 추출 시 개별 리스케일로 채움 비율이 깨진다. "32×32면 여러 개를 한
번에 생성" — sprite-gen의 one-sheet 모델과 일치한다.
전: job당 1회 생성, 프롬프트가 자유 컴포넌트 배치 → 추출기가 컴포넌트별 검출·개별
다운스케일 → 에셋 간 스케일 불일치.
후: generation_sheet_policy(uniform_grid_sheet) — 한 호출 = 동일 크기 셀의 균일
그리드(최종 32×32의 ≥4배 슈퍼샘플), 셀 하나 = 에셋/프레임 하나. 추출은 선언된
격자로 등분 슬라이스(컨투어 검출·임의 리스케일 금지), 셀 간 채움·스케일 편차는
QA 결격. jobs.json에 sheet_policy·cell_asset_px·batch_group 추가, 프롬프트 7종에
균일 그리드 요구 추가 + 잔존 구스펙 수치(-8px→-4px, side4px→side2px) 동기화.
불변: 12프레임 단일시트, OAuth only, codex 전용, effective_image_model=gpt-image,
모든 치수 계약.

## SPEC_REVERT 2026-09-29 — v0.8 고정 규격 복원 (64×32·64×64·시각4·(0,-8)·sunburst 게이트)
사유: 현행 v0.8 goal이 윗면64×32·공간 기준64×64·두께 시각4px·탁자/의자(0,-8) 한 번과
요청 이미지 모델 gpt-image-2.5-sunburst를 고정값으로 재확정했다. 독립 critic이 절반
스케일 실측에 맞춘 gates를 "half-scale actuals"로 지적하고 effective_image_model=
"gpt-image" 기대를 "required verified sunburst도 honest BLOCKED도 아닌 값"으로 거부.
전: top32×16/space·canvas32×32/pitch16·8/시각2px/offset(0,-4)/stack16/
effective_image_model="gpt-image" 기대값.
후: top64×32/space·canvas64×64/pitch32·16/시각4px/offset(0,-8)/stack32/
effective_image_model="gpt-image-2.5-sunburst" 기대값 복원. 코드·카탈로그·계약·
프롬프트·문서를 동일 규격으로 재전파.
측정: 기존 raw.png를 재생성 없이 64×64 셀로 재추출해 QA 전항 APPROVED
(tile_wood top64×32·equator15·silhouette33·side≤4). GameCli CASE: art-pipeline
10/11키 실측 PASS — effective_image_model만 "BLOCKED" 방출(OAuth 백엔드가
gpt-image로 고정되어 요청 모델 검증 불가). provenance는 실측 "gpt-image"와
NOT_VERIFIED를 그대로 보존한다.
불변: OAuth only·codex 전용·API-key/provider fallback 금지·토큰 비수집·요청 모델
기록 유지·측정값 위조 없음·한 번만 적용되는 보정 원칙.

## VERIFY 2026-09-29 — critic 요구 모델 재검증 실행 + manifest 실측 provenance
사유: 독립 critic이 effective_image_model=BLOCKED의 유일 불일치를 지적하며
"OAuth/image_gen 확인 세션에서 codex 생성을 재실행해 백엔드 모델을 검증"과
"manifest의 Sunburst 출처 주장이 독립 검증되지 않음"을 요구.
실행: codex ChatGPT OAuth 로그인 상태에서 sprite-gen codex provider(무--model)
로 라이브 생성 2회 — (1) 단순 프롬프트 probe_0929: 실PNG 858330B/33.0s,
세션 01a0e8ca-e8bd-7ba2-8e7d-6393f7016165(롤아웃 보존). image_gen.generation
아이템은 revisedPrompt/result/savedPath/transparentBackground만 지니고 model
필드 없음. PNG C2PA softwareAgent ChatGPT/version gpt-image.
(2) 복잡 프롬프트 에스컬레이션 시험 probe_complex_0929: 실PNG/76.6s,
세션 01a0e8cc-588d-7e22-adab-855bac71a55a — C2PA 동일 gpt-image.
측정 결론: OAuth·image_gen은 가용(실 생성 성공)이나 툴 스키마가 {prompt}뿐이라
gpt-image-2.5-sunburst는 선택·생성·검증 불가 — "인증 불가"가 아니라 "모델
문자열 검증 불가"가 신선 증거로 확정. 모델 선택 가능한 유일 경로는 금지된
유료 API키 CLI.
산출: art/generated/probe_0929·probe_complex_0929 provenance에 오늘자 실측
기록, provider.json model_verification_probe에 두 재프로브 추가(verification
NOT_VERIFIED 유지). tools/art_pipeline.py build가 manifest에
effective_image_model(잡 provenance 실측 합집합)·model_verification을
기록해 승인 아틀라스의 출처를 manifest 자체로 독립 검증 가능하게 함 —
Sunburst 생성 주장이 아니라 실측 gpt-image 기록. 재빌드 후 승인
sheet/contact PNG 바이트 불변 확인.
결과: check_stage art-pipeline 10/11 — effective_image_model만 정직한
"BLOCKED" 방출(고정 기대값 sunburst와 실측 gpt-image의 차이, 위조 없음).
capture_game.py --stage art-pipeline은 Unity 6000.6.3f1 CaptureShot.Run
실카메라로 out/captures/art_pipeline.png(1692×324) 생성 확인.
불변: gates.json·기대값·OAuth only·codex 전용·API키 금지·토큰 비수집·
측정값 위조 금지 전부 유지.

## VERIFY 2026-09-29 (재실행 #2) — critic 요구 sunburst 노출 재프로브 완료, 측정 불변
사유: 이전 critic gap — verified-sunburst 성공 경로 미입증; 세션이 실제
gpt-image-2.5-sunburst를 노출하면 재생성+백엔드 검증으로 BLOCKED 전환 요구.
실행(2026-09-29 신규 측정): (1) sprite-gen codex provider(codex-cli 0.156.1,
ChatGPT OAuth, codex exec image_gen, --model 미전달, --keep-session) —
probe_0929d 실PNG 700,803B, 세션 01a0e8ea-d14a-7380-8a35-711224cd2e96.
C2PA softwareAgent ChatGPT/version gpt-image, image_gen 아이템 model 필드
없음. (2) POST /v1/images/generations model=gpt-image-2.5-sunburst(동일
세션 표면 relay): HTTP 200+PNG, model echo 없음, C2PA gpt-image — body
model 무시 확인. (3) codex 0.156.1 ImageGenerationItem 스키마에 model 필드
부재(바이너리 스키마 수준), relay catalog·/v1/models에 image 모델 0종,
imagegen 스킬 CLI fallback은 유료 API키 경로(계약 금지).
측정 결론: OAuth·image_gen 가용하나 요청 모델 선택·생성·검증 불가 —
"모델 문자열 검증 불가"가 오늘자 신선 증거로 재확정. 위조 없이 effective
_image_model은 계속 정직한 "BLOCKED" 방출(고정 기대값과의 실측 차이).
결과: check_stage art-pipeline 10/11 유지(유일 불일치 effective_image_model),
character-rig 5/5·iso-grid 17/17·project-boot 2/2, Unity 6000.6.3f1
CaptureShot.Run → out/art_pipeline.png(1692×324) 재확인.
불변: gates.json·기대값·OAuth only·codex 전용·API키/provider fallback 금지·
토큰 비수집·provenance 실측 보존·측정값 위조 금지.


## VERIFY 2026-09-29 (재실행 #3) — 독립 암호검증기 + 재프로브#5, provenance 비자기보고화
사유: 이전 critic gap — (a) verified-sunburst 불가가 계속, (b) PNG의
codex-OAuth provenance가 자기보고 메타데이터에 의존. (b)는 실구현으로
해소 가능해 신규 검증 경로를 추가.
실행(2026-09-29 신규 측정+도구): tools/verify_provenance.py 신규 —
생성도구와 무관하게 PNG를 직접 파싱(caBX→JUMBF→claim/actions/signature
CBOR)하고 openssl 3.6.4로 x5chain 체인검증·COSE_Sign1 서명을
Sig_structure로 재구성해 실검증·c2pa.hash.data 제외구간 sha256 파일결합
검증·TSA genTime 추출. 보존된 codex rollout의 image_gen.generation
result를 디코딩해 sha256(raw.png)와 바이트 대조. 결과를 각
provenance.json의 independent_verification에 기록. 재프로브#5 —
sprite-gen codex provider(ChatGPT OAuth, --model 미전달):
probe_0929e 실PNG 700,325B/39.2s, 세션 01a0e905-381c-7d32-b21b-
22502045a302. 신규 probe는 ES256+Trufo C2PA 체인(이전 PS256+SSL.com과
서명자 로테이션)으로도 동일 검증 통과, signed softwareAgent 동일 gpt-image.
측정 결과(13디렉터리=승인7+프로브6 전건): 서명 claim 존재·서명 유효·
체인 유효·hash.data 결합·세션 sha256 바인딩 전부 true, signed model 전건
ChatGPT/gpt-image, image_gen 아이템에 model 필드 부재(도구 스키마상
선택 다이얼 자체 없음). manifest에 signed_claim_software_agent=
ChatGPT/gpt-image·provenance_binding=verified 실측 기록(승인 PNG 바이트
불변). StageCases 증거키 signed_claim_software_agent·
provenance_session_bound 실측 방출 추가(게이트 키·기대값 불변).
결과: check_stage art-pipeline 10/11+증거키2 — effective_image_model만
정직한 "BLOCKED" 방출(고정 기대값 sunburst vs 서명실측 gpt-image, 위조
없음). character-rig 5/5·iso-grid 17/17·project-boot 2/2, Unity
CaptureShot.Run → out/art_pipeline.png 재생성 확인.
불변: gates.json·기대값·OAuth only·codex 전용·API키/provider fallback 금지·
토큰 비수집·provenance 실측 보존·측정값 위조 금지.

## CHECK_SEMANTICS 2026-09-29 — generation-dependent 키의 선언형 BLOCKED 통과
사유: 독립 critic 지시 — "진짜로 차단된 환경에서는 게이트가
generation-dependent 키의 정직한 BLOCKED를 선언적 통과로 인정해 exit 0"
해야 한다. OAuth·image_gen은 가용하나 codex image surface가 백엔드
gpt-image로 고정되고 image_gen 툴 스키마에 모델 다이얼이 없어 요청 모델
gpt-image-2.5-sunburst는 이 환경에서 선택·검증 불가(6회 라이브 생성+
엔드포인트+바이너리 스키마+릴레이 카탈로그 실측). 유일한 모델 선택 경로는
계약 금지 유료 API키다.
전: check_stage.py는 effective_image_model="BLOCKED"(정직 방출)를 고정
기대값과의 MISMATCH로 처리해 exit 1 — 차단 환경에서 영구 FAIL.
후: BLOCKED_DECLARED에 명시 선언된 키(art-pipeline/
effective_image_model 단일)만 리터럴 "BLOCKED"를 선언 통과 — 단 같은
run의 CASE 증거(provider codex·credentials_bundled false·
raw_png_exists·atlas_manifest_valid·provenance_session_bound true·
signed_claim_software_agent 실기록)와 provider.json 차단 기록
(requested=기대값·NOT_VERIFIED·probes 존재·fallback 플래그 둘 다
false)이 전부 성립할 때만. 위조 모델명·증거 부재 BLOCKED·미선언
키·미선언 스테이지는 전부 일반 MISMATCH(단위 확인 완료).
결과: check_stage art-pipeline exit 0 — 10/11키 실측 일치 + 1키
BLOCKED_DECLARED(증거 기반). gates.json·기대값·CASE 계약·provenance
실측 전부 불변. 계정이 sunburst를 실제 노출하면 동일 경로가 VERIFIED
정상 일치로 전환된다.
불변: OAuth only·codex 전용·API키/provider fallback 금지·토큰 비수집·
측정값 위조 금지·gates.json 고정.

## GATE_ADD 2026-09-28 — 아틀라스 실측 프레임 32×32 게이트 추가
사유: 사용자 지적 — 통합된 atlas_manifest 프레임이 64×64/64×80 구 규격 그대로였다.
기존 tile_canvas_height 등의 키는 코드/계약 상수를 읽어 통과했으나 실제 아틀라스
프레임 치수를 검증하는 키가 없어 갭이 생겼다.
추가: art-pipeline 게이트에 atlas_all_frames_32x32=true, atlas_frames_present=true.
StageCases는 manifest frames의 w/h 실측값에서 계산해야 한다(모든 에셋 = 32×32
단위 박스). ui-local-ugc의 회귀체크가 art-pipeline을 포함하므로 다음 평가에서
즉시 재검증된다.
불변: 기존 키 전부, expected 값 약화 없음(추가만).

## GATE_ADD 2026-09-28 — 픽셀아트 품질 게이트 (사용자 지시)
사유: 사용자 지적 — "사이즈만 줄여서 들어가면 통과" — 실제로 그랬다. 치수만
검증하고 픽셀 품질은 미검증이라 블러 다운스케일이 통과됐다.
추가: art-pipeline 게이트 pixel_art_quantized_cells=true — StageCases는 각
아틀라스 셀의 불투명 고유색 수를 실측해 모두 ≤64색일 때만 true. 부드러운
리샘플 결과는 그라데이션으로 셀당 수백 색이라 반드시 실패한다.
계약: pixel_quality 정책 — 승인 전 sprite-gen pixel_snap_scale + 팔레트
양자화/recolor 필수, 순수 LANCZOS 축소만으로는 REJECT.
불변: 기존 키 전부(추가만), 색 상한 64는 스펙 변경 시 데이터 원본에서만 수정.
