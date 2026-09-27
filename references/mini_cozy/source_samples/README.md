# Mini Cozy Room – Demo : 언팩(추출) 결과물 안내

이 폴더는 `COZYBUNKAI` (Unity 게임 **Mini Cozy Room – Demo v1.04.00**) 빌드를 분해한 결과입니다.
자세한 분석은 **`REPORT.md`** 를 먼저 보세요.

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `REPORT.md` | 기능·연결관계·에셋 종합 분석 리포트 (한국어) |
| `assets\textures\` | 원본 텍스처 이미지 (PNG, 421개) |
| `assets\sprites\` | 게임에서 쓰는 개별 스프라이트 (PNG, 13,867개) — 캐릭터 헤어/옷/모자/눈, 데코, 펫, UI 아이콘 등 |
| `assets\audio\` | BGM/환경음/알람/SFX (WAV, 92개, 약 1.13 GB) |
| `assets\fonts\` | 폰트 (TTF, 8개) |
| `assets\meshes\` | 메시 (OBJ, 13개) |
| `assets\materials\` | 머티리얼 (JSON, 36개) |
| `assets\animations\` | 애니메이션 클립 / 애니메이터 컨트롤러 / 스프라이트 아틀라스 (JSON, 63개) |
| `assets\textassets\` | CSV/데이터 텍스트 에셋 (6개) |
| `data\scripts\` | MonoBehaviour 직렬화 데이터 (스크립트 클래스별 JSON, 283개) |
| `data\gamemanagers\` | 엔진 매니저 설정 (Input/Tag/Graphics/Quality/AudioMixer 등 JSON) |
| `data\scene_objects.json` | level0 씬의 모든 GameObject + 컴포넌트 목록 |
| `data\code_class_inventory.json` | 디컴파일된 전체 클래스/인터페이스/열거형 목록 (2,159개) |
| `localization\` | 다국어 문자열 테이블 CSV (11개 언어 + 통합본) |
| `code\` | **디컴파일된 C# 소스** (Assembly-CSharp, 플러그인 DLL 등) |
| `inventory.json` | 원본 유니티 파일별 오브젝트 타입 통계 |
| `extraction_stats.json` | 추출 통계 |
| `scene_tree_level0.txt` | level0 오브젝트 계층 트리 (들여쓰기) |

## 코드 보는 법
- `code\Assembly-CSharp\` : **게임 본체 로직**. `cozyhouse\`(코어), `TS\`, `cozyhouse.*\` 하위 네임스페이스별로 정리됨.
- `code\Assembly-CSharp-firstpass\` : DOTween, Easy Save 3(ES3), 로그뷰어
- `code\Heathen.Steamworks\`, `code\com.rlabrecque.steamworks.net\` : 스팀 연동
- `code\Vuplex.WebView\` : 임베디드 브라우저
- `code\Unity.Localization\`, `code\Unity.Addressables\`, `code\Unity.ResourceManager\` : 유니티 패키지

## 다시 실행/재현 정보
- 사용 도구: **UnityPy 1.25.3 + TypeTreeGeneratorAPI** (에셋 추출), **ILSpy 9.1 (ilspycmd)** (디컴파일)
- Unity 버전: **6000.0.51f1**, 스크립팅 백엔드: Mono

## 주의
- 추출 이미지/오디오/폰트/코드는 **원 저작권자(TesseractStudio)의 자산**입니다. 개인 열람·분석 외 재배포·상업적 사용 금지.
