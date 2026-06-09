param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "XmlCopiadorApp\XmlCopiadorApp.csproj"
$publishDir = Join-Path $root "dist\XmlCopiador"
$releaseDir = Join-Path $root "release"
$installerDir = Join-Path $root "installer"
$buildDir = Join-Path ([IO.Path]::GetTempPath()) "XmlCopiadorInstallerBuild"
$payloadSource = Join-Path $buildDir "payload"
$payloadZip = Join-Path $buildDir "payload.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet nao foi encontrado nesta maquina."
}

$iexpress = Get-Command iexpress.exe -ErrorAction SilentlyContinue
if (-not $iexpress) {
    throw "iexpress.exe nao foi encontrado nesta maquina."
}

[xml]$projectXml = Get-Content -Path $project
$version = $projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Nao foi possivel ler a versao do projeto."
}

& (Join-Path $root "gerar_exe_xml.ps1") -SelfContained:$SelfContained

New-Item -Path $releaseDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "XmlCopiador.exe") -Destination (Join-Path $releaseDir "XmlCopiador.exe") -Force

if (Test-Path -LiteralPath $buildDir) {
    Remove-Item -LiteralPath $buildDir -Recurse -Force
}
New-Item -Path $payloadSource -ItemType Directory -Force | Out-Null

Copy-Item -Path (Join-Path $publishDir "XmlCopiador.exe") -Destination $payloadSource -Force

$sqlFile = Get-ChildItem -Path $publishDir -Filter "*exportar_xml_nfe.sql" -File | Select-Object -First 1
if ($sqlFile) {
    Copy-Item -Path $sqlFile.FullName -Destination $payloadSource -Force
}

$assetsSource = Join-Path $publishDir "Assets"
if (Test-Path -LiteralPath $assetsSource) {
    Copy-Item -Path $assetsSource -Destination $payloadSource -Recurse -Force
}

Compress-Archive -Path (Join-Path $payloadSource "*") -DestinationPath $payloadZip -Force
Copy-Item -Path (Join-Path $installerDir "install.ps1") -Destination $buildDir -Force

$setupName = "XmlCopiadorSetup-$version.exe"
$setupPath = Join-Path $releaseDir $setupName
$tempSetupPath = Join-Path $buildDir $setupName
$sedPath = Join-Path $buildDir "XmlCopiadorSetup.sed"
$sourceFilesPath = $buildDir.TrimEnd("\") + "\"

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$tempSetupPath
FriendlyName=XmlCopiador Setup $version
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0="install.ps1"
FILE1="payload.zip"
[SourceFiles]
SourceFiles0=$sourceFilesPath
[SourceFiles0]
%FILE0%=
%FILE1%=
"@

Set-Content -Path $sedPath -Value $sed -Encoding ASCII
Start-Process -FilePath $iexpress.Source -ArgumentList @("/N", "/Q", $sedPath) -Wait -NoNewWindow -WorkingDirectory $buildDir

$deadline = (Get-Date).AddSeconds(30)
while (-not (Test-Path -LiteralPath $tempSetupPath) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}

if (-not (Test-Path -LiteralPath $tempSetupPath)) {
    throw "O instalador nao foi gerado: $tempSetupPath"
}

Copy-Item -Path $tempSetupPath -Destination $setupPath -Force

$exeHash = (Get-FileHash -Path (Join-Path $releaseDir "XmlCopiador.exe") -Algorithm SHA256).Hash
$setupHash = (Get-FileHash -Path $setupPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Release atualizada:"
Write-Host (Join-Path $releaseDir "XmlCopiador.exe")
Write-Host "SHA256 EXE:"
Write-Host $exeHash
Write-Host ""
Write-Host "Instalador gerado:"
Write-Host $setupPath
Write-Host "SHA256 INSTALADOR:"
Write-Host $setupHash
