param(
    [switch]$Elevated,
    [string]$CleanupDir = ""
)

$ErrorActionPreference = "Stop"
$DefaultInstallDir = "C:\CF Tecnologia\XML Copy"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Show-Error([string]$message) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($message, "Instalador XmlCopiador", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

function Start-ElevatedCopy {
    $stableDir = Join-Path $env:TEMP ("XmlCopiadorSetup_" + [Guid]::NewGuid().ToString("N"))
    New-Item -Path $stableDir -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot "install.ps1") -Destination $stableDir -Force
    Copy-Item -Path (Join-Path $PSScriptRoot "payload.zip") -Destination $stableDir -Force

    $scriptPath = Join-Path $stableDir "install.ps1"
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Elevated -CleanupDir `"$stableDir`""
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs | Out-Null
}

if (-not (Test-IsAdmin)) {
    try {
        Start-ElevatedCopy
    }
    catch {
        Show-Error "Nao foi possivel iniciar o instalador como administrador.`n`n$($_.Exception.Message)"
    }
    exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

function New-Shortcut([string]$shortcutPath, [string]$targetPath, [string]$workingDir, [string]$iconPath) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetPath
    $shortcut.WorkingDirectory = $workingDir
    if (Test-Path -LiteralPath $iconPath) {
        $shortcut.IconLocation = $iconPath
    }
    $shortcut.Save()
}

function Expand-Payload([string]$zipPath, [string]$destination) {
    if (Get-Command Expand-Archive -ErrorAction SilentlyContinue) {
        Expand-Archive -Path $zipPath -DestinationPath $destination -Force
        return
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (-not (Test-Path -LiteralPath $destination)) {
        New-Item -Path $destination -ItemType Directory -Force | Out-Null
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $targetFile = Join-Path $destination $entry.FullName
            if ([string]::IsNullOrWhiteSpace($entry.Name)) {
                New-Item -Path $targetFile -ItemType Directory -Force | Out-Null
                continue
            }

            $targetDir = Split-Path -Path $targetFile -Parent
            if (-not (Test-Path -LiteralPath $targetDir)) {
                New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
            }

            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetFile, $true)
        }
    }
    finally {
        $archive.Dispose()
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Instalador XmlCopiador"
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.Width = 620
$form.Height = 270
$form.BackColor = [System.Drawing.Color]::FromArgb(246, 242, 251)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

$title = New-Object System.Windows.Forms.Label
$title.Text = "Instalar Copiador de XML iCompany"
$title.Left = 20
$title.Top = 18
$title.Width = 560
$title.Height = 28
$title.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)

$description = New-Object System.Windows.Forms.Label
$description.Text = "Escolha a pasta onde o aplicativo sera instalado."
$description.Left = 20
$description.Top = 52
$description.Width = 560
$description.Height = 24

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = "Pasta de destino"
$pathLabel.Left = 20
$pathLabel.Top = 90
$pathLabel.Width = 120
$pathLabel.Height = 24

$pathBox = New-Object System.Windows.Forms.TextBox
$pathBox.Left = 20
$pathBox.Top = 116
$pathBox.Width = 440
$pathBox.Height = 24
$pathBox.Text = $DefaultInstallDir

$browseButton = New-Object System.Windows.Forms.Button
$browseButton.Text = "Procurar"
$browseButton.Left = 470
$browseButton.Top = 114
$browseButton.Width = 110
$browseButton.Height = 28

$desktopShortcut = New-Object System.Windows.Forms.CheckBox
$desktopShortcut.Text = "Criar atalho na area de trabalho"
$desktopShortcut.Left = 20
$desktopShortcut.Top = 154
$desktopShortcut.Width = 260
$desktopShortcut.Height = 24
$desktopShortcut.Checked = $true
$desktopShortcut.BackColor = $form.BackColor

