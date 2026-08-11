# RepoAmigos

Mod de R.E.P.O. hecho a medida para jugar con los amigos.

## Qué hace

### 1. Extracción bajo demanda

En el juego original, en cuanto cubres la meta de haul el extractor espera 1,5 s y **se dispara solo**. Con el mod se queda **listo y esperando**: la extracción solo arranca cuando alguien va y pulsa el botón del payaso.

Ventaja para el grupo: da tiempo a seguir metiendo objetos para el excedente (*surplus*) y decidís vosotros cuándo cerrar.

Mientras espera, el extractor muestra `PULSA EL BOTON` en la pantalla del tubo.

### 2. Revivir en el extractor

En el juego original, la cabeza de un compañero muerto solo revive:
- en el camión, tras 2 s dentro, o
- en el extractor, pero **solo al completarse** la extracción.

Con el mod basta con **dejar la cabeza dentro de un extractor** y esperar unos segundos. No hace falta completar nada. Revive en el sitio (sin el curado completo del camión).

### 3. Roles secretos

Al empezar cada nivel se sortean cuatro roles entre los jugadores. Solo tú ves el tuyo. Hacen falta al menos 3 jugadores (configurable); por debajo de eso no se reparte nada.

| Rol | Qué puede hacer |
|---|---|
| **Médico** | `G` cura al compañero herido más cercano: **+20 PV**, **3 cargas**, y recupera **una carga cada 3 minutos**. No le cuesta vida propia. |
| **Ingeniero** | Es el **único** que puede activar la extracción pulsando el pato. Los demás pueden empujar el botón, pero no pasa nada. |
| **Saboteador** | `H` apaga **todas las luces del nivel durante 2 minutos**. Mientras dura, **aparecen enemigos más rápido**. Recarga: 15 minutos. |
| **Rastreador** | Cada **2 minutos** le avisa automáticamente de qué monstruo tiene más cerca, a qué distancia y en qué dirección (en horas de reloj). |

Notas de diseño:

- Las **linternas no se apagan**: son `FlashlightController`, no `PropLight`. El apagón deja el nivel a oscuras salvo tu linterna.
- Si por lo que sea **no hay ingeniero** en la partida (pocos jugadores, rol desactivado), el pato vuelve a funcionar para todos. Nunca se deja al grupo sin poder extraer por un rol que no existe.
- El médico **no gasta carga** si no había nadie a quien curar, ni sobre alguien que ya está al máximo.
- Muertos no se curan, se reviven: para eso está la función 2.

## Requisitos

**Todos los jugadores necesitan la misma DLL**, incluido el anfitrión. Si alguien no la tiene, verá comportamientos raros: el mod cambia decisiones que toma el *master client*.

## Instalar

**Con el gestor de mods** (recomendado, es lo que hay que pasarle a los amigos):

> Thunderstore Mod Manager (o r2modman) → **Settings** → **Import local mod** → elegir `Eddy-RepoAmigos-<versión>.zip` → elegir perfil → **Import**

**A mano**, copiando `RepoAmigos.dll` en:

```
%APPDATA%\Thunderstore Mod Manager\DataFolder\REPO\profiles\<PERFIL>\BepInEx\plugins\Eddy-RepoAmigos\
```

Arrancar **siempre** con el botón **Start modded** de Thunderstore Mod Manager. Arrancar desde Steam corre el juego en vanilla sin avisar.

## Configurar

Tras la primera partida se genera:

```
...\<PERFIL>\BepInEx\config\com.usuario.repoamigos.cfg
```

| Sección | Opción | Por defecto | Qué hace |
|---|---|---|---|
| `ExtraccionManual` | `Activado` | `true` | `false` devuelve la extracción automática original |
| `ExtraccionManual` | `AvisoEnPantalla` | `true` | Muestra el texto en el tubo del extractor |
| `ExtraccionManual` | `TextoAviso` | `PULSA EL BOTON` | El texto que aparece |
| `RevivirEnExtractor` | `Activado` | `true` | `false` desactiva el revivir |
| `RevivirEnExtractor` | `SegundosDeEspera` | `2.0` | Segundos dentro del extractor antes de revivir |
| `Roles` | `Activado` | `true` | Sortea los roles al empezar cada nivel |
| `Roles` | `MinimoJugadores` | `3` | Por debajo de esto no se reparte ningún rol |
| `Roles` | `RepartirCadaNivel` | `true` | `false` los sortea una vez para toda la partida |
| `Medico` | `Tecla` / `PuntosPorCura` / `Cargas` / `MinutosRecarga` / `Alcance` | `G` / `20` / `3` / `3` / `4` | |
| `Ingeniero` | `AvisarAlRechazar` | `true` | Explica por qué no funciona el botón |
| `Saboteador` | `Tecla` / `MinutosApagon` / `MinutosRecarga` / `FuerzaEnemigos` | `H` / `2` / `15` / `10` | |
| `Rastreador` | `MinutosEntreAvisos` / `AlcanceMaximo` | `2` / `60` | |
| `General` | `LogDetallado` | `false` | Mensajes extra en la consola de BepInEx |

Se puede editar sin recompilar. Cada jugador puede tener su propio archivo, pero **conviene que coincidan** para evitar confusión.

## Desarrollo

```powershell
.\compilar.ps1      # compila e instala en el perfil Default
.\compilar.ps1 -Perfil "OtroPerfil"
.\verificar.ps1     # valida los parches contra el código del juego
.\empaquetar.ps1    # genera dist\Eddy-RepoAmigos-<version>.zip para compartir
```

