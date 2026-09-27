"""Run actual Godot contract tests. Missing game/engine is a failure, not PASS."""
from pathlib import Path
import os,shutil,subprocess,sys,argparse
ROOT=Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser();p.add_argument('stage');a=p.parse_args()
 engine=os.environ.get('GODOT_BIN') or shutil.which('godot') or shutil.which('godot4')
 if not engine:print('BLOCKED: install Godot and set GODOT_BIN');return 2
 if not (ROOT/'game/project.godot').is_file() or not (ROOT/'game/tests/gauntlet_entry.gd').is_file():
  print('NOT_IMPLEMENTED: game/project.godot and tests/gauntlet_entry.gd are required');return 2
 return subprocess.run([engine,'--headless','--path',str(ROOT/'game'),'--script','res://tests/gauntlet_entry.gd','--',a.stage],cwd=ROOT).returncode
if __name__=='__main__':raise SystemExit(main())
