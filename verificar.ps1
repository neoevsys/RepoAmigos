# =============================================================================
#  Valida los parches Harmony CONTRA el codigo real del juego, sin arrancarlo.
#
#  Harmony empareja por nombre en tiempo de ejecucion, asi que estos fallos
#  compilan sin una sola advertencia y solo revientan al cargar la partida:
#
#    - [HarmonyPatch(typeof(X), "Metodo")] con un metodo que no existe
#    - un parametro del Prefix/Postfix que no coincide con el del juego
#    - un ___campo inyectado que no existe en la clase
#
#  Uso:  .\verificar.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

$perfil = Join-Path $env:APPDATA 'Thunderstore Mod Manager\DataFolder\REPO\profiles\Default'
$cecil  = Join-Path $perfil 'BepInEx\core\Mono.Cecil.dll'
$juego  = 'D:\SteamLibrary\steamapps\common\REPO\REPO_Data\Managed\Assembly-CSharp.dll'
$mod    = Join-Path $PSScriptRoot 'bin\Release\RepoAmigos.dll'

foreach ($p in @($cecil, $juego, $mod)) {
    if (-not (Test-Path $p)) { Write-Host "No encuentro: $p" -ForegroundColor Red; exit 1 }
}

Add-Type -Path $cecil
$asmJuego = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($juego)
$asmMod   = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($mod)

# Parametros que Harmony inyecta y que NO tienen que existir en el metodo original.
$especiales = @('__instance', '__result', '__state', '__originalMethod', '__args', '__runOriginal')

$errores = 0
$checks  = 0

function Err($texto) { Write-Host "  FALLO  $texto" -ForegroundColor Red; $script:errores++ }
function Ok($texto)  { Write-Host "  ok     $texto" -ForegroundColor DarkGray; $script:checks++ }

Write-Host "`nValidando parches Harmony contra Assembly-CSharp.dll`n" -ForegroundColor Cyan

foreach ($tipo in $asmMod.MainModule.GetTypes()) {

    # Localizar el [HarmonyPatch(typeof(X), "Y")] de la clase de parche
    $attr = $tipo.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' -and $_.ConstructorArguments.Count -ge 2 }
    if (-not $attr) { continue }

    $nombreTipoObjetivo = $attr.ConstructorArguments[0].Value.Name
    $nombreMetodo       = $attr.ConstructorArguments[1].Value

    Write-Host "$($tipo.Name)  ->  $nombreTipoObjetivo.$nombreMetodo" -ForegroundColor White

    # 1) el tipo objetivo existe
    $tipoObjetivo = $asmJuego.MainModule.GetTypes() | Where-Object { $_.Name -eq $nombreTipoObjetivo }
    if (-not $tipoObjetivo) { Err "la clase '$nombreTipoObjetivo' no existe en el juego"; continue }

    # 2) el metodo objetivo existe
    $metodoObjetivo = $tipoObjetivo.Methods | Where-Object { $_.Name -eq $nombreMetodo }
    if (-not $metodoObjetivo) { Err "'$nombreTipoObjetivo' no tiene ningun metodo '$nombreMetodo'"; continue }
    Ok "metodo objetivo encontrado"

    $paramsObjetivo = @($metodoObjetivo[0].Parameters | ForEach-Object { $_.Name })
    $camposObjetivo = @($tipoObjetivo.Fields | ForEach-Object { $_.Name })

    # 3) validar cada Prefix/Postfix
    #
    # Los Transpiler quedan fuera a proposito: Harmony empareja sus parametros
    # por TIPO (IEnumerable<CodeInstruction>, ILGenerator, MethodBase), no por
    # nombre, asi que compararlos con los del metodo original da falsos fallos.
    foreach ($parche in ($tipo.Methods | Where-Object { $_.Name -in @('Prefix','Postfix','Finalizer') })) {
        foreach ($p in $parche.Parameters) {
            $n = $p.Name

            if ($n -in $especiales) { continue }

            if ($n.StartsWith('___')) {
                $campo = $n.Substring(3)
                if ($camposObjetivo -contains $campo) { Ok "campo inyectado ___$campo" }
                else { Err "'$nombreTipoObjetivo' no tiene el campo '$campo' (parametro ___$campo)" }
                continue
            }

            if ($paramsObjetivo -contains $n) { Ok "parametro '$n' coincide" }
            else {
                $lista = if ($paramsObjetivo.Count) { $paramsObjetivo -join ', ' } else { '(ninguno)' }
                Err "'$n' no es parametro de $nombreTipoObjetivo.$nombreMetodo  --  los reales son: $lista"
            }
        }
    }
    Write-Host ""
}

# =============================================================================
#  Campos buscados por reflexion:  AccessTools.Field(typeof(X), "campo")
#
#  Harmony devuelve null en silencio si el campo no existe, y el mod se queda
#  sin hacer nada sin lanzar una sola excepcion. Es el mismo tipo de fallo mudo
#  que los parches mal emparejados, asi que se valida igual.
#
#  En IL la llamada se ve asi:
#      ldtoken   <tipo>
#      call      System.Type::GetTypeFromHandle
#      ldstr     "<campo>"
#      call      FieldInfo AccessTools::Field(Type, String)
# =============================================================================

Write-Host "Validando campos buscados con AccessTools.Field`n" -ForegroundColor Cyan

foreach ($tipo in $asmMod.MainModule.GetTypes()) {
    foreach ($metodo in $tipo.Methods) {
        if (-not $metodo.HasBody) { continue }

        $ins = @($metodo.Body.Instructions)
        for ($i = 0; $i -lt $ins.Count; $i++) {

            if ("$($ins[$i].Operand)" -notmatch 'AccessTools::Field') { continue }

            # Retroceder hasta el ldstr (nombre) y el ldtoken (tipo) que lo alimentan.
            $nombreCampo = $null
            $tipoDestino = $null
            for ($j = $i - 1; $j -ge 0 -and $j -ge $i - 6; $j--) {
                if (-not $nombreCampo -and $ins[$j].OpCode.ToString() -eq 'ldstr')   { $nombreCampo = $ins[$j].Operand }
                if (-not $tipoDestino -and $ins[$j].OpCode.ToString() -eq 'ldtoken') { $tipoDestino = $ins[$j].Operand.Name }
            }
            if (-not $nombreCampo -or -not $tipoDestino) { continue }

            $t = $asmJuego.MainModule.GetTypes() | Where-Object { $_.Name -eq $tipoDestino }
            if (-not $t) { Err "AccessTools.Field: la clase '$tipoDestino' no existe en el juego"; continue }

            if ($t.Fields | Where-Object { $_.Name -eq $nombreCampo }) {
                Ok "$tipoDestino.$nombreCampo"
            } else {
                Err "'$tipoDestino' no tiene el campo '$nombreCampo'  (AccessTools.Field en $($tipo.Name))"
            }
        }
    }
}

Write-Host ""
Write-Host ("-" * 60)
if ($errores -eq 0) {
    Write-Host "$checks comprobaciones, todo correcto. Los parches deberian enganchar." -ForegroundColor Green
    exit 0
} else {
    Write-Host "$errores fallo(s) sobre $($checks + $errores) comprobaciones. Corrigelos ANTES de jugar." -ForegroundColor Red
    exit 1
}
