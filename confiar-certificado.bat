@echo off
REM ============================================================
REM  Confiar el certificado de la intranet (se corre en CADA PC usuaria)
REM  1) Descarga intranet.cer del servidor
REM  2) Lo agrega a "Autoridades de certificacion raiz de confianza" del usuario
REM  No pide Administrador. Se corre UNA sola vez por PC.
REM  Al terminar, la intranet se abre con https://...:5024 y ahi el navegador
REM  ya puede pedir el permiso de ubicacion (-automatico, sin escribir nada-).
REM ============================================================
setlocal

REM %1 = direccion del servidor (opcional). %2 = ruta de un .cer ya bajado (opcional).
if "%~1"=="" ( set "SERVIDOR=http://10.252.252.200:5023" ) else ( set "SERVIDOR=%~1" )
set "CER=%~2"
if "%CER%"=="" set "CER=%TEMP%\intranet.cer"

echo.
echo === Direccion del servidor: %SERVIDOR% ===
echo.

REM El archivo solo se baja si no lo pasaron como argumento.
if exist "%CER%" goto :confiar

where curl >nul 2>&1
if errorlevel 1 goto :manual
echo Bajando el certificado del servidor...
curl -fsSL --max-time 20 "%SERVIDOR%/intranet.cer" -o "%CER%"
if not exist "%CER%" goto :manual

:confiar
echo Agregando el certificado a las autoridades de raiz de confianza de este usuario...
certutil -addstore -user Root "%CER%"
if errorlevel 1 (
  echo.
  echo ERROR: Windows no pudo agregar el certificado.
  echo Revisa que "%CER%" exista y que sea el archivo intranet.cer del servidor.
  pause
  exit /b 1
)

certutil -store -user Root | findstr /i /c:"intranet" >nul
if errorlevel 1 (
  echo Aviso: Windows no lo muestra en la lista, pero igual intenta abrir la direccion segura.
)

echo.
echo ============================================
echo  Listo: esta PC ya confia el certificado.
echo.
echo  Abre la intranet en la direccion segura:
echo     %SERVIDOR:http:=https:%/Login
echo  (el puerto cambia a 5024; ya no debe salir advertencia)
echo.
echo  La primera vez que entres, el navegador preguntara si permite saber donde esta
echo  el equipo: dale en PERMITIR. Con eso la bitacora marca la ubicacion real del
echo  aparato, sin escribir ninguna direccion a mano.
echo ============================================
echo.
pause
endlocal
exit /b 0

:manual
echo.
echo No se pudo bajar el certificado automaticamente (no hay red o falta "curl").
echo Haz esto y vuelve a correr este archivo:
echo   1) Abre en el navegador:  %SERVIDOR%/intranet.cer   (se descarga el archivo)
echo   2) Ejecuta desde una ventana de comandos:
echo        confiar-certificado.bat %SERVIDOR% "C:\Users\tu-usuario\Downloads\intranet.cer"
echo.
pause
endlocal
exit /b 1
