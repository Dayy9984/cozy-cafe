# references/mini_cozy 감사 기록 — v0.8 (2026-09-27)

## 범위·방법
- 대상: `references/mini_cozy/sample_manifest.json`의 전 항목과 `game/`·`art/`·`data/`·`native/` 배송 디렉터리.
- 방법1(무결성): 매니페스트 각 `path`를 열어 sha256을 재계산, 기록값과 비교. PNG 항목은 IHDR의 실제 폭·높이를 `image_size`와 대조.
- 방법2(누출): `source_samples/` 전 파일의 sha256 집합을 만들고 배송 디렉터리 전 파일을 순회하며 byte-identical 여부를 검사.
- 방법3(금지 자산): 배송 디렉터리에서 ttf/otf/woff 계열 폰트 파일 존재 여부 검사.
- 실행 환경: 이 작업본의 python3(3.12), 로컬 파일 직접 읽기. 결과는 재실행으로 재현 가능.

## 결과 요약
- 매니페스트 항목 43개: 전부 존재·sha256 일치. PNG 30개의 `image_size`는 실제 IHDR과 전부 일치.
- `source_samples/` 실제 파일 43개(디렉터리 제외) = 매니페스트 43개. 목록 밖 잔여 파일 없음.
- 배송 디렉터리 파일 16개(game 1·art 11·data 3·native 1) 중 source_samples와 byte-identical인 파일: 0개.
- 배송 디렉터리 내 폰트 파일: 0개.
- 게이트 대응: `reference_samples_intact` 관측값 true, `forbidden_fonts_bundled` 관측값 false. (판정은 하네스 소유)