$launchAfterInstall = New-Object System.Windows.Forms.CheckBox
$launchAfterInstall.Text = "Abrir aplicativo apos instalar"
$launchAfterInstall.Left = 290
$launchAfterInstall.Top = 154
$launchAfterInstall.Width = 240
$launchAfterInstall.Height = 24
$launchAfterInstall.Checked = $false
$launchAfterInstall.BackColor = $form.BackColor

$installButton = New-Object System.Windows.Forms.Button
$installButton.Text = "Instalar"
$installButton.Left = 360
$installButton.Top = 190
$installButton.Width = 105
$installButton.Height = 30
$installButton.BackColor = [System.Drawing.Color]::FromArgb(103, 58, 183)
$installButton.ForeColor = [System.Drawing.Color]::White
$installButton.FlatStyle = "Flat"

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Text = "Cancelar"
$cancelButton.Left = 475
$cancelButton.Top = 190
$cancelButton.Width = 105
$cancelButton.Height = 30
$cancelButton.FlatStyle = "Flat"

$status = New-Object System.Windows.Forms.Label
$status.Left = 20
$status.Top = 194
$status.Width = 320
$status.Height = 24
$status.Text = ""

$browseButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Escolha a pasta de instalacao"
    $dialog.SelectedPath = $pathBox.Text
    if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        $pathBox.Text = $dialog.SelectedPath
    }
})

$cancelButton.Add_Click({
    $form.Close()
})

$installButton.Add_Click({
    $targetDir = $pathBox.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($targetDir)) {
        [System.Windows.Forms.MessageBox]::Show($form, "Informe a pasta de destino.", "Instalador XmlCopiador", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        return
    }

    $payload = Join-Path $PSScriptRoot "payload.zip"
    if (-not (Test-Path -LiteralPath $payload)) {
        [System.Windows.Forms.MessageBox]::Show($form, "O arquivo payload.zip nao foi encontrado.", "Instalador XmlCopiador", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }

    try {
        $installButton.Enabled = $false
        $cancelButton.Enabled = $false
        $browseButton.Enabled = $false
        $status.Text = "Instalando..."
        $form.Refresh()

        New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
        Expand-Payload -zipPath $payload -destination $targetDir

        $exePath = Join-Path $targetDir "XmlCopiador.exe"
        $iconPath = Join-Path $targetDir "Assets\ic_launcher.ico"

        $programsDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\CF Tecnologia"
        New-Item -Path $programsDir -ItemType Directory -Force | Out-Null
        New-Shortcut -shortcutPath (Join-Path $programsDir "XML Copy.lnk") -targetPath $exePath -workingDir $targetDir -iconPath $iconPath

        if ($desktopShortcut.Checked) {
            $desktopDir = [Environment]::GetFolderPath("DesktopDirectory")
            New-Shortcut -shortcutPath (Join-Path $desktopDir "XML Copy.lnk") -targetPath $exePath -workingDir $targetDir -iconPath $iconPath
        }

        [System.Windows.Forms.MessageBox]::Show($form, "Instalacao concluida com sucesso.", "Instalador XmlCopiador", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null

        if ($launchAfterInstall.Checked -and (Test-Path -LiteralPath $exePath)) {
            Start-Process -FilePath $exePath -WorkingDirectory $targetDir | Out-Null
        }

        $form.Close()
    }
    catch {
        $installButton.Enabled = $true
        $cancelButton.Enabled = $true
        $browseButton.Enabled = $true
        $status.Text = ""
        [System.Windows.Forms.MessageBox]::Show($form, "Nao foi possivel instalar o aplicativo.`n`n$($_.Exception.Message)", "Instalador XmlCopiador", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
})

$form.Controls.AddRange(@(
    $title,
    $description,
    $pathLabel,
    $pathBox,
    $browseButton,
    $desktopShortcut,
    $launchAfterInstall,
    $installButton,
    $cancelButton,
    $status
))

[void]$form.ShowDialog()

if (-not [string]::IsNullOrWhiteSpace($CleanupDir)) {
    try {
        Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -Command `"Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$CleanupDir' -Recurse -Force -ErrorAction SilentlyContinue`"" | Out-Null
    }
    catch {
    }
}
