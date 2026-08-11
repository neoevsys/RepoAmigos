# =============================================================================
#  Compila RepoAmigos y lo instala en el perfil de Thunderstore Mod Manager.
#
#  Uso:   .\compilar.ps1
#         .\compilar.ps1 -Perfil "OtroPerfil"
# =============================================================================

param(
    [string]$Perfil = 'Default'
)

$ErrorActionPreference = 'Stop'

$raiz       = $PSScriptRoot
$proyecto   = Join-Path $raiz 'RepoAmigos.csproj'
$perfilDir  = Join-Path $env:APPDATA "Thunderstore Mod Manager\DataFolder\REPO\profiles\$Perfil"
# Formato Autor-Nombre: es lo que Thunderstore espera para listarlo en "My mods".
$destino    = Join-Path $perfilDir 'BepInEx\plugins\Eddy-RepoAmigos'
$destinoViejo = Join-Path $perfilDir 'BepInEx\plugins\RepoAmigos'

function Paso($texto) { Write-Host "`n>> $texto" -ForegroundColor Cyan }

# --- Comprobaciones previas -------------------------------------------------

if (Get-Process -Name 'REPO' -ErrorAction SilentlyContinue) {
    Write-Host "`nR.E.P.O. esta abierto. Cierralo antes de compilar: la DLL esta bloqueada." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $perfilDir)) {
    Write-Host "`nNo existe el perfil '$Perfil' en:" -ForegroundColor Red
    Write-Host "  $perfilDir"
    Write-Host "Perfiles disponibles:" -ForegroundColor Yellow
    Get-ChildItem (Split-Path $perfilDir) -Directory | ForEach-Object { Write-Host "  - $($_.Name)" }
    exit 1
}

# --- Compilar ---------------------------------------------------------------

Paso 'Compilando...'
dotnet build $proyecto -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nFallo la compilacion. Nada que instalar." -ForegroundColor Red
    exit 1
}

$dll = Join-Path $raiz 'bin\Release\RepoAmigos.dll'
if (-not (Test-Path $dll)) {
    Write-Host "`nCompilo pero no encuentro la DLL en $dll" -ForegroundColor Red
    exit 1
}

# --- Instalar ---------------------------------------------------------------

Paso "Instalando en el perfil '$Perfil'..."

# Borrar la carpeta con el nombre antiguo. Si quedan las dos, BepInEx encuentra
# el mismo GUID dos veces y descarta una copia con un aviso.
if (Test-Path $destinoViejo) {
    Remove-Item $destinoViejo -Recurse -Force
    Write-Host "  Eliminada carpeta antigua 'RepoAmigos' (evita cargar el plugin dos veces)" -ForegroundColor DarkYellow
}

New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item $dll $destino -Force

$info = Get-Item (Join-Path $destino 'RepoAmigos.dll')
Write-Host "`n  OK  $($info.FullName)" -ForegroundColor Green
Write-Host "      $($info.Length) bytes  -  $($info.LastWriteTime)"

# --- Aviso: el mod en OTROS perfiles ------------------------------------------
#
# Thunderstore permite varios perfiles y cada uno tiene su propia carpeta de
# plugins. Este script instala en UNO, y si el juego se arranca desde otro se
# carga una version distinta sin un solo aviso: la DLL nueva esta en disco, el
# juego no la usa, y los arreglos "no hacen efecto" sin ninguna pista de por que.
#
# Paso de verdad: durante horas se instalo en 'Default' mientras la partida
# corria desde el perfil 'Eddy' con una version vieja.

$perfilesDir = Split-Path $perfilDir
$otros = Get-ChildItem $perfilesDir -Directory -ErrorAction SilentlyContinue |
         Where-Object { $_.Name -ne $Perfil }

$desajuste = @()
foreach ($p in $otros) {
    $dll = Join-Path $p.FullName 'BepInEx\plugins\Eddy-RepoAmigos\RepoAmigos.dll'
    if (-not (Test-Path $dll)) { continue }
    $v = (Get-Item $dll).VersionInfo.FileVersion
    $desajuste += [pscustomobject]@{ Perfil = $p.Name; Version = $v }
}

if ($desajuste.Count -gt 0) {
    $mia = $info.VersionInfo.FileVersion
    Write-Host "`n  AVISO: el mod tambien esta instalado en otros perfiles:" -ForegroundColor Yellow
    foreach ($d in $desajuste) {
        $marca = if ($d.Version -eq $mia) { 'igual' } else { 'DISTINTA' }
        $color = if ($d.Version -eq $mia) { 'DarkGray' } else { 'Red' }
        Write-Host ("    {0,-12} {1,-10} {2}" -f $d.Perfil, $d.Version, $marca) -ForegroundColor $color
    }
    Write-Host "    Acabas de instalar la $mia en '$Perfil'." -ForegroundColor Yellow
    Write-Host "    Si arrancas el juego desde otro perfil, se cargara SU version." -ForegroundColor Yellow
    Write-Host "    Para instalar ahi:  .\compilar.ps1 -Perfil `"<nombre>`"" -ForegroundColor Yellow
}

Write-Host "`nListo. Arranca REPO desde el boton 'Start modded' de Thunderstore Mod Manager." -ForegroundColor Green
Write-Host "La config se genera en:" -ForegroundColor DarkGray
Write-Host "  $perfilDir\BepInEx\config\com.usuario.repoamigos.cfg" -ForegroundColor DarkGray
Write-Host ""
