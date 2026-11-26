@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: No se encontro "dotnet" en el PATH.
  echo Instala .NET 8 SDK desde: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo.
echo === SDK ===
dotnet --version
echo.

echo Restaurando paquetes...
dotnet restore
if errorlevel 1 (
  echo ERROR en dotnet restore
  pause
  exit /b 1
)

echo.
echo Compilando (para ver errores claros)...
dotnet build -c Release --no-restore
if errorlevel 1 (
  echo.
  echo ERROR de compilacion. Revisa los mensajes de arriba.
  pause
  exit /b 1
)

echo.
echo Publicando (self-contained, un solo .exe)...
REM publish\App_Data son los datos del servidor (respaldo JSON, PDF del Playbook). Se apartan
REM antes de borrar la carpeta y se devuelven al terminar para no perderlos en una republicacion.
REM Se aparcan FUERA del proyecto: si quedaran dentro, dotnet publish los volveria a copiar.
set "RESGUARDO=%~dp0..\App_Data_servidor"
if exist "publish\App_Data" (
  echo Apartando los datos del servidor ^(publish\App_Data^)...
  if exist "%RESGUARDO%" rmdir /s /q "%RESGUARDO%"
  move "publish\App_Data" "%RESGUARDO%" >nul
)
if exist publish rmdir /s /q publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish --no-restore
if errorlevel 1 (
  echo ERROR en dotnet publish
  if exist "%RESGUARDO%" echo Tus datos quedaron a salvo en: %RESGUARDO%
  pause
  exit /b 1
)

if exist "%RESGUARDO%" (
  echo Devolviendo los datos del servidor a publish\App_Data...
  move "%RESGUARDO%" "publish\App_Data" >nul
)

if not exist "publish\Meditechnosys.Intranet.exe" (
  echo ERROR: no se genero publish\Meditechnosys.Intranet.exe
  dir publish
  pause
  exit /b 1
)

echo.
echo ========================================
echo  Publicacion lista en: %cd%\publish\
echo  (Intranet + Repositorio de Patologia + Visor DICOM, todo en uno)
echo.
echo  Probar a mano:
echo    cd publish
echo    Meditechnosys.Intranet.exe --urls http://localhost:5023
echo.
echo  Instalar como servicio (como Administrador):
echo    instalar-servicio.bat
echo ========================================
pause
