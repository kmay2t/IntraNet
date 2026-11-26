@echo off
:: Ejecutar como Administrador
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Ejecuta este script como Administrador.
    echo Clic derecho -^> Ejecutar como administrador
    pause
    exit /b 1
)

set "SERVICE_NAME=MeditechnosysIntranet"
set "DISPLAY_NAME=Meditechnosys Intranet (Patologia + DICOM)"
set "EXE_PATH=%~dp0publish\Meditechnosys.Intranet.exe"
set "WORK_DIR=%~dp0publish"
:: Puerto HTTP de la intranet unificada. Cambialo aqui si lo necesitas.
set "URL=http://0.0.0.0:5023"

if not exist "%EXE_PATH%" (
    echo No se encuentra: %EXE_PATH%
    echo Primero publica con: publicar.bat
    pause
    exit /b 1
)

:: Si ya existe, pararlo y eliminarlo
sc query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% equ 0 (
    echo Deteniendo servicio existente...
    sc stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete "%SERVICE_NAME%"
    timeout /t 2 /nobreak >nul
)

echo Instalando servicio...
sc create "%SERVICE_NAME%" binPath= "\"%EXE_PATH%\" --urls %URL%" DisplayName= "%DISPLAY_NAME%" start= auto
if %errorLevel% neq 0 (
    echo Error al crear el servicio.
    pause
    exit /b 1
)

:: Directorio de trabajo = carpeta publish (para appsettings.json, wwwroot y dicom_downloads)
sc config "%SERVICE_NAME%" AppDirectory= "%WORK_DIR%" >nul 2>&1

:: Descripcion
sc description "%SERVICE_NAME%" "Intranet unificada: login unico, repositorio de Patologia y visor DICOM (C-FIND / C-MOVE)."

:: Reinicio automatico si falla
sc failure "%SERVICE_NAME%" reset= 60 actions= restart/5000/restart/10000/restart/30000

echo Iniciando servicio...
sc start "%SERVICE_NAME%"
if %errorLevel% neq 0 (
    echo Aviso: no se pudo iniciar. Revisa el Visor de eventos o ejecuta:
    echo   sc start %SERVICE_NAME%
)

echo.
echo ========================================
echo  Servicio instalado: %SERVICE_NAME%
echo  URL: %URL%  (desde esta PC: http://localhost:5023)
echo  Carpeta: %WORK_DIR%
echo ========================================
echo.
echo Comandos utiles:
echo   sc stop %SERVICE_NAME%
echo   sc start %SERVICE_NAME%
echo   sc query %SERVICE_NAME%
echo.
pause
