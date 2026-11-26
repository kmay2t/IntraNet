# Despliegue del portal PHP en cPanel

## Requisitos

- cPanel con PHP 7.4 o superior.
- Acceso al Administrador de archivos o a FTP.
- No se requiere SSH, base de datos, sistema de colas ni proxy inverso.

## Pasos

1. Abre el Administrador de archivos de cPanel.
2. Entra a `public_html` o a la subcarpeta donde se publicará el portal.
3. Sube el archivo `intranet-cpanel.zip`.
4. Extrae el ZIP en esa misma carpeta.
5. Verifica que `index.php` esté en la raíz de la carpeta publicada y que exista `assets/css/portal.css`.
6. En **MultiPHP Manager**, selecciona PHP 7.4 o superior para el dominio.
7. Abre el sitio en el navegador.

## Permisos recomendados

Si el servidor los solicita:

- Carpetas: `755`.
- Archivos: `644`.

## Comprobación

La portada debe mostrar:

- El encabezado **Sistemas médicos**.
- Cuatro tarjetas informativas.
- Ningún botón **Abrir**.
- Ningún formulario de consulta clínica.

El archivo `.htaccess` aplica encabezados de seguridad cuando el servidor usa Apache. Si usa Nginx, las reglas de listado de directorios y encabezados deben configurarse en el servidor; la portada PHP funciona sin ellas.

## Solución de problemas

- **Se descarga `index.php` en lugar de abrirse:** selecciona una versión de PHP activa para el dominio.
- **No se ve el fondo o los estilos:** confirma que la carpeta `assets` se haya extraído junto a `index.php`.
- **Aparece un error 404:** comprueba que el ZIP se extrajo en la carpeta correcta y que `index.php` quedó en su raíz.
- **Se muestra el listado de archivos:** activa `Options -Indexes` en Apache o desactiva el listado de directorios en el panel del servidor.
