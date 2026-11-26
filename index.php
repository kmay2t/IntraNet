<?php

declare(strict_types=1);

header('Content-Type: text/html; charset=UTF-8');
header('Cache-Control: no-store, no-cache, must-revalidate');
header('X-Content-Type-Options: nosniff');
header('X-Frame-Options: SAMEORIGIN');
header('Referrer-Policy: no-referrer');
header('Permissions-Policy: camera=(), microphone=(), geolocation=()');
header("Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'none'; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'self'");

$cards = [
    [
        'title' => 'Calendario de actividades',
        'description' => 'Crea actividades, incidentes y tickets y da seguimiento con comentarios, reportes y estatus.',
    ],
    [
        'title' => 'Inventario de equipos',
        'description' => 'Pestaña única de Ingresos: selector Almacén/HES/HGR/HGO/HI, botón Añadir y hoja de entrada y de salida en cada fila.',
    ],
    [
        'title' => 'Inventario de IPs',
        'description' => 'Direcciones IP por hospital, con búsqueda, alta, edición y descarga en Excel.',
    ],
    [
        'title' => 'Inventario de AETitle',
        'description' => 'AE Titles y nodos DICOM por hospital, con búsqueda, alta, edición y descarga en Excel.',
    ],
    [
        'title' => 'Playbook',
        'description' => 'Biblioteca de documentación técnica: manuales, procedimientos y guías en PDF.',
    ],
    [
        'title' => 'Actas administrativas',
        'description' => 'Captura, edita e imprime actas administrativas con folio, hechos, acuerdos y firmas.',
    ],
    [
        'title' => 'Credenciales',
        'description' => 'Genera credenciales ligadas a los usuarios con foto tamaño infantil, en el formato oficial de la empresa.',
    ],
    [
        'title' => 'Roles y permisos',
        'description' => 'Define los roles, qué puede hacer cada uno y los ajustes por usuario.',
    ],
    [
        'title' => 'Gestión de usuarios',
        'description' => 'Altas, bajas, desactivación, rol y permisos individuales de cada cuenta.',
    ],
    [
        'title' => 'Reportes',
        'description' => 'Bitácora de usuarios, histórico de actividades y reportes de estatus, organizados por pestañas con sus filtros y descarga a Excel.',
    ],
    [
        'title' => 'Bandeja de soporte',
        'description' => 'Lee y responde con el correo de soporte y, si el administrador lo conectó, también con tu segundo correo: tú eliges cuál ves y con cuál envías.',
    ],
];

$escape = static fn (?string $value): string => htmlspecialchars(
    $value ?? '',
    ENT_QUOTES | ENT_SUBSTITUTE,
    'UTF-8'
);

?>
<!doctype html>
<html lang="es">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="robots" content="noindex, nofollow">
    <meta name="referrer" content="no-referrer">
    <title>Sistemas Médicos · meditechnosys</title>
    <link rel="icon" href="assets/favicon.ico">
    <link rel="stylesheet" href="assets/css/portal.css?v=1.0.0">
</head>
<body>
    <div class="shell">
        <header class="site-header">
            <div class="brand" aria-label="meditechnosys">
                <span class="brand-name">medi<span>technosys</span></span>
                <span class="brand-subtitle">Medical Technology Solutions</span>
            </div>
            <span class="internal-badge">Uso interno</span>
        </header>

        <main id="contenido">
            <section class="medical-systems" aria-labelledby="sistemas-medicos">
                <div class="section-heading">
                    <h1 id="sistemas-medicos">Sistemas médicos</h1>
                </div>
                <p class="section-intro">Catálogo informativo de los sistemas médicos.</p>

                <div class="cards">
                    <?php foreach ($cards as $card): ?>
                        <article class="card">
                            <p class="card-tag">Sistemas médicos</p>
                            <h2><?= $escape($card['title']) ?></h2>
                            <p class="card-description"><?= $escape($card['description']) ?></p>
                        </article>
                    <?php endforeach; ?>
                </div>
            </section>
        </main>

        <footer class="site-footer">
            <span>meditechnosys · meditechnosys.mx</span>
            <span>Portal informativo · Versión 1.0.0</span>
        </footer>
    </div>
</body>
</html>
