# Meditechnosys — Portal informativo

Portal web básico renderizado con PHP que muestra el catálogo de **Sistemas médicos**.

## Alcance

- Cuatro tarjetas informativas: consulta de reportes, visor DICOM, envío DICOM y consulta de lista de trabajo.
- No incluye botones «Abrir», enlaces a sistemas clínicos, formularios, JavaScript ni APIs.
- No requiere inicio de sesión, base de datos, Composer, procesos en segundo plano ni conexión con el PACS.
- Las rutas y los archivos de la versión ASP.NET se retiraron del proyecto.

## Requisitos

- PHP 7.4 o superior.
- Apache con PHP o cualquier servidor compatible que sirva archivos `.php`.
- No se necesitan extensiones adicionales.

## Estructura

```text
.
├── index.php                 # Portada y catálogo informativo
├── assets/
│   ├── favicon.ico
│   ├── css/portal.css        # Tema azul marino y dorado
│   └── img/synapse.png       # Emblema de fondo
├── .htaccess                 # Seguridad básica para Apache
└── intranet-cpanel.zip       # Paquete listo para subir a cPanel
```

## Probar localmente

Desde la carpeta del proyecto:

```bash
php -S localhost:8080
```

Después abre `http://localhost:8080`.

## Publicar en cPanel

1. Entra al **Administrador de archivos** de cPanel.
2. Sube `intranet-cpanel.zip` a `public_html` o a la subcarpeta elegida.
3. Extrae el ZIP en esa misma carpeta.
4. Comprueba que `index.php` quede en la raíz de la carpeta publicada.
5. Selecciona PHP 7.4 o superior en la configuración del dominio.
6. Abre el dominio y verifica que aparezcan las cuatro tarjetas sin botones.

No hace falta crear bases de datos, abrir puertos, instalar servicios ni ejecutar comandos por SSH.

## Versión

1.0.0 — Portal informativo PHP.
