# 05 작업보드

각 작업은 실제 빌드/검사/리뷰가 있어야 DONE. 날짜 약속 없이 의존순서로 진행.

|Task|작업|선행|상태|
|---|---|---|---|
|reference-audit|첨부 원본 분석·샘플 확인|없음|READY|
|project-boot|실행 가능한 게임·테스트 진입점|reference-audit|READY|
|iso-grid|64×32/공간64×64/시각4·탁자의자-8|project-boot|READY|
|layout-editor|가구·타일·문·의자·머신받침|iso-grid|검증완료(리뷰대기)|
|desktop-tools|미니창·메모·할일·타이머·음악|layout-editor|검증완료(리뷰대기)|
|idle-economy|자동판매·강화|project-boot|READY|
|research-staff|연구·메뉴·직원|idle-economy|READY|
|save-offline|저장·복귀·시간경계|research-staff,layout-editor|READY|
|character-rig|소수 파츠·팔레트·랜덤 손님|iso-grid,research-staff|READY|
|art-pipeline|Codex OAuth·sprite-gen 자산화|character-rig|검증완료(게이트10/11·요청모델키 BLOCKED 보고)|
|ui-local-ugc|UI 컴포넌트·로컬 창작툴|desktop-tools,art-pipeline,save-offline|READY|
|integration|무개입 카페 통합|ui-local-ugc|READY|
|native-release|실제 Mac/Windows 빌드·검증|integration|READY|

원본 runner는 운영 오류 시 BLOCKED로 멈춘다. 인증/특정OS 없는 경우 이미 통합된 변경을 보존한 뒤 가능한 독립작업은 별도 세션에서 계속한다. 같은 run의 평가 기준을 낮춰 통과시키지 않는다.

v0.8: iso-grid/layout-editor/art-pipeline/integration의 변경된 수용 기준으로 다시 검사한다. 이전 PASS를 승계하지 않는다.

v0.8.2 run 메모: character-rig 실구현 완료(Core/Character·StageCases 케이스·4방향 장면·실스폰 근거) 및 iso-grid를 SPEC_CHANGE 상수(64×32/64×64/피치32·16)로 재검증 — 상세는 progress.md "character-rig"·"iso-grid v0.8 신규격" 절. Unity 캡처는 6000.6.3f1 에디터 실카메라로 생성.

v0.8.2 증거 보강: character-rig 가림 증거를 전 4방향 실측(SW/SE18px·NW/NE0px)으로, 위상·팔레트 불변 스윕을 계약 전 상태(idle/walk/sit/work)로 확장, variant_png_assets을 배송 자산 실스캔으로 전환, 캐릭터 시트 캡처를 Zoom2(600×400)로 상향, capture_game.py Windows형 절대경로 수용 보강.

v0.8.4 art-pipeline 산출물 배송화: art/generated/ 언 ignore → 7잡 raw·provenance·report가 커밋 트리에 포함되어 평가 체크아웃에서 raw_png_exists=true·effective_image_model이 실측값(gpt-image)으로 보고됨(BLOCKED 아님). assets.py codex 경로 해석을 msys/cygwin python에 대응. 오늘자 codex OAuth 라이브 재생성(probe_live, 35s)으로 image_gen 가용·백엔드 gpt-image 고정 재확인 — sunburst 불가는 honest MISMATCH로 유지. character-rig·iso-grid·project-boot 회귀 전부 일치, Unity 실카메라 캡처 재생성.

v0.8.3 art-pipeline 실구현: tools/assets.py+sprite-gen(b725baa) 경유 Codex OAuth로 7잡 실생성, tools/art_pipeline.py 추출·QA·승인, art/approved/(sheet+manifest+qa_contact) 실산출물, StageCases/StageScenes/SceneRenderer/PngReader·ArtAssets 코어 배선. 게이트 10/11 일치·effective_image_model만 실측 gpt-image(요청 sunburst 미검증, honest MISMATCH). Unity CaptureShot 실카메라 캡처 out/art_pipeline.png.

v0.8.2 병합 해소: character-rig(c1f9a1d)×layout-editor 충돌 7파일 해소 —
양쪽 케이스·장면·렌더 경로 공존, IsoContract·TileArt는 현 규격 측 유지.
재검증: build 경고0·layout-editor 22/22·iso-grid 16/16·project-boot 2/2·
idle-economy 8/8·research-staff 8/8·character-rig 5/5, Unity 스테이징
사본 캡처로 out/layout_editor.png 생성·추적소스 변경 0.