`empaquetar.ps1` lee la versión de `Plugin.cs` (única fuente de verdad), comprueba que `manifest.json` dice lo mismo, valida que el icono sea PNG de 256×256 exactos, compila, **pasa `verificar.ps1` obligatoriamente** y solo entonces comprime. Se empaqueta lo que va a manos de otra gente: una DLL con un parche mal emparejado carga sin error y no hace nada, y quien lo sufre es un amigo que no puede depurarlo.

Para subir una versión nueva hay que tocar **tres** sitios: `Plugin.cs`, `RepoAmigos.csproj` y `manifest.json`. El script para si los dos primeros no cuadran.

**Ejecutar `verificar.ps1` siempre después de tocar un parche.** Harmony empareja los parámetros por nombre en tiempo de ejecución: si un nombre no coincide con el del juego, compila con cero advertencias y solo falla al cargar la partida. Ese script detecta justo eso.

También valida los `AccessTools.Field(typeof(X), "campo")`, que es el mismo fallo mudo en otra forma: si el campo no existe, Harmony devuelve `null` y el código simplemente no hace nada, sin excepción.

Cerrar R.E.P.O. antes de compilar: con el juego abierto la DLL está bloqueada.

## Cómo funciona por dentro

Todo son parches Harmony sobre `Assembly-CSharp.dll`; no se modifica ningún archivo del juego.

**Extracción bajo demanda** — el final de `ExtractionPoint.HaulChecker` hace:

```csharp
if (haulGoal - haulCurrent <= 0 && haulGoalFetched) {
    successDelay -= Time.deltaTime;
    if (successDelay <= 0f) { StateSet(State.Success); return; }
}
```

Se bloquea ese `StateSet(Success)` con un prefix, salvo cuando el mod lo ha «armado» por pulsación. Además hay que reactivar `buttonGrabObject` cada frame, porque `ButtonToggle` hace `if (currentState != State.Idle) buttonGrabObject.enabled = false;` — o sea, vanilla deja el botón inservible en cuanto el extractor se activa.

**Revivir** — se replica el bloque `inTruckReviveTimer` de `PlayerDeathHead.Update` sobre `inExtractionPoint`, llamando al `PlayerDeathHead.Revive()` del propio juego (que usa `Revive(false)`, sin el `TruckHealer.Heal`).

**Red** — `ExtractionPoint.StateSet` y `PlayerAvatar.Revive` emiten RPC a todos, así que solo el *master client* puede invocarlos o se duplican. Cuando pulsa el botón un jugador que no es master, se envía un evento Photon propio (código `174`) al master para que ejecute él.

### Los roles

**Dónde vive el tic.** En `RunManager.Update`, **no** en `Plugin.Update`. Unity destruye el componente del plugin al cambiar de escena, así que un temporizador colgado de `Plugin.Update` dejaría de correr en cuanto empieza la primera partida — sin un solo error en el log. Los parches Harmony sí sobreviven.

**Identidad.** Los roles se guardan por SteamID (`SemiFunc.PlayerGetSteamID`), no por referencia al `PlayerAvatar`: entre niveles no hay garantía de que el objeto sea el mismo.

**Curar no necesita red.** `PlayerHealth.HealOther` manda `RpcTarget.All`, pero `HealOtherRPC` vuelve a filtrar por `photonView.IsMine`, así que solo la máquina del curado aplica la vida. Se puede llamar desde cualquier cliente sin duplicar — al revés que `Revive` o `StateSet`.

**El apagón, dos trampas.**

1. `LightManager.turnOffLights` **no es un interruptor**, es el pestillo del apagón de vanilla (salta con `allExtractionPointsCompleted`). Ponerlo a `true` no apaga nada; solo cancelaría el apagón final.
2. `PropLight.SetIntensity(x)` hace `originalIntensity = x`. Apagar con `SetIntensity(0)` **destruye el dato con el que se restauraría**.

Por eso se toca `lightComponent.enabled`, que no usa nadie más, y solo se apuntan las luces que estaban encendidas (para no encender al restaurar las que el culling tenía apagadas a propósito). `LightManager` es local y no emite RPC: el apagón se difunde con evento propio (`177`).

**Los enemigos solo obedecen al master.** `EnemyDirector.SetInvestigate` arranca con `if (!IsMasterClientOrSingleplayer()) return;`. La subida de generación usa `EnemyParent.DisableDecrease(float)`, la misma palanca que el juego usa para los ruidos gordos, y va por evento al master (`176`).

**Dos prefixes sobre `ExtractionPoint.OnClick`.** `ExtractionOnDemand.BotonLanzaExtraccion` y `RolIngeniero.SoloElIngenieroPulsa` parchean el mismo método, y `PatchAll` los aplica en el orden en que la reflexión devuelve los tipos: **no determinista**. Por eso la comprobación del ingeniero vive en `RolIngeniero.DebeBloquear()` y la llaman los dos, en vez de depender de quién gane la carrera.

**Reparto y los que entran tarde.** El master repite el reparto cada 20 s, porque quien entra con el nivel empezado se perdió el evento y tendría la tabla vacía (y el pato le funcionaría). `RecibirReparto` compara antes de aplicar: si no, `AlRepartir` le devolvería al médico sus 3 cargas cada 20 segundos.
