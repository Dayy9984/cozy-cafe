# 실제 OS 시험
windows.json / macos.json은 실제 해당 OS에서 실행한 빌드만 기록한다.
{"os":"windows","status":"PASS","build_sha256":"actual build hash","cases":["transparency","IME","focus","DPI","sleep"],"evidence":[{"path":"native/evidence/windows-recording.mp4","sha256":"actual hash"}]}
예시문자열을 복사해 PASS로 만들지 않는다. raw 실행로그·실제빌드·녹화가 필요하며 독립 검토자가 검사한다.

native/build/<os>/는 실행·생성된 실 산출물의 추적(tracked) 포장본이다 —
out/builds/는 gitignore라 스냅샷에 실리지 않으므로 build.path는 포장본을
가리키고 sha256은 포장 파일에서 재계산된다. macOS .app은
~/UnityLocal/6000.6.3f1 미러 에디터(MacStandaloneSupport 모듈 설치)의
CozyCafe.Editor.NativePlayerBuild.BuildMacOS가 생성한 실 Unity 플레이어
번들이다. 이 Windows 호스트에서는 실행 불가 — status는 BLOCKED로 유지.
