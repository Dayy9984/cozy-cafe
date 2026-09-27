# 실제 첨부 자료 확인 기록 — 2026-09-27
분할 조각을 순서대로 읽는 seekable stream으로 ZipFile 중앙목록을 열었다. 총16,893개 항목,
압축해제 합계1,471,577,440byte. 목록상 PNG14,288·CS1,929·JSON412·WAV92·TTF8.
전체 파일의CRC를 전수 확인한 것은 아니다. 선택해 읽은43개 항목은 zipfile 읽기 CRC검사를 거쳤다.

## 직접 본 내용
- 원본 README/REPORT는 Mini Cozy Room Demo1.04.00, Unity6000.0.51f1라고 서술한다. 별도 게임 실행 확인은 안 함.
- PlayerSkinHandler.GetEquippedSkinKey는 Body/Hair/Eyes/Mouth/Hat/Top/Bottom/Shoes/Accessory1/2를 구분한다.
  초기 비어 있는 프리셋 목록에서3개를 구성하는 코드가 있다. 우리3프리셋안은 재사용 전략이지 원작 그대로 이식 아님.
- EGameMode는 None/Max/Main/MiniMode. UI_Minimode·MiniModeTrayModule 및 메모/할일 managers의
  Main/MiniMode 전환 분기가 있다. 창 API의 실제 Mac 동작은 여기서 보증되지 않는다.
- DecoSkinInfoFields에는 ImageTopPath/ImageBottomPath가 별도로 있다. 이것만으로 임의 1×1 편집/경로 규칙을 증명하지 않는다.
- MemoManager/TodoListManager에서 열림상태·캐시·미니모드 전환 관련 처리가 읽힌다.

## 이번 패키지의 사용
source_samples와 reference_contact.png는 이 사용자에게 전달하는 분석용 발췌이며 game/으로 복사 금지.
샘플 치수는 sample_manifest. 64×32/4는 사용자 지정한 우리 규격이지 원작에서 측정된 타일 규격이 아니다.
전체 음악·폰트·실행파일·원작 배포본은 포함하지 않음. UI 영상/애니메이션 사용성을 실제 실행과 비교하는 시험은 NOT_RUN.
