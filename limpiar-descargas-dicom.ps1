[CmdletBinding()]
param(
    [string]$RutaDescargas = "C:\Intranet_Definitiva\publish\dicom_downloads",
    [ValidateRange(1, 365)]
    [int]$Dias = 2,
    [ValidateRange(0, 10000)]
    [double]$LibreMinimoGB = 50,
    [switch]$Aplicar
)

$ErrorActionPreference = "Stop"
$raiz = [System.IO.Path]::GetFullPath($RutaDescargas).TrimEnd([char]92)
if (-not (Test-Path -LiteralPath $raiz)) {
    Write-Host "La carpeta de descargas DICOM no existe; no hay nada que limpiar."
    exit 0
}

if ($LibreMinimoGB -gt 0) {
    $disco = Get-CimInstance Win32_LogicalDisk |
        Where-Object { $_.DeviceID -eq [System.IO.Path]::GetPathRoot($raiz).TrimEnd([char]92) } |
        Select-Object -First 1
    $libreGB = if ($null -ne $disco) { $disco.FreeSpace / 1GB } else { 0 }
    if ($libreGB -ge $LibreMinimoGB) {
        Write-Host ("Limpieza DICOM omitida: hay {0:N1} GB libres y el mínimo configurado es {1:N1} GB." -f $libreGB, $LibreMinimoGB)
        exit 0
    }
}

$corte = (Get-Date).AddDays(-$Dias)
$candidatos = New-Object System.Collections.Generic.List[object]

function AgregarArchivos([string]$carpeta) {
    Get-ChildItem -LiteralPath $carpeta -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.LastWriteTime -lt $corte -and
            -not ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
        } |
        ForEach-Object { $candidatos.Add([pscustomobject]@{ Ruta = $_.FullName; Tamano = $_.Length }) }
}

# Una carpeta de trabajo reciente se conserva completa. Si contiene un marcador de bloqueo,
# también se conserva, aunque algún archivo individual sea antiguo.
Get-ChildItem -LiteralPath $raiz -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $corte } |
    ForEach-Object {
        $marcador = Get-ChildItem -LiteralPath $_.FullName -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "^(\.activo|.*\.lock)$" } |
            Select-Object -First 1
        if (-not $marcador) { AgregarArchivos $_.FullName }
    }

# ZIP y otros archivos sueltos antiguos en la raíz de descargas.
Get-ChildItem -LiteralPath $raiz -File -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $corte -and -not ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) } |
    ForEach-Object { $candidatos.Add([pscustomobject]@{ Ruta = $_.FullName; Tamano = $_.Length }) }

$candidatos = @($candidatos | Sort-Object Ruta -Unique)
$totalBytes = ($candidatos | Measure-Object Tamano -Sum).Sum
if ($null -eq $totalBytes) { $totalBytes = 0 }
Write-Host ("Corte DICOM: {0:dd/MM/yyyy HH:mm}" -f $corte)
Write-Host ("Archivos candidatos: {0:N0}" -f $candidatos.Count)
Write-Host ("Espacio recuperable: {0:N2} GB" -f ($totalBytes / 1GB))

if (-not $Aplicar) {
    Write-Host "Vista previa solamente. Para aplicar, ejecuta este mismo script con -Aplicar."
    exit 0
}

$liberado = 0L
$eliminados = 0
$omitidos = 0
foreach ($candidato in $candidatos) {
    try {
        Remove-Item -LiteralPath $candidato.Ruta -Force -ErrorAction Stop
        $liberado += [long]$candidato.Tamano
        $eliminados++
    }
    catch { $omitidos++ }
}

Get-ChildItem -LiteralPath $raiz -Directory -Force -ErrorAction SilentlyContinue |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        try {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction Stop)) {
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
            }
        }
        catch { }
    }

Write-Host ("Archivos DICOM eliminados: {0:N0}" -f $eliminados)
Write-Host ("Espacio DICOM liberado: {0:N2} GB" -f ($liberado / 1GB))
Write-Host ("Archivos omitidos por estar en uso o protegidos: {0:N0}" -f $omitidos)
