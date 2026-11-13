@echo off
chcp 65001 > nul
echo ======================================================================
echo  Электронный учебный кабинет преподавателя и студента
echo  Публикация проекта в конфигурации Release для рабочей среды
echo ======================================================================
echo.

cd /d "%~dp0"
echo [1/3] Очистка предыдущей сборки...
if exist "publish" rd /s /q "publish"

echo [2/3] Выполнение компиляции и публикации Release...
dotnet publish EduCabinet.Web\EduCabinet.Web.csproj -c Release -o .\publish --nologo

if %errorlevel% neq 0 (
    echo.
    echo [ОШИБКА] Не удалось выполнить публикацию! Проверьте наличие .NET SDK.
    pause
    exit /b %errorlevel%
)

echo [3/3] Подготовка структуры каталогов в опубликованной версии...
if not exist "publish\wwwroot\uploads" mkdir "publish\wwwroot\uploads"

echo.
echo ======================================================================
echo  Публикация успешно завершена!
echo  Каталог развертывания: %~dp0publish
echo  Для запуска перейдите в каталог publish и запустите run.bat
echo ======================================================================
pause
