# 게임 구현
Unity 6 프로젝트. v0.8.2 dual-host 계약:
- Assets/CozyCafe/Core — 순수 C# 공유 코어(UnityEngine 미참조, netstandard2.1 API만).
- Assets/CozyCafe/Unity — UnityEngine 의존 뷰·부트스트랩 레이어.
- Assets/CozyCafe/Editor — GauntletEntry.Run(CASE)·CaptureShot.Run(캡처) 진입점.
- GameCli — 같은 Core 소스를 컴파일하는 .NET 8 게이트 호스트(case/render 명령).
CASE 값은 모두 컴파일된 코어의 실제 호출 결과다.
