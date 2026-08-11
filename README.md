# RepoAmigos

A [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) mod built for playing with friends: secret roles, manual extraction, reviving at the extraction point, and a tidier cart.

*Español más abajo — [ir a la versión en español](#repoamigos-español).*

---

# English

## Table of contents

- [What it does](#what-it-does)
- [Requirements](#requirements)
- [Installing](#installing)
- [Configuration](#configuration)
- [Development](#development)
- [How it works inside](#how-it-works-inside)

## What it does

### 1. Manual extraction

In vanilla, the moment you cover the haul goal the extraction point waits 1.5 s and **fires on its own**. With the mod it stays **ready and waiting**: extraction only starts when somebody presses the button.

That gives the group time to keep hauling for surplus and to decide when to close.

There are **three** ways to trigger it, and all of them work:

| Route | What it is |
|---|---|
| `PhysGrabObject.GrabStarted` | the rubber duck — the one people actually use |
| `ExtractionPoint.OnClick` | the small side button |
| `ExtractionPoint.OnShopClick` | the big shop-style button |

While it waits, the tube screen reads `PULSA EL BOTON` (configurable).

If you press before it is ready, the mod **tells you why** instead of doing nothing:

- `Este extractor todavia no esta activo` — walk into it first
- `Faltan 1.234 de botin` — bring more loot

### 2. Reviving at the extraction point

In vanilla a dead teammate's head only revives in the truck (2 s inside), or at the extraction point **but only once extraction completes**.

With the mod it is enough to **leave the head inside an extraction point** and wait a couple of seconds. Nothing has to be completed. The player revives on the spot, without the truck's full heal.

### 3. Secret roles

At the start of every level four roles are drawn among the players. **Only you see yours.** At least 3 players are needed by default; below that nothing is drawn.

| Role | Key | What it does |
|---|---|---|
| **Medic** | `G` | Heals the nearest wounded teammate in sight: **+20 HP**, **3 charges**, one charge back every **3 minutes**. Costs no health of your own. |
| **Engineer** | `J` | Fully recharges the battery of whatever you are **holding in your hands** — guns, drones, trackers, anything with a battery. **5 repairs**, one back every **5 minutes**. |
| **Saboteur** | `H` | Kills **every light in the level for 2 minutes**. While it lasts, enemies spawn faster. Cooldown: 15 minutes. |
| **Tracker** | — | Every **2 minutes** you are automatically told which monster is closest, how far away, and in which direction (as clock hours). |

Press **`Tab`** at any time to be reminded which role you have and how to use it. It also shows live state — the medic's line tells you how many charges you have *right now*. If you have no role it tells you *why* (roles off, not enough players, or simply not drawn this level) instead of doing nothing.

Design notes:

- **Flashlights are not affected** by the blackout: they are `FlashlightController`, not `PropLight`. The level goes dark except for your own torch.
- The medic **does not spend a charge** if there was nobody to heal, or on somebody already at full health.
- Dead players are not healed, they are revived — that is what feature 2 is for.
- No role blocks progression. Any player can extract; roles only grant advantages.

### 4. Smaller loot inside the C.A.R.T.

Loot shrinks to 75 % while it is inside the cart and returns to full size when it leaves, so more fits and you can see what you are carrying.

By default **only valuables** shrink. Some things are never touched, because shrinking them broke either the level or the object itself: equippable items, players, enemies, death heads, doors and hinges.

### 5. Version check

If somebody in the lobby is running a different version of the mod — or none at all — everyone gets a warning. A version mismatch breaks the mod in ways that are very hard to diagnose from inside the game.

## Requirements

- **[BepInEx 5](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/)**
- **Every player needs the same DLL**, host included.

That second point is not optional. The mod changes decisions the *master client* makes, so a lobby with mixed versions will behave strangely.

## Installing

**With a mod manager** (recommended — this is what you hand to your friends):

> Thunderstore Mod Manager or r2modman → **Settings** → **Import local mod** → pick `Eddy-RepoAmigos-<version>.zip` → pick a profile → **Import**

**By hand**, copy `RepoAmigos.dll` into:

```
%APPDATA%\Thunderstore Mod Manager\DataFolder\REPO\profiles\<PROFILE>\BepInEx\plugins\Eddy-RepoAmigos\
```

Always launch with **Start modded**. Launching from Steam runs the game vanilla, silently, with no warning at all.

## Configuration

After the first run you get:

```
...\<PROFILE>\BepInEx\config\com.usuario.repoamigos.cfg
```

Everything can be edited without recompiling. Each player can have their own file, but **keeping them in sync avoids confusion**.

### Roles

| Section | Option | Default | What it does |
|---|---|---|---|
| `Roles` | `Activado` | `true` | Draw roles at the start of each level |
| `Roles` | `MinimoJugadores` | `3` | Below this, no roles are drawn at all |
| `Roles` | `RepartirCadaNivel` | `true` | `false` draws once for the whole run |
| `Roles` | `AnunciarRol` | `true` | Show your role when the level starts |
| `Roles` | `RecordarRol` | `true` | Let you ask again with a key |
| `Roles` | `TeclaRecordar` | `Tab` | The key that reminds you of your role |
| `Medico` | `Activado` / `Tecla` / `PuntosPorCura` / `Cargas` / `MinutosRecarga` / `Alcance` | `true` / `G` / `20` / `3` / `3` / `4` | |
| `Ingeniero` | `Activado` / `Tecla` / `Cargas` / `MinutosRecarga` | `true` / `J` / `5` / `5` | |
| `Saboteador` | `Activado` / `Tecla` / `MinutosApagon` / `MinutosRecarga` / `SubirEnemigos` / `FuerzaEnemigos` | `true` / `H` / `2` / `15` / `true` / `10` | |
| `Rastreador` | `Activado` / `MinutosEntreAvisos` / `AlcanceMaximo` / `DecirNombre` | `true` / `2` / `60` / `true` | |

### Everything else

| Section | Option | Default | What it does |
|---|---|---|---|
| `ExtraccionManual` | `Activado` | `true` | `false` restores vanilla automatic extraction |
| `ExtraccionManual` | `AvisoEnPantalla` / `TextoAviso` | `true` / `PULSA EL BOTON` | The text on the tube screen |
| `ExtraccionManual` | `UsarPato` / `PatoNombreDelItem` | `true` / `Rubber Duck` | Use a rubber duck as the button |
| `ExtraccionManual` | `PatoEscala` / `PatoGiroX` / `PatoGiroY` / `PatoGiroZ` | `2.0` / `0` / `0` / `0` | Size and orientation of the duck |
| `ExtraccionManual` | `BotonGrandeDeTienda` / `SoloElBoton` / `PiezasDelBoton` | `true` / `true` / — | Reuse the shop pedestal as a button |
| `ExtraccionManual` | `BotonLateral` / `BotonAltura` / `BotonGiro` / `BotonHaciaFuera` / `BotonEscala` | `2.5` / `1.1` / `0` / `1.0` / `1.0` | Where that pedestal sits |
| `RevivirEnExtractor` | `Activado` / `SegundosDeEspera` | `true` / `2.0` | Seconds inside before reviving |
| `Carrito` | `Activado` / `EscalaDentro` / `SoloValiosos` | `true` / `0.75` / `true` | Shrink loot inside the cart |
| `Version` | `AvisarDesajuste` | `true` | Warn when players run different versions |
| `Pruebas` | `Activado` / `Tecla` / `ValorMinimo` / `ValorMaximo` / `CantidadPorPulsacion` | `true` / `F3` / `2000` / `9000` / `1` | Spawn test loot (development) |
| `General` | `LogDetallado` | `false` | Extra messages in the BepInEx console |

## Development

```powershell
.\compilar.ps1                      # build and install into the Default profile
.\compilar.ps1 -Perfil "OtherOne"   # ...or into another one
.\verificar.ps1                     # validate patches against the game's code
.\empaquetar.ps1                    # build dist\Eddy-RepoAmigos-<version>.zip
```

**Always run `verificar.ps1` after touching a patch.** Harmony matches parameters **by name at runtime**: a name that does not match the game compiles with zero warnings and only blows up when the save loads. The script catches exactly that, without opening the game, by reading `Assembly-CSharp.dll` with Mono.Cecil.

It also validates every `AccessTools.Field(typeof(X), "field")`, which is the same silent failure in another shape: if the field does not exist Harmony returns `null` and the code simply does nothing, no exception. Note it can only check **literal** field names — passing the name through a variable compiles fine and silently loses the safety net.

A version bump means **three** files: `Plugin.cs`, `RepoAmigos.csproj` and `manifest.json`. `empaquetar.ps1` stops if the first two disagree, and refuses to package unless `verificar.ps1` passes — what gets packaged goes to other people, and a mismatched patch loads without error and does nothing.

Close R.E.P.O. before building: the DLL is locked while the game runs.

## How it works inside

Everything is Harmony patches over `Assembly-CSharp.dll`. No game file is modified.

### Manual extraction

The tail of `ExtractionPoint.HaulChecker` does:

```csharp
if (haulGoal - haulCurrent <= 0 && haulGoalFetched) {
    successDelay -= Time.deltaTime;
    if (successDelay <= 0f) { StateSet(State.Success); return; }
}
```

That `StateSet(Success)` is blocked with a prefix unless the mod has *armed* it through a button press. `buttonGrabObject` also has to be re-enabled every frame, because `ButtonToggle` does `if (currentState != State.Idle) buttonGrabObject.enabled = false;` — vanilla makes the button useless the instant the extraction point activates.

### Networking and authority

`ExtractionPoint.StateSet` and `PlayerAvatar.Revive` fire RPCs to everyone, so only the master client may call them or they run twice. When a non-master presses, a custom Photon event goes to the master instead.

Custom event codes (PUN reserves 200+):

| Code | Direction | Purpose |
|---|---|---|
| `174` | client → master | extraction requested |
| `175` | master → all | role assignment |
| `176` | client → master | raise enemy spawn rate during sabotage |
| `177` | anyone → all | blackout |
| `178` | anyone → all | version check |
| `179` | client → master | engineer battery recharge |

Healing is the exception that needs no event: `PlayerHealth.HealOther` sends `RpcTarget.All`, but `HealOtherRPC` filters again by `photonView.IsMine`, so only the healed player's machine applies it.

### Where the tick lives

In `RunManager.Update`, **not** `Plugin.Update`. Unity destroys the plugin component on scene change, so a timer hanging off `Plugin.Update` would stop running the moment the first level starts — without a single error in the log. Harmony patches do survive.

Roles are keyed by SteamID (`SemiFunc.PlayerGetSteamID`), not by `PlayerAvatar` reference: across levels there is no guarantee the object is the same.

### The blackout, two traps

1. `LightManager.turnOffLights` **is not a switch**. It is the latch for vanilla's own end-of-level blackout (`allExtractionPointsCompleted`). Setting it to `true` turns nothing off; it would only cancel that final blackout.
2. `PropLight.SetIntensity(x)` assigns `originalIntensity = x`. Turning lights off with `SetIntensity(0)` **destroys the value you would restore them with**.

So the mod toggles `lightComponent.enabled`, which nobody else touches, and only records lights that were actually on — otherwise restoring would switch on lights that culling had deliberately left off.

### Engineer batteries, two more traps

Both were found by reading the game's IL, not by guessing:

1. `ItemBattery.SetBatteryLife` **cannot revive a dead battery**. It opens with `if (batteryLife > 0) { ...apply... } else { batteryLife = 0; }` — precisely the case the role exists to fix. The field is public, so it gets a positive nudge first.
2. Only the host can propagate the change. `BatteryFullPercentChange` ends in `else return` when you are not the master, silently. A client engineer therefore sends event `179` and the host performs the recharge.

### The cart

A single Postfix on `PhysGrabCart.ObjectsInCart` — the game's own answer to "what is in the cart".

The previous implementation hooked `PhysGrabObjectImpactDetector.OnTriggerStay` and could never have worked for clients: on a non-master machine **every `PhysGrabObject` stays kinematic for life**. `Awake` sets `rb.isKinematic = true` and only `EnableRigidbody()` undoes it, which runs solely under `!Multiplayer || IsMasterClient`. Unity does not deliver `OnTriggerStay` to sleeping bodies.

`ObjectsInCart` is the right hook because it is called with no master gate, it uses `Physics.OverlapBox` — a scene query that ignores rigidbody sleep — and it throttles itself to one sweep every 0.5 s. Two reconciliations per second instead of thousands of physics callbacks.

Scale is treated as dangerous on purpose: `ItemEquippable` uses it as a **state variable** (`StateEquipped` checks `magnitude < 0.1f`), so writing a weapon's scale at the wrong moment can break it permanently. Hence the whitelist and the forced restore on level change.

## License

MIT — see [LICENSE](LICENSE).

---

<a name="repoamigos-español"></a>

# RepoAmigos (Español)

Mod de [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) hecho a medida para jugar con los amigos: roles secretos, extracción manual, revivir en el extractor y un carrito más ordenado.

## Índice

- [Qué hace](#qué-hace)
- [Requisitos](#requisitos-1)
- [Instalar](#instalar)
- [Configurar](#configurar)
- [Desarrollo](#desarrollo-1)
- [Cómo funciona por dentro](#cómo-funciona-por-dentro)

## Qué hace

### 1. Extracción bajo demanda

En el juego original, en cuanto cubres la meta de botín el extractor espera 1,5 s y **se dispara solo**. Con el mod se queda **listo y esperando**: la extracción solo arranca cuando alguien pulsa el botón.

Eso os da tiempo a seguir metiendo objetos para el excedente y a decidir vosotros cuándo cerrar.

Hay **tres** formas de lanzarla, y las tres funcionan:

| Ruta | Qué es |
|---|---|
| `PhysGrabObject.GrabStarted` | el pato de goma — el que se usa de verdad |
| `ExtractionPoint.OnClick` | el botón lateral pequeño |
| `ExtractionPoint.OnShopClick` | el botón grande estilo tienda |

Mientras espera, la pantalla del tubo muestra `PULSA EL BOTON` (configurable).

Si pulsas antes de tiempo, el mod **te dice por qué** en vez de quedarse callado:

- `Este extractor todavia no esta activo` — hay que entrar en él primero
- `Faltan 1.234 de botin` — hay que traer más cosas

### 2. Revivir en el extractor

En el juego original, la cabeza de un compañero muerto solo revive en el camión (2 s dentro), o en el extractor **pero solo al completarse** la extracción.

Con el mod basta con **dejar la cabeza dentro de un extractor** y esperar unos segundos. No hace falta completar nada. Revive en el sitio, sin el curado completo del camión.

### 3. Roles secretos

Al empezar cada nivel se sortean cuatro roles entre los jugadores. **Solo tú ves el tuyo.** Hacen falta al menos 3 jugadores por defecto; por debajo de eso no se reparte nada.

| Rol | Tecla | Qué hace |
|---|---|---|
| **Médico** | `G` | Cura al compañero herido más cercano que tenga a la vista: **+20 PV**, **3 cargas**, y recupera una cada **3 minutos**. No le cuesta vida propia. |
| **Ingeniero** | `J` | Recarga a tope la batería de lo que lleve **en las manos** — armas, drones, rastreadores, cualquier cosa con batería. **5 reparaciones**, una cada **5 minutos**. |
| **Saboteador** | `H` | Apaga **todas las luces del nivel durante 2 minutos**. Mientras dura, aparecen enemigos más rápido. Recarga: 15 minutos. |
| **Rastreador** | — | Cada **2 minutos** le avisan de qué monstruo tiene más cerca, a qué distancia y en qué dirección (en horas de reloj). |

Pulsa **`Tab`** cuando quieras para que te recuerde qué rol tienes y cómo se usa. Trae el estado del momento: la línea del médico dice cuántas cargas te quedan **ahora**. Y si no tienes rol te dice **por qué** (roles apagados, falta gente, o simplemente no te tocó) en vez de no hacer nada.

Notas de diseño:

- Las **linternas no se apagan** con el apagón: son `FlashlightController`, no `PropLight`. El nivel se queda a oscuras salvo tu linterna.
- El médico **no gasta carga** si no había a quien curar, ni sobre alguien que ya está al máximo.
- A los muertos no se les cura, se les revive: para eso está la función 2.
- **Ningún rol bloquea el avance.** Extrae cualquiera; los roles solo dan ventajas.

### 4. Botín más pequeño en el C.A.R.T.

El botín se encoge al 75 % mientras está dentro del carrito y recupera su tamaño al salir, así cabe más y se ve mejor lo que llevas.

Por defecto **solo se encoge el botín**. Hay cosas que no se tocan nunca, porque encogerlas rompía el nivel o el propio objeto: armas equipables, jugadores, enemigos, cabezas de muerto, puertas y bisagras.

### 5. Comprobación de versión

Si alguien de la sala lleva una versión distinta del mod —o no lo lleva— se avisa a todos. Un desajuste de versión rompe el mod de formas muy difíciles de diagnosticar desde dentro del juego.

## Requisitos

- **[BepInEx 5](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/)**
- **Todos los jugadores necesitan la misma DLL**, incluido el anfitrión.

Lo segundo no es opcional. El mod cambia decisiones que toma el *master client*, así que una sala con versiones mezcladas se comporta de forma rara.

## Instalar

**Con el gestor de mods** (recomendado, es lo que hay que pasarle a los amigos):

> Thunderstore Mod Manager o r2modman → **Settings** → **Import local mod** → elegir `Eddy-RepoAmigos-<versión>.zip` → elegir perfil → **Import**

**A mano**, copiando `RepoAmigos.dll` en:

```
%APPDATA%\Thunderstore Mod Manager\DataFolder\REPO\profiles\<PERFIL>\BepInEx\plugins\Eddy-RepoAmigos\
```

Arrancar **siempre** con **Start modded**. Arrancar desde Steam corre el juego en vanilla, en silencio y sin ningún aviso.

## Configurar

Tras la primera partida se genera:

```
...\<PERFIL>\BepInEx\config\com.usuario.repoamigos.cfg
```

Se puede editar sin recompilar. Cada jugador puede tener su propio archivo, pero **conviene que coincidan** para evitar confusión.

### Roles

| Sección | Opción | Por defecto | Qué hace |
|---|---|---|---|
| `Roles` | `Activado` | `true` | Sortea los roles al empezar cada nivel |
| `Roles` | `MinimoJugadores` | `3` | Por debajo de esto no se reparte ningún rol |
| `Roles` | `RepartirCadaNivel` | `true` | `false` los sortea una vez para toda la partida |
| `Roles` | `AnunciarRol` | `true` | Enseña tu rol al empezar el nivel |
| `Roles` | `RecordarRol` | `true` | Permite volver a consultarlo con una tecla |
| `Roles` | `TeclaRecordar` | `Tab` | La tecla que te recuerda el rol |
| `Medico` | `Activado` / `Tecla` / `PuntosPorCura` / `Cargas` / `MinutosRecarga` / `Alcance` | `true` / `G` / `20` / `3` / `3` / `4` | |
| `Ingeniero` | `Activado` / `Tecla` / `Cargas` / `MinutosRecarga` | `true` / `J` / `5` / `5` | |
| `Saboteador` | `Activado` / `Tecla` / `MinutosApagon` / `MinutosRecarga` / `SubirEnemigos` / `FuerzaEnemigos` | `true` / `H` / `2` / `15` / `true` / `10` | |
| `Rastreador` | `Activado` / `MinutosEntreAvisos` / `AlcanceMaximo` / `DecirNombre` | `true` / `2` / `60` / `true` | |

### Todo lo demás

| Sección | Opción | Por defecto | Qué hace |
|---|---|---|---|
| `ExtraccionManual` | `Activado` | `true` | `false` devuelve la extracción automática original |
| `ExtraccionManual` | `AvisoEnPantalla` / `TextoAviso` | `true` / `PULSA EL BOTON` | El texto de la pantalla del tubo |
| `ExtraccionManual` | `UsarPato` / `PatoNombreDelItem` | `true` / `Rubber Duck` | Usar un pato de goma como botón |
| `ExtraccionManual` | `PatoEscala` / `PatoGiroX` / `PatoGiroY` / `PatoGiroZ` | `2.0` / `0` / `0` / `0` | Tamaño y orientación del pato |
| `ExtraccionManual` | `BotonGrandeDeTienda` / `SoloElBoton` / `PiezasDelBoton` | `true` / `true` / — | Reutilizar el pedestal de la tienda como botón |
| `ExtraccionManual` | `BotonLateral` / `BotonAltura` / `BotonGiro` / `BotonHaciaFuera` / `BotonEscala` | `2.5` / `1.1` / `0` / `1.0` / `1.0` | Dónde se coloca ese pedestal |
| `RevivirEnExtractor` | `Activado` / `SegundosDeEspera` | `true` / `2.0` | Segundos dentro del extractor antes de revivir |
| `Carrito` | `Activado` / `EscalaDentro` / `SoloValiosos` | `true` / `0.75` / `true` | Encoger el botín dentro del carrito |
| `Version` | `AvisarDesajuste` | `true` | Avisar cuando alguien lleva otra versión |
| `Pruebas` | `Activado` / `Tecla` / `ValorMinimo` / `ValorMaximo` / `CantidadPorPulsacion` | `true` / `F3` / `2000` / `9000` / `1` | Generar botín de prueba (desarrollo) |
| `General` | `LogDetallado` | `false` | Mensajes extra en la consola de BepInEx |

## Desarrollo

```powershell
.\compilar.ps1                      # compila e instala en el perfil Default
.\compilar.ps1 -Perfil "OtroPerfil" # ...o en otro
.\verificar.ps1                     # valida los parches contra el código del juego
.\empaquetar.ps1                    # genera dist\Eddy-RepoAmigos-<version>.zip
```

**Ejecutar `verificar.ps1` siempre después de tocar un parche.** Harmony empareja los parámetros **por nombre y en tiempo de ejecución**: si un nombre no coincide con el del juego, compila con cero advertencias y solo falla al cargar la partida. El script caza justo eso, sin abrir el juego, leyendo `Assembly-CSharp.dll` con Mono.Cecil.

También valida los `AccessTools.Field(typeof(X), "campo")`, que es el mismo fallo mudo en otra forma: si el campo no existe, Harmony devuelve `null` y el código simplemente no hace nada, sin excepción. Ojo: solo puede comprobar nombres **literales** — pasar el nombre por una variable compila igual y pierde la red de seguridad en silencio.

Subir de versión son **tres** archivos: `Plugin.cs`, `RepoAmigos.csproj` y `manifest.json`. `empaquetar.ps1` para si los dos primeros no cuadran, y se niega a empaquetar si `verificar.ps1` no pasa — lo que se empaqueta va a manos de otra gente, y una DLL con un parche mal emparejado carga sin error y no hace nada.

Cerrar R.E.P.O. antes de compilar: con el juego abierto la DLL está bloqueada.

## Cómo funciona por dentro

Todo son parches Harmony sobre `Assembly-CSharp.dll`. No se modifica ningún archivo del juego.

### Extracción bajo demanda

El final de `ExtractionPoint.HaulChecker` hace:

```csharp
if (haulGoal - haulCurrent <= 0 && haulGoalFetched) {
    successDelay -= Time.deltaTime;
    if (successDelay <= 0f) { StateSet(State.Success); return; }
}
```

Se bloquea ese `StateSet(Success)` con un prefix, salvo cuando el mod lo ha «armado» por pulsación. Además hay que reactivar `buttonGrabObject` cada frame, porque `ButtonToggle` hace `if (currentState != State.Idle) buttonGrabObject.enabled = false;` — o sea, vanilla deja el botón inservible en cuanto el extractor se activa.

### Red y autoridad

`ExtractionPoint.StateSet` y `PlayerAvatar.Revive` emiten RPC a todos, así que solo el *master client* puede invocarlos o se duplican. Cuando pulsa alguien que no es master, se manda un evento Photon propio al master.

Códigos de evento propios (PUN se reserva del 200 en adelante):

| Código | Dirección | Para qué |
|---|---|---|
| `174` | cliente → master | pedir la extracción |
| `175` | master → todos | reparto de roles |
| `176` | cliente → master | subir la generación de enemigos en el sabotaje |
| `177` | cualquiera → todos | apagón |
| `178` | cualquiera → todos | comprobación de versión |
| `179` | cliente → master | recarga de batería del ingeniero |

Curar es la excepción que no necesita evento: `PlayerHealth.HealOther` manda `RpcTarget.All`, pero `HealOtherRPC` vuelve a filtrar por `photonView.IsMine`, así que solo la máquina del curado aplica la vida.

### Dónde vive el tic

En `RunManager.Update`, **no** en `Plugin.Update`. Unity destruye el componente del plugin al cambiar de escena, así que un temporizador colgado de `Plugin.Update` dejaría de correr en cuanto empieza la primera partida — sin un solo error en el log. Los parches Harmony sí sobreviven.

Los roles se guardan por SteamID (`SemiFunc.PlayerGetSteamID`), no por referencia al `PlayerAvatar`: entre niveles no hay garantía de que el objeto sea el mismo.

### El apagón, dos trampas

1. `LightManager.turnOffLights` **no es un interruptor**. Es el pestillo del apagón de final de nivel de vanilla (`allExtractionPointsCompleted`). Ponerlo a `true` no apaga nada; solo cancelaría ese apagón final.
2. `PropLight.SetIntensity(x)` hace `originalIntensity = x`. Apagar con `SetIntensity(0)` **destruye el dato con el que se restauraría**.

Por eso se toca `lightComponent.enabled`, que no usa nadie más, y solo se apuntan las luces que estaban encendidas — si no, al restaurar se encenderían las que el culling tenía apagadas a propósito.

### Las baterías del ingeniero, dos trampas más

Las dos salieron de leer el IL del juego, no de suponer:

1. `ItemBattery.SetBatteryLife` **no resucita una batería agotada**. Empieza con `if (batteryLife > 0) { ...aplica... } else { batteryLife = 0; }` — justo el caso que el rol existe para arreglar. El campo es público, así que se le da un empujón positivo antes.
2. Solo el anfitrión puede propagar el cambio. `BatteryFullPercentChange` termina en `else return` si no eres el master, en silencio. Por eso un ingeniero cliente manda el evento `179` y recarga el anfitrión.

### El carrito

Un único Postfix sobre `PhysGrabCart.ObjectsInCart` — la forma en que el propio juego decide qué hay dentro.

La versión anterior se enganchaba a `PhysGrabObjectImpactDetector.OnTriggerStay` y no podía funcionar en los clientes: en una máquina que no es el master **todo `PhysGrabObject` se queda cinemático de por vida**. `Awake` pone `rb.isKinematic = true` y lo único que lo deshace es `EnableRigidbody()`, que solo corre bajo `!Multiplayer || IsMasterClient`. Unity no entrega `OnTriggerStay` a cuerpos dormidos.

`ObjectsInCart` es el enganche bueno porque se llama sin puerta de master, usa `Physics.OverlapBox` —una consulta de escena que ignora el sueño de los rigidbodies— y se autolimita a un barrido cada 0,5 s. Dos reconciliaciones por segundo en vez de miles de callbacks de física.

La escala se trata como algo peligroso a propósito: `ItemEquippable` la usa como **variable de estado** (`StateEquipped` comprueba `magnitude < 0.1f`), así que escribirle la escala a un arma en el momento equivocado puede romperla para siempre. De ahí la lista blanca y la restauración forzada al cambiar de nivel.

## Licencia

MIT — ver [LICENSE](LICENSE).