## 파일별 결과 (sha256 재계산)
| path | bytes | sha256(앞12) | zip CRC읽기 | PNG 실측 | 결과 |
|---|---|---|---|---|---|
| `references/mini_cozy/source_samples/README.md` | 2626 | c4d91a54f60a… | PASS | - | PASS |
| `references/mini_cozy/source_samples/REPORT.md` | 14142 | 5dc48a8b92a0… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/MemoManager.cs` | 16959 | 8647eff27eb2… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/PlayerSkinHandler.cs` | 5638 | 77227d68e77c… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/TodoListManager.cs` | 18687 | 5b9349a09d2e… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/DecoSkinInfoFields.cs` | 550 | a9bcafc065b1… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/EGameMode.cs` | 87 | fe407d558673… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/PcSkinColorGroup.cs` | 267 | 56c328e0dd59… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/PcSkinInfo.cs` | 783 | b6712cb57e86… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/PcSkinType.cs` | 384 | 149e2c658cad… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse/UI_Minimode.cs` | 15934 | ad6aecc242c1… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse.scriptableobjects/SkinColorData.cs` | 227 | 0856d46ecec9… | PASS | - | PASS |
| `references/mini_cozy/source_samples/code/Assembly-CSharp/cozyhouse.tray/MiniModeTrayModule.cs` | 5686 | 16fabf5b1248… | PASS | - | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Decor_General_Floor_Category_Icon.png` | 1131 | e6e6cb175cb3… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Room2_Decor_General_Floor_Category_Icon.png` | 515 | b8af34fbb5ac… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Room3_Decor_General_Floor_Category_Icon.png` | 1196 | 3c2a555a0a11… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Decor_Activity_Chair_Category_Icon.png` | 2641 | bb13f3838a14… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Room2_Decor_Activity_Chair_Category_Icon.png` | 1187 | 4a138a1fbfca… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Room3_Decor_Activity_Chair_Category_Icon.png` | 2148 | 4cf24aced8cf… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_DrawingTablet_24001_0.png` | 117 | 14234a82a7b7… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_DrawingTablet_24001_1.png` | 117 | 14234a82a7b7… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_DrawingTablet_24001_10.png` | 117 | 14234a82a7b7… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_DrawingTablet_24001_11.png` | 117 | 14234a82a7b7… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Body_10001.png` | 204 | 40e15aafff1f… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Body_10002.png` | 202 | 34ff8d5b1dd7… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Body_10003.png` | 208 | 12e302ed8b06… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Body_10004.png` | 207 | ea064cada7fb… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Hair_11001.png` | 573 | a92921c77ffb… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Hair_11002.png` | 581 | 1743b1f46caa… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Hair_11003.png` | 580 | c9642dccd800… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Sleep_Effect_Top_0.png` | 129 | c5452901109e… | PASS | 9x10(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Sleep_Effect_Top_1.png` | 127 | c6beb3488dbe… | PASS | 9x10(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Sleep_Effect_Top_10.png` | 115 | 95c629eaa869… | PASS | 7x8(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Sleep_Effect_Top_11.png` | 155 | 4272d37dca30… | PASS | 14x16(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Bottom_22001.png` | 1176 | 55db5925c1be… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Bottom_22002.png` | 1213 | 578d6f5ea251… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Bottom_22003.png` | 1191 | 433e726d72f8… | PASS | 42x42(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_MiniMode_24001_0.png` | 83 | b0775cba09ea… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Accessory1_MiniMode_24001_1.png` | 83 | b0775cba09ea… | PASS | 32x32(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Decor_Function_Memo_Category_Icon.png` | 2446 | b1441dad6942… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/MiniMode_Player_Button_Memo_Active.png` | 158 | bbd4761404f2… | PASS | 22x22(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/Decor_Function_Todo_Category_Icon.png` | 2150 | deb8adfba354… | PASS | 36x36(manifest match) | PASS |
| `references/mini_cozy/source_samples/assets/sprites/MiniMode_Player_Button_TodoList_Active.png` | 161 | 16973340cc8c… | PASS | 22x22(manifest match) | PASS |

## 사실 vs 가정 (OBSERVATIONS.md 방식)

### 직접 확인(샘플 파일 텍스트/바이트에서 관측)
- **Unity 6000.0.51f1**: `source_samples/README.md` 38행과 `REPORT.md` 15행에 "Unity 버전 6000.0.51f1 / Mono 백엔드"라고 명기된 것을 확인. 단 이것은 원본 분석 문서의 서술을 읽은 것이며, 원본 실행 파일을 띄워 버전을 독립 재확인한 것은 아님(원본 배포본 미포함).
- **파트 슬롯 구조**: `PlayerSkinHandler.GetEquippedSkinKey`(137~151행)가 `Body/Hair/Eyes/Mouth/Hat/Top/Bottom/Shoes/Accessory1/Accessory2` 10개 문자열을 구분하고 `_curCharacterParts`의 동명 필드에 매핑. `PcSkinInfo`는 `PartsCategory`·`GroupKeys`(쉼표/공백 분리 int 목록)·`ColorType`·`Cost`·`HasUpperLayer`·`IsDefaultUnlocked`·`IconPath`를 가짐. `PcSkinColorGroup`은 `SelectableGroup<PcSkinInfo>` 상속, `SkinColorData`는 `(EColorType colorType, Sprite iconColor)` 쌍.
- **MiniMode 관리자들**: `EGameMode`는 `None/Max/Main/MiniMode` 4값. `UI_Minimode`가 `MemoManager.IsActivate`·`TodoListManager.IsActivate`를 구독해 토글하고 `ui_TimerManager`·줌인/아웃·embedded YouTube를 제어. `MiniModeTrayModule : TrayModuleBase`가 트레이 메뉴에 Zoom0~3·미니 위젯 표시/숨김·`PlayerPrefs "StartInMiniMode"`를 다룸. `MemoManager`·`TodoListManager`는 `OnGameModeChanged`에서 `MiniMode` 진입 시 `_beforeMiniModeActive`를 저장하고 `Main` 복귀 시 `IsCloseAllPopupMiniMode` 조건으로 복원.
- **데코 스킨 필드**: `DecoSkinInfoFields`에 `ImageTopPath`/`ImageBottomPath`가 별도 존재.
- **스프라이트 치수**: 매니페스트 `image_size`(36×36 아이콘, 42×42 파츠, 32×32 액세서리 등)는 실제 PNG IHDR과 전부 일치 — 샘플 자체는 정합.

### 관측되지 않음 / 가정으로만 취급
- 원본 게임의 실제 타일 픽셀 규격·격자 피치: 샘플에 타일 원본 없음. 우리 32/15.5 피치는 64×31 윗면에서 유도한 구현 기본값이지 원작 실측 아님.
- `IsCloseAllPopupMiniMode` 등 설정의 실제 실행 시 UX, 트레이 API의 macOS 동작: 원본 미실행(NOT_RUN), 이식 판단 근거로만 사용.
- 파트 슬롯 10개 체계를 우리 3프리셋 구조에 그대로 대응시키는 것은 재사용 "전략"이며 원작 구조 그대로의 이식이 아님.
- `ImageTopPath`/`ImageBottomPath` 존재만으로 원작의 임의 1×1 편집·경로 규칙을 증명하지 않음.

## 배송 경계 확인
`source_samples`와 `reference_contact.png`는 분석 전용 발췌. 이번 검사에서 game/art/data/native 어느 파일도 참조 샘플과 바이트 동일하지 않았고 폰트도 없었다. 향후 아트 산출물이 들어오면 동일 스캔을 다시 돌려야 한다.
