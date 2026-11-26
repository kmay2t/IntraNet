@echo off
REM ============================================================
REM  Abrir en el Firewall de Windows el puerto seguro de la intranet (5024)
REM  Se corre UNA vez en la PC donde esta instalado el servicio.
REM  Requiere Administrador.
REM ============================================================
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Ejecuta este script como Administrador.
    echo Clic derecho -^> Ejecutar como administrador
    pause
    exit /b 1
)

set "PUERTO=5024"
set "REGLA=Meditechnosys Intranet HTTPS"

echo.
echo Borrando una regla anterior con el mismo nombre (si existia)...
netsh advfirewall firewall delete rule name="%REGLA%" >nul 2>&1

echo Agregando la regla de entrada para TCP %PUERTO%...
netsh advfirewall firewall add rule name="%REGLA%" dir=in action=allow protocol=TCP localport=%PUERTO% enable=yes profile=any
if errorlevel 1 (
  echo ERROR: no se pudo crear la regla.
  pause
  exit /b 1
)

echo.
echo Reglas de la intranet en el firewall:
netsh advfirewall firewall show rule name=all | findstr /i "Meditechnosys 5023 5024"

echo.
echo Listo. Desde otra PC del hospital ya se puede abrir https://ESTA-PC:%PUERTO%
echo (antes hay que confiar el certificado en esa PC con confiar-certificado.bat).
echo.
pause
