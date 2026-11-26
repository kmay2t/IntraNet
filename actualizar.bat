@echo off
REM ============================================================
REM  Actualizar la Intranet Unificada (requiere Administrador)
REM  1) Detiene el servicio  2) Republica  3) Lo vuelve a arrancar
REM ============================================================
setlocal
cd /d "%~dp0"

echo.
echo === [1/3] Deteniendo servicio MeditechnosysIntranet ===
sc stop MeditechnosysIntranet >nul 2>&1
timeout /t 4 /nobreak >nul

echo === [2/3] Republicando (dotnet publish) ===
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo ERROR: la publicacion fallo. Revisa el mensaje de arriba.
  echo === Intentando arrancar el servicio de nuevo ===
  sc start MeditechnosysIntranet >nul 2>&1
  pause
  exit /b 1
)

echo === [3/3] Arrancando servicio MeditechnosysIntranet ===
sc start MeditechnosysIntranet >nul 2>&1
timeout /t 3 /nobreak >nul
sc query MeditechnosysIntranet | findstr /i "STATE"

echo.
echo Listo. La intranet ya tiene los cambios aplicados.
pause
endlocal
