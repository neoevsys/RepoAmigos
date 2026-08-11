# Changelog

## 1.5.0

**La extracción vuelve a ser de todos.** El ingeniero era el único que podía activarla, y eso convertía a una persona en el cuello de botella del grupo entero: si moría, se desconectaba, se quedaba lejos o no se había enterado de que le había tocado, el nivel se quedaba sin salida. Quedó en un log: 88 rechazos seguidos, unos 44 segundos machacando el pato, y la partida murió ahí. El candado se ha quitado de las dos rutas donde vivía.

**El ingeniero ahora repara.** Recarga a tope la batería del objeto que lleve en las manos —armas, drones, rastreadores, cualquier cosa con `ItemBattery`—, con 5 reparaciones que se reponen una cada 5 minutos. Tecla `J` por defecto, configurable en `[Ingeniero]`.

Dos trampas del juego que hubo que sortear, las dos leídas del IL:

- `SetBatteryLife` **no resucita una batería agotada**: empieza con `if (batteryLife > 0)` y en el `else` la deja a cero. Justo el caso que el rol existe para arreglar. Se le da un empujón positivo al campo antes de llamarlo.
- Solo el anfitrión puede propagar el cambio. `BatteryFullPercentChange` termina en `else return` si no eres el master, sin lanzar un solo error. Por eso un ingeniero cliente manda el evento Photon 179 y recarga el anfitrión.

**El carrito, reescrito entero.** Lo anterior no funcionaba en los clientes y además hacía daño:

- No podía funcionar. Se apoyaba en `OnTriggerStay`, y en una máquina que no es el master **todo `PhysGrabObject` se queda cinemático de por vida** (`Awake` pone `isKinematic = true` y solo el master arranca `EnableRigidbody()`). Unity no entrega `OnTriggerStay` a cuerpos dormidos.
- No filtraba qué encogía. En los logs salen encogidas puertas del nivel que nunca se restauraron, bisagras, cabezas de muerto, el ragdoll de un jugador, una mina armada y el propio pato del mod. En una sesión: 68 encogidos, 29 restaurados, **39 objetos pequeños para siempre**.
- Bomba sin estallar: `ItemEquippable` usa la escala como variable de estado (`StateEquipped` comprueba `magnitude < 0.1f`), así que tocarle la escala a un arma al equiparla podía dejarla rota de forma permanente.

Ahora hay un único enganche, un Postfix sobre `PhysGrabCart.ObjectsInCart`, que es como el propio juego decide qué hay dentro: se llama sin puerta de master, usa `Physics.OverlapBox` —consulta de escena, que ignora el sueño de los rigidbodies— y se autolimita a 2 barridos por segundo. Con lista blanca (por defecto solo el botín), histéresis de 3 barridos para no parpadear, y restauración forzada al cambiar de nivel.

De miles de callbacks de física por segundo a dos reconciliaciones. Y funciona igual en anfitrión y en clientes.

## 1.4.5

**El pato ya dice por qué no hace nada.**

Reportado jugando de dos: «no hay ingeniero, así que nadie puede extraer». El log dice que no fue eso — el rol nunca bloqueó nada:

| Mensaje | Veces |
|---|---|
| `Roles: solo 2 jugadores, minimo 3. Sin roles.` | — |
| `Ingeniero: pulsacion rechazada` | **0** |
| `Pato pulsado sin cubrir la meta todavia: ignorado.` | **114** |

Lo de «sin ingeniero el pato es de todos» ya se arregló en la 1.4.0 y funciona: `DebeBloquear()` sale en `if (!Roles.RolEnJuego(Rol.Ingeniero)) return false;`.

El fallo real era que ese rechazo **solo se escribía en el log**. Desde dentro del juego, pulsar el pato 114 veces sin respuesta es indistinguible de que el mod esté roto, y no había forma de averiguar qué faltaba.

Ahora sale en pantalla. Y con el motivo correcto, porque `Esperando` mezclaba tres condiciones distintas en un solo bool:

- `Este extractor todavia no esta activo` — se arregla entrando en él
- `Espera, el extractor aun no ha pedido su botin`
- `Faltan 1.234 de botin` — se arregla trayendo más cosas

