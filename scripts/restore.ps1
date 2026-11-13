# PowerShell Restore Script for EduCabinet
param(
    [Parameter(Mandatory=$false)]
    [string]$BackupFilePath
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

if (-not $BackupFilePath) {
    $backupDir = Join-Path $projectRoot "backups"
    $latestBackup = Get-ChildItem -Path $backupDir -Filter "EduCabinet_Backup_*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestBackup) {
        $BackupFilePath = $latestBackup.FullName
        Write-Host "Using latest backup: $BackupFilePath" -ForegroundColor Cyan
    } else {
        Write-Host "Error: No backup found in $backupDir" -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $BackupFilePath)) {
    Write-Host "Error: File $BackupFilePath does not exist." -ForegroundColor Red
    exit 1
}

$tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "EduCabinet_Restore_Temp"
if (Test-Path $tempExtract) { Remove-Item -Path $tempExtract -Recurse -Force }
New-Item -ItemType Directory -Path $tempExtract | Out-Null

try {
    Write-Host "Extracting archive..." -ForegroundColor Cyan
    Expand-Archive -Path $BackupFilePath -DestinationPath $tempExtract -Force

    $targetDb1 = Join-Path $projectRoot "EduCabinet.Web\educabinet.db"
    $targetDb2 = Join-Path $projectRoot "educabinet.db"
    $extractedDb = Join-Path $tempExtract "educabinet.db"

    if (Test-Path $extractedDb) {
        Copy-Item -Path $extractedDb -Destination $targetDb1 -Force -ErrorAction SilentlyContinue
        Copy-Item -Path $extractedDb -Destination $targetDb2 -Force -ErrorAction SilentlyContinue
        Write-Host "Database successfully restored." -ForegroundColor Green
    }

    $extractedUploads = Join-Path $tempExtract "uploads"
    $targetUploads = Join-Path $projectRoot "EduCabinet.Web\wwwroot\uploads"
    if (Test-Path $extractedUploads) {
        if (-not (Test-Path $targetUploads)) { New-Item -ItemType Directory -Path $targetUploads | Out-Null }
        Copy-Item -Path (Join-Path $extractedUploads "*") -Destination $targetUploads -Recurse -Force
        Write-Host "Uploads folder successfully restored." -ForegroundColor Green
    }

    Write-Host "[SUCCESS] System restored from $BackupFilePath" -ForegroundColor Green
}
finally {
    Remove-Item -Path $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
}
