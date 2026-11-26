# El servicio de Windows (arranque automático)

## ¿Qué es y por qué?

Un **servicio de Windows** es un programa que se ejecuta **en segundo plano**, sin que nadie abra una ventana, y que puede **arrancar solo cuando se enciende la PC** (antes incluso de que alguien inicie sesión en Windows).

Conviene al sistema unificado porque:
- El **receptor DICOM** (puerto 11113) y la **web** (puerto 5023) deben estar **siempre disponibles** para todo el equipo.
- Si se cerrara una ventana o se reiniciara la PC, el servicio **vuelve a arrancar solo**.
- Si el programa falla, Windows lo **reinicia automáticamente** (configurado en el script).

El servicio se llama **`MeditechnosysIntranet`** y ejecuta el `.exe` de la carpeta `publish\`.

---

## Cómo se instala (2 pasos)

> Ambos scripts están en la carpeta del proyecto: `Intranet_Unificada\`.

1. **`publicar.bat`** — doble clic.
   - Compila y genera la versión final **autocontenida** en `publish\Meditechnosys.Intranet.exe` (un solo archivo, no requiere instalar .NET en el servidor).
2. **`instalar-servicio.bat`** — **clic derecho → Ejecutar como administrador**.
   - Crea el servicio `MeditechnosysIntranet`, lo configura con **arranque automático** (`start= auto`), **reinicios automáticos** si falla, y lo **inicia** de inmediato.
   - Necesita permisos de administrador porque registrar un servicio es un cambio a nivel de sistema.

Al terminar, el sistema queda escuchando en **http://localhost:5023** (y accesible desde otras PC por la IP del equipo, gracias a `0.0.0.0:5023`).

---

## Qué hace exactamente `instalar-servicio.bat`

```bat
set "SERVICE_NAME=MeditechnosysIntranet"
set "EXE_PATH=%~dp0publish\Meditechnosys.Intranet.exe"
set "URL=http://0.0.0.0:5023"          ← puerto web (cámbialo aquí si hace falta)