El mensaje viejo decía «sin cubrir la meta» incluso cuando el problema era que el extractor no estaba activo, o sea que mentía.

Lleva freno de 1,5 s: un agarre mantenido vuelve a disparar `GrabStarted` y si no parpadearía. El freno cubre también la traza — las 114 pulsaciones escribieron 114 líneas para no decir nada.

Mismo aviso en el botón grande de la tienda, que se cortaba igual de callado.

## 1.4.4

**El pato escribía 22 MB de log por partida, y esos eran los tirones.**

El bloque que mantiene el pato quieto hacía esto en cada frame:

```csharp
if (!cuerpo.isKinematic) cuerpo.isKinematic = true;
cuerpo.velocity        = Vector3.zero;   // el cuerpo YA es cinematico
cuerpo.angularVelocity = Vector3.zero;
```

Un cuerpo cinemático ignora la velocidad — Unity lo mueve solo por `transform` — y escribírsela emite `Setting linear/angular velocity of a kinematic body is not supported`, **con captura de la pila de llamadas**. Dos por frame y por pato.

Lo irónico es que este mismo archivo ya documentaba el fallo veinte líneas más arriba, achacándoselo a `ItemEquippable.Update` del juego. El mod lo estaba repitiendo.

Ahora la velocidad se pone a cero **mientras el cuerpo sigue siendo dinámico**, y solo después se vuelve cinemático. Si ya lo es, no se toca.

Medido en una sesión real de cinco patos: 178.010 avisos lineales y otros tantos angulares — 356.020 líneas y 22,6 MB de `Player.log`. Lo que lo señaló sin adivinar fue dividir avisos entre frames y entre patos: salió 0,96, o sea **un** escritor por pato; si `ItemEquippable.Update` también disparara serían dos. Que los avisos lineales y angulares coincidan al dígito confirma que salen del mismo sitio.

**TAB recuerda qué rol tienes y cómo se usa.**

El aviso del principio del nivel se pierde en segundos y después no había forma de consultarlo. Ahora se puede pedir cuando quieras.

Reutiliza el mismo aviso del reparto en vez de duplicar los textos, así no se pueden quedar desfasados, y de paso trae el estado del momento: el del médico dice las cargas que le quedan **ahora**.

Novedad propia es la rama de «sin rol». Antes, quien no tenía rol pulsaba y no pasaba nada — indistinguible de que el mod esté roto. Ahora dice el motivo: roles apagados, reparto aún sin hacer, faltan jugadores, o simplemente no te tocó. La comprobación va **antes** del interruptor de los roles, para que también conteste con los roles desactivados.

Configurable en `[Roles]`: `RecordarRol` y `TeclaRecordar` (Tab por defecto). Tab es también la tecla del mapa del juego; las dos cosas salen a la vez y no se pisan.

## 1.4.1

**Los objetos del C.A.R.T. ya se encogen para todos, no solo para el anfitrión.**

La causa no era la escala ni la red, sino la primera línea de `PhysGrabObjectImpactDetector.OnTriggerStay`:

```csharp
if (GameManager.instance.gameMode != 0)
    if (!PhotonNetwork.IsMasterClient) return;
```

En multijugador el juego solo lleva la cuenta del contenido del carrito en la máquina del anfitrión. `PhysGrabInCart.Add` nunca se llama en los clientes, así que el parche que encogía los objetos no llegaba a ejecutarse allí.

No hace falta red para arreglarlo: `OnTriggerStay` lo invoca Unity y la física es local, así que el método sí se ejecuta en todas las máquinas — es el código del juego el que se sale antes de hacer nada. Un `Prefix` corre antes de esa salida y repite la comprobación del carrito por su cuenta.

Al ser un efecto puramente visual, que una máquina discrepe un frame de otra da igual: no hay estado que sincronizar.

## 1.4.0

**Siempre hay un ingeniero.** Que el rol pudiera desaparecer era un fallo de diseño, no solo un bug: sin ingeniero no se activa la extracción y el nivel se queda sin salida, en silencio y sin ninguna pista.

Tres cambios que lo garantizan:

