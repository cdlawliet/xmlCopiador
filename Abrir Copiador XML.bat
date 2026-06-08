@echo off
setlocal

set "APP=%~dp0dist\XmlCopiador\XmlCopiador.exe"

if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)

echo O executavel ainda nao foi gerado.
echo.
echo Rode primeiro:
echo   powershell -ExecutionPolicy Bypass -File "%~dp0gerar_exe_xml.ps1"
echo.
pause
exit /b 1
