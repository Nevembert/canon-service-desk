@echo off
setlocal
cd /d "%~dp0"
if not exist .venv\Scripts\python.exe (
  echo Run setup_trace.cmd first. See README-RU.txt.
  pause
  exit /b 1
)
.venv\Scripts\python.exe -X utf8 capture.py
pause
