# 개발 에이전트 지침
사용자가 개발 진행을 요청했다. 또 다른 기획 제안이나 ZIP 생성 스크립트만 반환하지 않는다.
읽기: START_HERE→PLAN→project/planning 01~05→project/DEVELOPMENT→data/art.
우선순위: 최신 사용자 확정 > 현재 문서 > MVP 제안값 > legacy v0.4. 미정값은 제공 기본값으로 진행한다.

## 원본 실행기
사용자 ZIP은 eclipse1228 명의 MIT Devin Gauntlet Runner v0.1.0이다. 원본은 vendor/gauntlet에 그대로 있다.
이번 패키지의 Codex adapter와 계획 로더는 별도 추가 코드다. 공식 OpenAI/Cognition 제품이라고 하지 않는다.
원본 실행기는 Git checkout·실제 검사·독립 critic·중단/재개를 관리한다. /loop 프롬프트만 재생하는 도구가 아니다.
Codex adapter는 새 프로세스, builder workspace-write / critic read-only를 사용한다.
OS sandbox가 계정 전체의 읽기까지 격리한다고 보장하지 않는다. 전용 개발환경에서 실행한다.
검토자가 실제 파일을 못 열면 UNJUDGEABLE이다. 미실행=NOT_RUN, 자료 불가=BLOCKED.

## 변경 제한
판정 기준·expected 값·금지 시스템을 약하게 바꿔 PASS를 만들지 않는다. 체크스크립트와 고정 기준은 하네스 소유다.
배치/도구/시간/경제를 분리하고 새로운 경영 시스템을 추가하지 않는다.
원작 자료는 읽기 참고만. 원본 폰트·음원·이미지·코드가 game/art/build에 들어가지 않는다.
로그인 토큰을 수집·복사·출력하지 않는다. 유료 API/다른 이미지 provider로 조용히 바꾸지 않는다.
독립 agent를 실제 못 띄웠으면 자기검토를 독립검토라고 부르지 않는다.
외부 프로세스/클라우드를 무단으로 분리 실행하지 않는다. 감독 아래 이미지 제작 CLI의 동기 호출은 허용한다.
수치는 데이터 원본에서만 바꾸고 변경 사유·전후 결과를 결정 기록에 남긴다.

## v0.8 필수 정렬 규칙
64×31 top / 64×62 space. 4px는 visual-only, physics height0. 탁자·의자 effective render offset(0,-8) once.
logical root/collider/pathfinding/경제는 변하지 않는다. 캔버스에 두께를 추가하거나 z로 쓰지 않는다.
원화 baked_offset과 runtime_offset을 합한 결과만 -8이어야 한다. 회전 후에도 화면 위 방향, 줌 전 보정이다.
현재 피치32/15.5는 구현 기본값으로 명시되어 있다. 사용자 확정 피치나 원작 실측으로 오인하지 마라.
이전 판의 PASS/PLAN_READY를 현재 실행 검증으로 가져오지 않는다. 변경된 외부 gates로 재검증한다.
