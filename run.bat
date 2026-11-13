@echo off
chcp 65001 > nul
echo ======================================================================
echo  Электронный учебный кабинет преподавателя и студента
echo  Запуск веб-сервера Kestrel
echo ======================================================================
echo.

cd /d "%~dp0"

REM Определяем локальный IP-адрес для вывода студентам
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do (
    set LOCAL_IP=%%a
    goto :found_ip
)
:found_ip
set LOCAL_IP=%LOCAL_IP: =%

echo [ИНФОРМАЦИЯ О СЕТЕВОМ ДОСТУПЕ]
echo 1. Локальный доступ на этом компьютере:
echo    http://localhost:5000
echo.
echo 2. Доступ для студентов по локальной сети:
echo    http://%LOCAL_IP%:5000
echo.
echo [УЧЕТНЫЕ ДАННЫЕ ДЛЯ ВХОДА]
echo - Преподаватель: teacher / Teacher123!
echo - Студент 1:     ivanov  / Student123! (начинающий)
echo - Студент 2:     petrov  / Student123! (сдал лекцию 1)
echo - Администратор: admin   / Admin123!
echo.
echo ======================================================================
echo Для остановки сервера нажмите Ctrl+C
echo ======================================================================
echo.

if exist "publish\EduCabinet.Web.dll" (
    cd publish
    dotnet EduCabinet.Web.dll --urls "http://0.0.0.0:5000"
) else (
    cd EduCabinet.Web
    dotnet run --urls "http://0.0.0.0:5000"
)

pause
