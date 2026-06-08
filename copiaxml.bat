@echo off
setlocal

set "APP=%~dp0dist\XmlCopiador\XmlCopiador.exe"
set "BUILD=%~dp0gerar_exe_xml.ps1"

if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)

echo O aplicativo XmlCopiador ainda nao foi gerado.
echo.
echo Vou tentar gerar agora usando o dotnet instalado nesta maquina.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD%"
if errorlevel 1 (
    echo.
    echo Nao consegui gerar o executavel.
    echo Veja as mensagens acima.
    pause
    exit /b 1
)

if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)

echo.
echo O build terminou, mas o executavel nao foi encontrado em:
echo %APP%
pause
exit /b 1
