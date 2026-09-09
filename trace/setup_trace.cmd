@echo off
setlocal
cd /d "%~dp0"
py -3 --version
if errorlevel 1 (
  echo Install Python 3.10 or newer from python.org with the Python launcher.
  pause
  exit /b 1
)
py -3 -m venv .venv
if errorlevel 1 goto failed
.venv\Scripts\python.exe -m pip install -r requirements.txt
if errorlevel 1 goto failed
echo Ready. Run start_trace.cmd.
pause
exit /b 0
:failed
echo Setup failed. Keep this error message for troubleshooting.
pause
exit /b 1
