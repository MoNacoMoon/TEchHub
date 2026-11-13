# Скрипт настройки брандмауэра Windows для доступа студентов к кабинету по локальной сети
# Запуск от имени Администратора: powershell -ExecutionPolicy Bypass -File setup-firewall.ps1

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Настройка сетевого экрана (Windows Firewall) для Электронного кабинета" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

$ruleName = "EduCabinet_Web_Port_5000"
$port = 5000

# Проверяем, существует ли уже правило
$existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue

if ($existing) {
    Write-Host "[ИНФО] Правило '$ruleName' уже существует в брандмауэре." -ForegroundColor Yellow
} else {
    Write-Host "[1/2] Добавление входящего правила для TCP порта $port..." -ForegroundColor Green
    New-NetFirewallRule -DisplayName $ruleName `
                        -Direction Inbound `
                        -LocalPort $port `
                        -Protocol TCP `
                        -Action Allow `
                        -Description "Разрешает студентам подключаться к веб-ресурсу Электронный учебный кабинет" | Out-Null
    Write-Host "[УСПЕХ] Входящее правило успешно создано!" -ForegroundColor Green
}

# Определяем IP-адрес преподавателя
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notmatch "Loopback|vEthernet" -and $_.IPAddress -notmatch "^169\." } | Select-Object -First 1).IPAddress

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Адрес для подключения студентов в локальной сети:" -ForegroundColor Yellow
Write-Host " http://${ip}:${port}" -ForegroundColor White -BackgroundColor DarkBlue
Write-Host "======================================================================" -ForegroundColor Cyan
