param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "XmlCopiadorApp\XmlCopiadorApp.csproj"
$output = Join-Path $PSScriptRoot "dist\XmlCopiador"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet nao foi encontrado nesta maquina."
}

$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$publishArgs = @(
    "publish",
    $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $selfContainedValue,
    "/p:PublishSingleFile=true",
    "-o", $output
)

if ($SelfContained) {
    $publishArgs += "/p:EnableCompressionInSingleFile=true"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falhou com codigo $LASTEXITCODE."
}

$exe = Join-Path $output "XmlCopiador.exe"
$sha256 = (Get-FileHash -Path $exe -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Executavel gerado em:"
Write-Host $exe
Write-Host ""
Write-Host "SHA256:"
Write-Host $sha256

if (-not $SelfContained) {
    Write-Host ""
    Write-Host "Observacao: esta versao usa o .NET instalado na maquina."
    Write-Host "Para gerar um exe mais portatil, rode: .\gerar_exe_xml.ps1 -SelfContained"
}
