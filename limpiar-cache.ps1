[CmdletBinding()]
param(
    [ValidateRange(1, 365)]
    [int]$Dias = 2,
    [ValidateRange(0, 10000)]
    [double]$LibreMinimoGB = 0,
    [switch]$Aplicar
)

$ErrorActionPreference = "Stop"
if ($LibreMinimoGB -gt 0) {
    $disco = Get-CimInstance Win32_LogicalDisk |
        Where-Object { $_.DeviceID -eq [System.IO.Path]::GetPathRoot($env:TEMP).TrimEnd([char]92) } |
        Select-Object -First 1
    $libreGB = if ($null -ne $disco) { $disco.FreeSpace / 1GB } else { 0 }
    if ($libreGB -ge $LibreMinimoGB) {
        Write-Host ("Limpieza omitida: hay {0:N1} GB libres y el mínimo configurado es {1:N1} GB." -f $libreGB, $LibreMinimoGB)
        exit 0
    }
}
$corte = (Get-Date).AddDays(-$Dias)
$raizTemporal = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd([char]92)
$perfilesActivos = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "chrome.exe" -or $_.Name -eq "msedge.exe" } |
    ForEach-Object {
        if ($_.CommandLine -match "--user-data-dir=([^ ]+)") {
            [void]$perfilesActivos.Add([System.IO.Path]::GetFullPath($Matches[1]).TrimEnd([char]92))
        }
    }

$candidatos = New-Object System.Collections.Generic.List[object]
$omitidos = 0

function AgregarCandidatos([string]$ruta, [bool]$perfilCompleto) {
    if (-not (Test-Path -LiteralPath $ruta)) { return }
    $archivos = Get-ChildItem -LiteralPath $ruta -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $corte -and -not ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) }
    foreach ($archivo in $archivos) {
        $candidatos.Add([pscustomobject]@{ Ruta = $archivo.FullName; Tamano = $archivo.Length; PerfilCompleto = $perfilCompleto })
    }
}

# Perfiles temporales que Chrome/Edge crean para sesiones automatizadas. Solo se eliminan los
# inactivos y anteriores al corte; nunca el perfil que esté usando un navegador en ejecución.
if (Test-Path -LiteralPath $raizTemporal) {
    Get-ChildItem -LiteralPath $raizTemporal -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match "^[a-z0-9]{8}\.[a-z0-9]{3}$" -and
            $_.CreationTime -lt $corte -and
            -not $perfilesActivos.Contains($_.FullName.TrimEnd([char]92))
        } |
        ForEach-Object { AgregarCandidatos $_.FullName $true }
}

# Cachés normales de Chrome y Edge. Se borran archivos antiguos, no cookies, contraseñas ni historial.
$raicesNavegador = @(
    (Join-Path $env:LOCALAPPDATA "Google\Chrome\User Data"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\Edge\User Data")
)
foreach ($raiz in $raicesNavegador) {
    if (-not (Test-Path -LiteralPath $raiz)) { continue }
    $perfiles = Get-ChildItem -LiteralPath $raiz -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "Default" -or $_.Name -like "Profile *" }
    foreach ($perfil in $perfiles) {
        if ($perfilesActivos.Contains($perfil.FullName.TrimEnd([char]92))) { continue }
        foreach ($nombreCache in @("Cache", "Code Cache", "GPUCache")) {
            AgregarCandidatos (Join-Path $perfil.FullName $nombreCache) $false
        }
    }
}

$candidatos = @($candidatos | Sort-Object Ruta -Unique)
$totalBytes = ($candidatos | Measure-Object Tamano -Sum).Sum
if ($null -eq $totalBytes) { $totalBytes = 0 }

Write-Host ("Corte: archivos modificados antes del {0:dd/MM/yyyy HH:mm}" -f $corte)
Write-Host ("Perfiles de navegador activos protegidos: {0}" -f $perfilesActivos.Count)
Write-Host ("Archivos candidatos: {0:N0}" -f $candidatos.Count)
Write-Host ("Espacio recuperable: {0:N1} MB" -f ($totalBytes / 1MB))

if (-not $Aplicar) {
    Write-Host "Vista previa solamente. Para aplicar, ejecuta este mismo script con -Aplicar."
    exit 0
}

$liberado = 0L
$eliminados = 0
foreach ($candidato in $candidatos) {
    try {
        Remove-Item -LiteralPath $candidato.Ruta -Force -ErrorAction Stop
        $liberado += [long]$candidato.Tamano
        $eliminados++
    }
    catch {
        $omitidos++
    }
}

# Elimina carpetas vacías de los perfiles temporales ya limpiados, sin tocar perfiles activos.
if (Test-Path -LiteralPath $raizTemporal) {
    Get-ChildItem -LiteralPath $raizTemporal -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match "^[a-z0-9]{8}\.[a-z0-9]{3}$" -and
            -not $perfilesActivos.Contains($_.FullName.TrimEnd([char]92))
        } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object {
            try {
                if (-not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction Stop)) {
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
                }
            }
            catch { }
        }
}

Write-Host ("Archivos eliminados: {0:N0}" -f $eliminados)
Write-Host ("Espacio liberado: {0:N1} MB" -f ($liberado / 1MB))
Write-Host ("Archivos omitidos por estar en uso o protegidos: {0:N0}" -f $omitidos)