- **El ingeniero se reparte el primero.** Cuando hay menos jugadores que roles, los últimos de la lista se quedan fuera. El ingeniero es el único rol del que depende que el grupo pueda avanzar; los otros tres solo dan ventajas. Ahora encabeza la lista, así que existe siempre que se reparta algo.
- **Si su dueño se desconecta, el rol se reasigna.** El anfitrión revisa cada 5 segundos que todos los roles tengan dueño presente y le pasa los huérfanos a quien no tenga ninguno. Se reasignan solo los huérfanos, no se vuelve a sortear todo: cambiarle el rol a gente que no tiene nada que ver, a mitad de partida, sería peor.
- **Si no hay a quién dárselo, el rol se borra** en vez de quedar a nombre de un fantasma. Entonces `RolEnJuego()` devuelve false y el botón vuelve a ser de todos. Nunca se prefiere un bloqueo a un rol de menos.

El paquete de red lleva ahora una marca de cabecera (`N` sorteo nuevo / `R` reparación). El cliente no puede deducirlo, y hace falta: un sorteo de nivel nuevo tiene que reiniciarle las cargas al médico aunque le vuelva a tocar el mismo rol, mientras que una reparación no debe tocárselas a nadie que no haya cambiado de rol.

## 1.3.3

**El rol de ingeniero no servía de nada.** Encontrado jugando: había ingeniero, salía su mensaje al rechazar el botón lateral, y aun así cualquiera lanzaba la extracción agarrando el pato.

El filtro estaba puesto en `ExtractionPoint.OnClick`, pero hay **tres** formas de lanzar una extracción y esa es la que menos se usa:

| Ruta | Qué es |
|---|---|
| `PhysGrabObject.GrabStarted` | el pato — el que se usa de verdad |
| `ExtractionPoint.OnShopClick` | el botón grande |
| `ExtractionPoint.OnClick` | el botón lateral — el único que estaba filtrado |

El pato no es un botón sino un objeto agarrable, y el mod intercepta el *agarre* para tratarlo como pulsación: nunca pasa por `OnClick`. Como la mitad visible del rol sí funcionaba, nada delataba que la otra mitad estaba abierta.

Arreglado moviendo el candado al cuello de botella: las tres rutas acaban en `Solicitar()`, así que la comprobación vive ahí dentro y no en cada parche. Una cuarta forma de pulsar quedaría cubierta sola.

## 1.3.2

Dos fallos salidos de la primera partida real de 3 jugadores.

- **El pato ya no genera 5000 avisos por partida.** Se le ponía `isKinematic = true` para dejarlo quieto, pero el `PhysGrabObject` sigue vivo a propósito (es lo que permite agarrarlo) y escribe `velocity` y `angularVelocity` cada frame. Sobre un cuerpo cinemático Unity no lo permite y suelta **dos `LogWarning` por frame** mientras el pato exista. Visualmente no rompía nada —por eso pasó desapercibido desde el principio— pero cada aviso captura la pila de llamadas: tirones y un `Player.log` de 336 KB en minutos, sepultando cualquier línea útil. Ahora se usa `RigidbodyConstraints.FreezeAll`, que lo deja igual de quieto sin que el cuerpo sea cinemático.
- **Si el ingeniero se desconectaba, el nivel quedaba bloqueado sin salida.** `RolEnJuego()` miraba si el rol estaba en la tabla, pero no si su dueño seguía en la sala. Con el ingeniero desconectado el pato dejaba de responderle a nadie, sin ninguna pista de por qué. Ahora se comprueba que el dueño del rol siga presente, y si no, el botón vuelve a ser de todos.

## 1.3.0

**Aviso de versión desincronizada.** Cada máquina anuncia su versión del mod y sale un mensaje en pantalla si alguien de la sala lleva otra distinta, o directamente no lo lleva.

Existe porque los dos fallos que más tiempo han costado en este mod son el mismo fallo con dos caras, y ninguno produce un error:

1. Arrancar desde Steam corre el juego **en vanilla sin avisar**.
2. Entre la 1.2.0 y la 1.2.1 cambió el formato del reparto de roles, y las dos versiones se descartan mutuamente en silencio.

