# =============================================================================
#  Empaqueta RepoAmigos en un .zip con formato Thunderstore.
#
#  El resultado se instala con "Import local mod" en Thunderstore Mod Manager
#  (o r2modman), y tambien vale tal cual para subirlo a thunderstore.io.
#
#  Uso:   .\empaquetar.ps1
#         .\empaquetar.ps1 -Salida "D:\paraLosAmigos"
#
#  La version NO se escribe aqui: se lee de Plugin.cs, que es la unica fuente
#  de verdad. Si manifest.json y el plugin no coinciden, el script para.
# =============================================================================

param(
    [string]$Salida = ''
)

$ErrorActionPreference = 'Stop'

$raiz  = $PSScriptRoot
$autor = 'Eddy'
$nombre = 'RepoAmigos'

if (-not $Salida) { $Salida = Join-Path $raiz 'dist' }

function Paso($texto) { Write-Host "`n>> $texto" -ForegroundColor Cyan }
function Muere($texto) { Write-Host "`n$texto" -ForegroundColor Red; exit 1 }

# --- Version: Plugin.cs manda -----------------------------------------------

$pluginCs = Get-Content (Join-Path $raiz 'Plugin.cs') -Raw
if ($pluginCs -notmatch 'PluginVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') {
    Muere "No encuentro PluginVersion en Plugin.cs."
}
$version = $Matches[1]

$manifestRuta = Join-Path $raiz 'manifest.json'
if (-not (Test-Path $manifestRuta)) { Muere "Falta manifest.json." }
$manifest = Get-Content $manifestRuta -Raw | ConvertFrom-Json

if ($manifest.version_number -ne $version) {
    Muere @"
Las versiones no coinciden:
  Plugin.cs      -> $version
  manifest.json  -> $($manifest.version_number)

Thunderstore rechaza subir dos veces la misma version, y una DLL que dice una
cosa dentro del juego y otra en el gestor de mods es imposible de depurar
cuando un amigo reporta un fallo. Iguala las dos y vuelve a lanzar.
"@
}

Write-Host "Empaquetando $autor-$nombre $version" -ForegroundColor White

# --- Comprobaciones que Thunderstore exige ----------------------------------

Paso 'Comprobando los requisitos del paquete...'

if ($manifest.name -notmatch '^[a-zA-Z0-9_]+$') {
    Muere "manifest.json: 'name' solo admite letras, numeros y guion bajo. Ahora es '$($manifest.name)'."
}
if ($manifest.description.Length -gt 250) {
    Muere "manifest.json: la descripcion pasa de 250 caracteres ($($manifest.description.Length))."
}

# El icono tiene que ser PNG de 256x256 EXACTOS o Thunderstore lo rechaza.
$iconoRuta = Join-Path $raiz 'icon.png'
if (-not (Test-Path $iconoRuta)) { Muere "Falta icon.png (PNG de 256x256)." }

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($iconoRuta)
$ancho = $img.Width; $alto = $img.Height
$img.Dispose()
if ($ancho -ne 256 -or $alto -ne 256) {
    Muere "icon.png tiene que ser 256x256 exactos; es $ancho x $alto."
}
Write-Host "  ok  icon.png 256x256"
Write-Host "  ok  manifest.json ($($manifest.description.Length)/250 caracteres)"

# --- Compilar ---------------------------------------------------------------

Paso 'Compilando en Release...'

if (Get-Process -Name 'REPO' -ErrorAction SilentlyContinue) {
    Muere "R.E.P.O. esta abierto. Cierralo: la DLL esta bloqueada."
}

dotnet build (Join-Path $raiz 'RepoAmigos.csproj') -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { Muere "Fallo la compilacion. No se empaqueta nada." }

$dll = Join-Path $raiz 'bin\Release\RepoAmigos.dll'
if (-not (Test-Path $dll)) { Muere "No encuentro la DLL en $dll" }

# --- Validar los parches ----------------------------------------------------
#
# Se empaqueta lo que se va a repartir a otra gente, asi que aqui NO es
# opcional: una DLL con un parche mal emparejado carga sin error y no hace
# nada, y el que lo sufre es un amigo que no puede depurarlo.

Paso 'Validando los parches contra el juego...'

# 6> silencia el flujo de informacion: verificar.ps1 imprime con Write-Host, que
# no pasa por la salida estandar y por tanto Out-Null no lo atrapa.
& (Join-Path $raiz 'verificar.ps1') 6>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Muere "verificar.ps1 ha encontrado fallos. Lanzalo a mano para ver cuales. No se empaqueta."
}
Write-Host "  ok  parches y campos por reflexion validados"

# --- Montar el paquete ------------------------------------------------------
#
# Estructura que espera Thunderstore: todo en la RAIZ del zip, sin carpeta
# contenedora. Es el mismo formato que usan REPOLib o MoreUpgrades.
#
#   manifest.json
#   icon.png
#   README.md
#   CHANGELOG.md
#   RepoAmigos.dll

Paso 'Montando el paquete...'

$staging = Join-Path $env:TEMP "repoamigos-pkg-$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item $manifestRuta $staging
Copy-Item $iconoRuta    $staging
Copy-Item $dll          $staging
Copy-Item (Join-Path $raiz 'CHANGELOG.md') $staging

# Thunderstore muestra README.md como pagina del mod, y GitHub tambien lo
# renderiza como portada del repo: por eso el texto vive ya en README.md y aqui
# solo hay que copiarlo. Antes se llamaba LEEME.md y habia que renombrarlo al
# vuelo, lo que dejaba el repo de GitHub sin portada.
Copy-Item (Join-Path $raiz 'README.md') $staging

foreach ($f in @('manifest.json','icon.png','README.md','CHANGELOG.md','RepoAmigos.dll')) {
    if (-not (Test-Path (Join-Path $staging $f))) { Muere "El paquete se ha quedado sin $f" }
    Write-Host "  + $f"
}

# --- Comprimir --------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$zip = Join-Path $Salida "$autor-$nombre-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$info = Get-Item $zip
Write-Host "`n  OK  $($info.FullName)" -ForegroundColor Green
Write-Host "      $([math]::Round($info.Length/1KB,1)) KB"

Write-Host @"

Para tus amigos (Thunderstore Mod Manager o r2modman):
  Settings  ->  Import local mod  ->  elegir este .zip  ->  perfil  ->  Import

Recuerda: LO NECESITAN TODOS, incluido el host, y hay que arrancar con
'Start modded'. Desde Steam el juego corre en vanilla sin avisar.
"@ -ForegroundColor DarkGray
Write-Host ""
