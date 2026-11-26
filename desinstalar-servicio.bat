@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Ejecuta este script como Administrador.
    pause
    exit /b 1
)

set "SERVICE_NAME=MeditechnosysIntranet"

sc query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% neq 0 (
    echo El servicio %SERVICE_NAME% no existe.
    pause
    exit /b 0
)

echo Deteniendo...
sc stop "%SERVICE_NAME%" >nul 2>&1
timeout /t 3 /nobreak >nul

echo Eliminando...
sc delete "%SERVICE_NAME%"
echo Listo.
pause
