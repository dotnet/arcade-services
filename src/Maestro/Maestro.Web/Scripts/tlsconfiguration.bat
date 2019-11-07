SETLOCAL
SET LOGFILE="%TEMP%\Startup.log"
SET PollingMode=PollingMode4min
SET TimeSyncLogFile="%TEMP%\w32time.log"

echo "--- Configuring TLS ---" >> "%LOGFILE%" 2>&1
POWERSHELL -ExecutionPolicy Unrestricted -File ".\Scripts\Set-TlsConfiguration.ps1" >> "%LOGFILE%" 2>&1
if %errorlevel% neq 0 if %errorlevel% neq 3010 exit /b %errorlevel%
