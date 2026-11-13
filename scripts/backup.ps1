# PowerShell Backup Script for EduCabinet
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$backupDir = Join-Path $projectRoot "backups"

if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$zipPath = Join-Path $backupDir ("EduCabinet_Backup_" + $timestamp + ".zip")

Write-Host "Creating backup: $zipPath..." -ForegroundColor Cyan

$dbPath = Join-Path $projectRoot "EduCabinet.Web\educabinet.db"
if (-not (Test-Path $dbPath)) {
    $dbPath = Join-Path $projectRoot "educabinet.db"
}
$uploadsPath = Join-Path $projectRoot "EduCabinet.Web\wwwroot\uploads"

$tempFolder = Join-Path ([System.IO.Path]::GetTempPath()) ("EduCabinet_Backup_" + $timestamp)
New-Item -ItemType Directory -Path $tempFolder | Out-Null

try {
    if (Test-Path $dbPath) {
        Copy-Item -Path $dbPath -Destination (Join-Path $tempFolder "educabinet.db") -Force
        Write-Host "Database copied." -ForegroundColor Green
    }

    if (Test-Path $uploadsPath) {
        Copy-Item -Path $uploadsPath -Destination (Join-Path $tempFolder "uploads") -Recurse -Force
        Write-Host "Uploads folder copied." -ForegroundColor Green
    }

    Compress-Archive -Path (Join-Path $tempFolder "*") -DestinationPath $zipPath -Force
    Write-Host "[SUCCESS] Backup created at: $zipPath" -ForegroundColor Green
}
finally {
    Remove-Item -Path $tempFolder -Recurse -Force -ErrorAction SilentlyContinue
}