sc create MeditechnosysIntranet binPath= "\"...publish\Meditechnosys.Intranet.exe\" --urls http://0.0.0.0:5023" start= auto
sc config  MeditechnosysIntranet AppDirectory= "...publish"     ← carpeta de trabajo (appsettings, wwwroot, descargas)
sc failure MeditechnosysIntranet reset= 60 actions= restart/5000/restart/10000/restart/30000   ← reinicios automáticos
sc start   MeditechnosysIntranet
```

- `--urls http://0.0.0.0:5023` → le dice al programa en qué puerto escuchar. `0.0.0.0` = "acepta conexiones de cualquier tarjeta de red" (para que otras PC del hospital entren). Si solo quisieras acceso local, usa `http://localhost:5023`.
- `AppDirectory` → la carpeta de trabajo. Es importante porque ahí están `appsettings.json`, `wwwroot\` y donde se crean `dicom_downloads\`.
- El **puerto DICOM 11113** no se pone aquí: viene de `appsettings.json` (`Local:MovePort`).

---

## Comandos útiles (en una consola, como administrador)

| Acción | Comando |
|---|---|
| Ver estado | `sc query MeditechnosysIntranet` |
| Detener | `sc stop MeditechnosysIntranet` |
| Iniciar | `sc start MeditechnosysIntranet` |
| Reiniciar | `sc stop MeditechnosysIntranet && sc start MeditechnosysIntranet` |
| Eliminar | `desinstalar-servicio.bat` (como admin) |

También puedes verlo en la ventana **Servicios** de Windows (`services.msc`), como *"Meditechnosys Intranet (Patologia + DICOM)"*.

---

## Cómo actualizar el sistema cuando cambies el código

**Opción recomendada (1 clic):** clic derecho en **`actualizar.bat`** → *Ejecutar como administrador*.
El script hace solo los tres pasos: detiene el servicio, ejecuta `dotnet publish` sobre `publish\` y reinicia el servicio.
Si la publicación falla, intenta rearrancar el servicio y te avisa.

**Opción manual (3 pasos):**
1. **Detén** el servicio: `sc stop MeditechnosysIntranet`.
2. Vuelve a ejecutar **`publicar.bat`** (regenera `publish\`).
3. **Inicia** el servicio: `sc start MeditechnosysIntranet`.

> Si **solo cambiaste archivos `.html`/css/js** (diseño), **no hace falta reiniciar**: copia el archivo a
> `publish\wwwroot\...` y pulsa **F5** en el navegador (las `.html` se sirven sin caché).
> Solo necesitas `actualizar.bat` (reinicio) cuando cambió código **C#** o `appsettings.json`.
>
> No hace falta reinstalar el servicio si solo cambió el código. Solo reinstala (`instalar-servicio.bat`)
> si cambiaste el puerto, la ruta del `.exe` o el nombre del servicio.

### Los datos del servidor no se publican

`publish\App_Data\` es el **almacén de respaldo en tiempo de ejecución** del servidor: usuarios y
bitácora en JSON (si MySQL no respondió al arrancar), actividades, reportes, historial, permisos,
configuración, inventarios, Playbook y los PDF subidos. Por eso:

- `Meditechnosys.Intranet.csproj` marca `App_Data\**` con `CopyToPublishDirectory="Never"`:
  `dotnet publish` (y por tanto `actualizar.bat`) **nunca** copia ni sobrescribe esos archivos. La
  carpeta `App_Data\` de la carpeta de desarrollo es solo de pruebas.
- `publicar.bat` borra `publish\` completo, así que **aparta** `publish\App_Data` a
  `..\App_Data_servidor` (fuera del proyecto, para que `dotnet publish` no lo vuelva a copiar) antes
  de borrar y **lo devuelve** al terminar. Si la publicación falla, los datos quedan a salvo en
  `..\App_Data_servidor` y el script lo avisa.
- En un servidor nuevo la carpeta se crea sola al primer arranque; los usuarios vienen de MySQL o de
  la semilla por variables de entorno (ver `docs/USUARIOS-Y-MYSQL.md` §8). **Nunca** se envían a
  producción las cuentas de prueba `admin`/`ing`.

---

## Dónde ver qué está pasando (logs)

- El servicio escribe su actividad por los canales estándar de .NET. Para ver errores de arranque:
  - **Visor de eventos de Windows** → *Registros de Windows → Aplicación* (busca fallos del servicio).
  - O ejecuta el `.exe` **a mano** en una consola para ver los mensajes en pantalla:
    ```
    cd publish
    Meditechnosys.Intranet.exe --urls http://localhost:5023
    ```
    Al arrancar deberías ver:
    ```
    Receptor C-STORE escuchando en :11113 AE=MEDI
    Vigilando carpetas de entrada cada 30s.
    Now listening on: http://localhost:5023
    ```

---

## Solución de problemas del servicio

- **No inicia / se detiene solo** → casi siempre es MySQL caído o falta la base `RepoPato`. Arranca el `.exe` a mano (arriba) para leer el error exacto.
- **Puerto 5023 ocupado** → otra app lo usa; cambia la variable `URL` en `instalar-servicio.bat` y reinstala.
- **Puerto 11113 ocupado** → quizá quedó una instancia vieja del DICOM; ciérrala o cambia `Local:MovePort`.
- **"Acceso denegado" al instalar** → no ejecutaste el script como administrador.
- **Otras PC no entran** → revisa el **Firewall de Windows**: debe permitir el puerto 5023 (entrada) en la red del hospital.

---

## Diferencia con "correr a mano"

| | `dotnet run` / doble clic al `.exe` | Servicio de Windows |
|---|---|---|
| Arranca con la PC | No | **Sí (automático)** |
| Necesita ventana abierta | Sí | No |
| Se reinicia si falla | No | **Sí** |
| Para producción | No | **Sí** |

Para el día a día del hospital, usa el **servicio**. `dotnet run` es solo para desarrollo/pruebas.