En los dos casos lo único que se ve es que «el mod no hace nada», que es la peor pista posible. Ahora se ve un `MOD DESINCRONIZADO` con el nombre de quién y qué versión lleva.

- Funciona **aunque los roles estén desactivados**: el desajuste afecta al mod entero.
- Da 25 segundos de margen antes de acusar a nadie de no tener el mod, para no señalar en falso a quien acaba de entrar.
- Los clientes con 1.2.1 o anterior no anuncian nada y aparecen como «sin el mod». Es correcto: son incompatibles igualmente.
- Se puede apagar con `[Version] AvisarDesajuste = false`.

## 1.2.1

Correcciones sobre el reparto de roles y la capa de red, salidas de la primera prueba con gente.

- **El reparto ya no se hace en el lobby.** Saltaba 3 s después de *cualquier* cambio de nivel, incluido el del menú del lobby, con la gente todavía entrando: el resultado era un reparto vacío y un `me ha tocado Ninguno` antes siquiera de cargar el nivel. Ahora exige `SemiFunc.RunIsLevel()`, estar dentro de la sala de Photon y que el jugador local exista.
- **Corregido el candado de autoridad.** `SemiFunc.IsMasterClientOrSingleplayer()` empieza con `if (!GameManager.Multiplayer()) return true;`, así que durante las transiciones de nivel devuelve `true` en todas las máquinas y cada cliente se creía el anfitrión. Ahora se pregunta al final, cuando el estado ya está asentado.
- **El manejador de eventos de Photon va dentro de un `try/catch`.** Lo invoca PUN desde su bucle de red: una excepción que se escape sube por la pila de la librería y puede tumbarle la conexión al jugador, que desde fuera se ve como «se ha caído el servidor». Un rol que falla tiene que ser un rol que no funciona, nunca un jugador expulsado.
- **Imposible suscribirse dos veces** al canal de eventos: ahora se desengancha antes de enganchar. `Plugin.OnDestroy` salta en cada cambio de escena y podía dejar el manejador puesto marcándolo como quitado, acumulando duplicados.
- **El reparto viaja como un solo `string`** en vez de un `object[]` con arrays anidados dentro, que es la parte con más esquinas raras del serializador de Photon.
- Las habilidades ya no se activan en la tienda ni en el lobby.

## 1.2.0

**Roles secretos.** Al empezar cada nivel se sortean cuatro roles entre los jugadores. Solo ves el tuyo. Hacen falta 3 jugadores como mínimo (configurable).

- **Médico** — `G` cura al compañero herido más cercano: +20 PV, 3 cargas, recupera una carga cada 3 minutos. No le cuesta vida propia.
- **Ingeniero** — es el único que puede activar la extracción pulsando el pato.
- **Saboteador** — `H` apaga todas las luces del nivel durante 2 minutos y acelera la aparición de enemigos mientras dura. Recarga de 15 minutos.
- **Rastreador** — cada 2 minutos le avisa de qué monstruo tiene más cerca, a qué distancia y en qué dirección.

Todo configurable: teclas, cantidades, tiempos y alcances.

Detalles:

- Si no hay ingeniero en la partida (pocos jugadores, rol desactivado), el pato vuelve a funcionar para todos. Nunca se deja al grupo sin poder extraer por un rol que no existe.
- Las linternas no se apagan durante el sabotaje: son `FlashlightController`, no `PropLight`.
- El médico no gasta carga si no hay a quién curar ni sobre alguien que ya está al máximo.
- El host repite el reparto cada 20 s, para los que entran con el nivel ya empezado.

`verificar.ps1` ahora valida también los `AccessTools.Field`, que fallan en silencio devolviendo `null` si el nombre del campo no existe.

## 1.1.0

- Los objetos se encogen dentro del C.A.R.T. y recuperan su tamaño al sacarlos.
- Generador de objetos de valor con una tecla, para probar el mod.

## 1.0.0

- **Extracción bajo demanda** — la extracción ya no se dispara sola al cubrir la meta; hay que pulsar el botón. Da tiempo a seguir metiendo objetos para el excedente.
- **Revivir en el extractor** — basta con dejar la cabeza de un compañero muerto dentro de un extractor y esperar unos segundos, sin completar nada.
