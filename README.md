# Meditechnosys — Portal informativo

Portal web autocontenido que muestra el catálogo actual de **Sistemas médicos**. Está disponible en dos versiones: PHP para hosting compartido y Node.js para **cPanel AI App Hosting**.

## Alcance

- Once tarjetas informativas con el contenido actual del portal.
- No incluye botones «Abrir», enlaces a sistemas clínicos, formularios, JavaScript ni APIs JSON.
- No requiere inicio de sesión, base de datos, Composer, paquetes npm ni conexión con el PACS.
- La versión Node.js usa únicamente módulos incluidos en Node.js.

## Archivos de entrega

```text
.
├── index.php                 # Portada PHP
├── assets/                   # Recursos de la versión PHP
├── .htaccess                 # Seguridad básica para Apache
├── intranet-cpanel.zip       # Paquete PHP para Administrador de archivos
├── nodejs/
│   ├── app.js                # Servidor Node.js
│   ├── package.json          # Inicio con npm start
│   ├── package-lock.json
│   └── public/               # HTML, CSS e imágenes de la versión Node.js
└── intranet-nodejs.zip       # Paquete para cPanel AI App Hosting
```

## Probar localmente

### PHP

```bash
php -S localhost:8080
```

Abre `http://localhost:8080`.

### Node.js

```bash
cd nodejs
npm start
```

Abre `http://localhost:3000`. Para usar otro puerto, define la variable `PORT`.

## Publicar Node.js en cPanel

1. En **Sitios web**, inicia la creación de un sitio y selecciona **AI App Hosting**.
2. Pulsa **Continuar**.
3. En **Origen**, selecciona **Subir un archivo**.
4. Sube `intranet-nodejs.zip`.
5. Espera a que termine la implementación y abre la dirección HTTPS que genere cPanel.

No uses **WordPress**, **SITEJET** ni **Repositorio Git**. Tampoco pegues la URL de ejemplo de GitHub. La aplicación ya incluye `package.json` y el comando de inicio `npm start`.

## Publicar PHP en cPanel

Esta opción es para un hosting compartido con PHP:

1. Abre **Administrador de archivos**.
2. Entra a `public_html` o a la subcarpeta del dominio.
3. Sube y extrae `intranet-cpanel.zip`.
4. Comprueba que `index.php` y `assets` queden juntos.
5. Selecciona PHP 7.4 o superior en **MultiPHP Manager**.
6. Abre el dominio.

El asistente **«Implementar una aplicación de Node.js»** no debe usarse para el paquete PHP.

## Versión

1.0.0 — Portal informativo PHP y Node.js.
