# 실제 OS 시험
windows.json / macos.json은 실제 해당 OS에서 실행한 빌드만 기록한다.
{"os":"windows","status":"PASS","build_sha256":"actual build hash","cases":["transparency","IME","focus","DPI","sleep"],"evidence":[{"path":"native/evidence/windows-recording.mp4","sha256":"actual hash"}]}
예시문자열을 복사해 PASS로 만들지 않는다. raw 실행로그·실제빌드·녹화가 필요하며 독립 검토자가 검사한다.
