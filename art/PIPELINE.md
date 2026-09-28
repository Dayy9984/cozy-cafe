# 이미지·스프라이트·UI 제작 실행 계약
요청: project/art/jobs.json. 실제 실행: packet의 tools/assets.py.
1. python tools/assets.py install --allow-network : upstream Git clone→전용venv→pip -e→commit 기록.
2. codex login / codex login status : 사용자가 직접 로그인. 패키지는 토큰을 열거나 저장하지 않음.
3. python tools/assets.py generate tile_wood --allow-generation : 명시 codex provider로 raw 생성.
4. raw+report 존재/PNG 검증. 모델은 요청값과 실제 확인값을 분리. main Codex 모델을 Sunburst로 바꾸지 않음.
4a. python tools/verify_provenance.py : 독립 2차 검증. 서명된 C2PA claim을 직접 파싱(openssl로 PS256 서명·인증체인·hash.data 파일결합 검증)하고 보존된 codex rollout의 image_gen 결과 sha256과 raw.png를 바이트 대조해 provenance.json의 independent_verification에 기록한다.
5. 로컬 설치 SKILL/docs/gen을 읽고 승인 idle anchor→state rows→extract→compose 경로 실행.
6. geometry/alpha/palette/rig QA와 curation 후 approved 에만 채택. 생성성공=게임자산승인 아님.

sprite-gen은 외부 공개도구로 첫 설치에는 네트워크 필요. 설치 commit과 SKILL 버전을 lock에 기록하고 임의 업데이트하지 않음.
repo docs의 --model은 설치 코드에서 의미 확인 전 사용 금지. 원하는 이미지모델 gpt-image-2.5-sunburst는
이미지 tool 설정과 prompt에 명시하되 실제 backend metadata 확인 전 VERIFIED로 기록하지 않음.
provider는 --provider codex로 명시해 Grok 자동폴백 방지. API 키 과금으로 자동전환 금지.
OAuth 로그인은 기능 제공을 보증하지 않음. image_gen tool 없는 계정이면 인증 우회가 아니라 생성 BLOCKED.
보유 OAuth credentials를 별도 proxy/게임런타임으로 옮기지 않음. 모델·사용량 등 로그는 비밀값 없이 남김.

character jobs는 몸체 하나의 anchor부터. walk는 실패 가능하므로 프레임격자뿐 아니라 실제 연속 동작으로 판정.
헤어/안경/옷 색조별 전체시트 재생성 금지. project/data의 parts+palette 조합을 구현.
UI jobs는 후보 스타일일 뿐. 글자는 runtime, 컴포넌트는 9-slice/input/focus 테스트 필요.

## v0.8 geometry/placement QA
채택 전에 top64×32, space/canvas64×64, visual side4px를 확인한다. 두께는 물리값이 아니다.
모델이 요청 크기를 정확히 출력했다고 가정하지 않고 추출·manifest 규격을 검사한다.
새 table_square/chair 작업은 미보정 source pivot으로 제작하고 renderer에서 -8px를 적용한다.
이미 보정한 원화는 baked_alignment_offset_px로 기록/정규화한다. metadata 없이 이중 보정하지 않는다.
2×2/3×3 바닥 이음, 뒷선, 외곽만 측면 표시, table/chair ghost와 적용 결과,
4방향 회전, 줌1/2, 저장·복귀에서 동일한 논리 셀/단일 보정을 실제 렌더로 확인한다.
기존 character/body/UI 크기와 팔레트 조합은 유지한다. UI 스킨에 아이소 투영이나 -8px를 적용하지 않는다.
