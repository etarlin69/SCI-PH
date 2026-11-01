# Lector de huellas SCI-PH (ESP32 + R307)

Firmware del lector físico de huellas. Es un cliente más del **Device API existente**
([contratos](../../docs/api/device-api-contracts.md), [ADR-009](../../docs/adr/ADR-009-fingerprint-identification.md),
[ADR-010](../../docs/adr/ADR-010-fingerprint-enrollment.md), [ADR-011](../../docs/adr/ADR-011-fingerprint-revocation.md)):
no cambia el backend ni el contrato. El simulador `tools/SCI-PH.Device.Simulator` se mantiene como cliente de CI, de
desarrollo y de pruebas del backend.

```
ESP32  ⇄ UART (HardwareSerial 2) ⇄  R307          (plantillas y comparación en el sensor)
ESP32  ⇄ HTTPS (Wi-Fi)           ⇄  SCI-PH.Device.API
```

La plantilla y la imagen **nunca salen del sensor**: el lector solo informa slots y resultados.

> **Estado de validación:** este firmware **no se ha probado en hardware**. La lista de pruebas físicas está en
> [docs/testing/fingerprint-reader-physical-checklist.md](../../docs/testing/fingerprint-reader-physical-checklist.md);
> ningún punto se marca como aprobado sin el ESP32 y el R307 conectados.

## Hardware

| Componente | Notas |
|---|---|
| ESP32 DevKit (ESP32-WROOM-32, `board = esp32dev`) | Cualquier placa ESP32 clásica con UART2 libre |
| Sensor de huella R307 (o compatible R30x) | Capacidad, numeración y comandos varían entre clones: el firmware los lee del sensor |
| Fuente de 5 V estable | El R307 consume picos de corriente durante la captura |
| LED y buzzer activo (opcionales) | Pines configurables; `-1` = no conectado |
| Cable USB de datos | Alimentación del ESP32, flasheo y consola serie |

### Conexiones

| R307 (cable) | ESP32 | Notas |
|---|---|---|
| VCC (5 V) | 5V / VIN | Alimentación del sensor: **5 V** según su hoja de datos |
| GND | GND | **GND común** obligatorio |
| TX (salida del sensor) | GPIO16 (RX2) | `SCIPH_R307_RX_PIN` |
| RX (entrada del sensor) | GPIO17 (TX2) | `SCIPH_R307_TX_PIN` |
| Touch / WAKEUP, 3.3 V táctil | sin conectar | No se usan |

- UART: **57600 baudios, 8N1** (`SCIPH_R307_BAUD`), `HardwareSerial(2)`.
- **Medir antes de conectar:** el ESP32 tolera **3,3 V** en sus GPIO. Con el sensor alimentado, mida con un multímetro
  el nivel alto del TX del R307 respecto a GND. Si supera 3,3 V, use un divisor resistivo o un conversor de nivel
  entre el TX del sensor y GPIO16. No todos los clones se comportan igual (hay variantes R307S a 3,3 V y colores de
  cable distintos): verifique el pinout del módulo concreto.
- Los pines, el UART, la velocidad, el LED y el buzzer se cambian en `platformio.ini` (`build_flags = -D ...`); los
  valores por defecto están en [`include/firmware_config.h`](include/firmware_config.h).

## Toolchain (versiones fijas)

| Componente | Versión | Licencia |
|---|---|---|
| PlatformIO platform `platformio/espressif32` | 8.0.0 | Apache-2.0 |
| Arduino-ESP32 (`framework-arduinoespressif32` 4.30312.261024 + `-esp32-libs` 3.3.12) | 3.3.12 | LGPL-2.1 |
| Adafruit Fingerprint Sensor Library | 2.1.4 | BSD |
| ArduinoJson | 7.4.3 | MIT |
| Unity (tests `native`, incluido por PlatformIO) | el de PlatformIO | MIT |

Las licencias son las que declara cada proyecto en su repositorio; revise cada una antes de distribuir binarios. Los
módulos del toolchain (compiladores, esptool) tienen sus propias licencias.

Instalación (no incluida en este repositorio; ocupa varios GB entre PlatformIO, el toolchain Xtensa y el framework):

```bash
python -m pip install --user platformio
cd firmware/sciph-fingerprint-reader
pio run -e esp32dev            # compila el firmware de producción (descarga platform y librerías la primera vez)
pio test -e native             # pruebas en el PC (requiere un compilador C++ del sistema: gcc/clang)
```

Entornos:

| Entorno | Uso |
|---|---|
| `esp32dev` | Producción: solo HTTPS con CA. `SCIPH_PRODUCTION=1`; HTTP de desarrollo y diagnóstico de slots dan error de compilación |
| `esp32dev-devhttp` | Desarrollo: además acepta `http://` a un Device API con `RequireHttps=false` en una red aislada |
| `native` | Pruebas Unity en el PC con sensor, HTTP, NVS, reloj y red simulados |

## Registrar el dispositivo en SCI-PH

1. En el Web (administración, permiso `Devices.Manage`) registre el dispositivo: `POST /api/web/admin/devices` con un
   `deviceCode` (por ejemplo `ESP32-LAB-01`), nombre y scopes **`Device.Heartbeat`, `Device.Fingerprint.Identify` y
   `Device.Fingerprint.Enroll`** (el registro de huellas exige los dos últimos; ver ADR-010).
2. La respuesta muestra la **clave una sola vez**. Cópiela directamente a la consola serie del lector (paso siguiente);
   no la guarde en archivos, capturas ni en el repositorio. Si se pierde, rote la clave (`rotate-secret`).
3. El lector se identifica con `X-Device-Id` = **deviceCode** (no el GUID) y `X-Device-Key` = la clave, siempre en
   cabeceras y nunca en la URL.

## Flasheo

```bash
pio run -e esp32dev -t upload          # o -e esp32dev-devhttp en desarrollo
pio device monitor -b 115200
```

## Aprovisionamiento por consola serie

Con el monitor serie a 115200 baudios, escriba `provision` y responda:

1. SSID de Wi-Fi.
2. Clave de Wi-Fi.
3. URL base del Device API (`https://host:puerto`). En la compilación de desarrollo se acepta `http://`.
4. Código del dispositivo (`deviceCode`).
5. Clave del dispositivo.
6. Con HTTPS: el certificado **CA** en PEM (pegar hasta la línea `-----END CERTIFICATE-----`).
7. `save` guarda en NVS y reinicia. `cancel` sale sin guardar en cualquier paso.

El firmware no repite los valores escritos (algunos monitores serie muestran localmente lo que se teclea).

### Comandos

| Comando | Qué hace |
|---|---|
| `provision` | Configuración guiada (Wi-Fi, URL, código, clave, CA) |
| `status` | Estado, Wi-Fi, URL, transporte, código, "clave configurada", capacidad, cantidad de slots ocupados, registro en curso. Nunca la clave, la clave Wi-Fi ni números de slot |
| `forget` | Pide escribir `FORGET`; borra credencial, Wi-Fi, URL y CA. **No borra plantillas del sensor** ni el registro en curso (protegen slots) |
| `reconcile` | Ejecuta ahora la reconciliación de slots huérfanos |
| `reconcile force` | Igual, permitiendo más borrados que el límite automático (ver abajo) |
| `journal discard` | Descarta un registro de enrolamiento **ilegible** (solo ese caso) |
| `restart` | Reinicia el lector |
| `help` | Lista los comandos |

## HTTPS y modo de desarrollo

- **Producción:** `WiFiClientSecure` validando el certificado del Device API contra el CA aprovisionado
  (`setCACert`). Nunca `setInsecure()`. El Device API sirve HTTPS directamente con Kestrel (decisión D2); el CA es el
  que firmó ese certificado (por ejemplo, el CA interno del hospital o uno de laboratorio).
- La validación TLS necesita la **hora** (SNTP, `pool.ntp.org` / `time.google.com`, configurables). Con HTTPS el lector
  espera en `TIME_SYNC` hasta tener una hora plausible; no hace peticiones antes.
- **Desarrollo:** solo con el entorno `esp32dev-devhttp` **y** una URL `http://` **y** un Device API ejecutándose
  directamente en el host/LAN con `RequireHttps=false` (Development; decisión D3), en una red aislada. Una
  configuración HTTP guardada por una compilación de desarrollo es rechazada por la de producción (el lector vuelve a
  pedir `provision`). La consola avisa en cada arranque cuando el transporte de desarrollo está activo.

## Secretos

- La clave del dispositivo, la clave Wi-Fi y el CA viven solo en **NVS** del ESP32 (prototipo académico: NVS normal).
- Nunca en el código, el repositorio, la URL ni los logs. `status` solo indica si están configuradas.
- **Producción (recomendado, no activado aquí):** cifrado de flash del ESP32 + NVS cifrado. Este PR no activa el cifrado
  de flash porque es irreversible en el chip.

## Comportamiento

### Arranque

```
BOOT → (sin configuración) PROVISIONING
BOOT → WIFI_CONNECT → TIME_SYNC → SENSOR_INIT → READ_PARAMETERS → SLOT_RECONCILIATION → JOURNAL_RECOVERY → IDLE
Wi-Fi caído → NETWORK_RECOVERY (1 s, 2 s, 4 s, … máx. 30 s)   401 → AUTH_FAILED   sensor sin respuesta → SENSOR_ERROR
```

- `READ_PARAMETERS` lee la **capacidad del sensor** (`ReadSysPara`); nunca se asume 1000. Slots fuera de esa
  capacidad se rechazan.
- `SLOT_RECONCILIATION` lee la ocupación física: `ReadIndexTable` (0x1F) si el módulo lo implementa y coincide con el
  conteo de plantillas y con una muestra de `LoadChar`; si no, `LoadChar` slot por slot (lento en 1000 slots). Durante
  el escaneo no hay sondeo normal. El mapa vive en RAM y nunca reemplaza la comprobación previa a cada `Store`.

### Una operación física a la vez, por prioridad

| Prioridad | Operación | Intervalo |
|---|---|---|
| 1 | Identificación (emergencias) | 3 s |
| 2 | Registro (enrolamiento) | 3 s |
| 3 | Borrado de slots revocados | 30 s |
| 4 | Reconciliación | arranque, tras resolver un registro interrumpido, `reconcile`, cada 6 h |
| 5 | Heartbeat (`firmwareVersion`, `uptimeSeconds`) | 60 s |

Unas 43 peticiones por minuto, por debajo del límite de 120/60 s. Un `429` detiene **todas** las peticiones durante
`Retry-After` (10 s si falta, máximo 5 min). Un `403` pausa 5 minutos la familia de su scope (`missing_scope`), sin
reintentos en bucle. Un `401` lleva a `AUTH_FAILED`: se detiene el sondeo, LED fijo, y cada ~5 minutos un heartbeat
prueba la credencial (no se borra; se puede reaprovisionar por serie). Timeout HTTP: 10 s.

### Registro

1. Elige un slot **físicamente vacío** que **no** esté en el último inventario del servidor (activo o pendiente de
   borrar).
2. Escribe el registro (`journal`, NVS, un solo valor atómico) con `enrollmentId`, slot, estado `Storing` y
   `expiresAtUtc`, **antes** de cualquier `Store`.
3. Captura 1 → `Img2Tz(1)` → retire el dedo → captura 2 → `Img2Tz(2)` → `RegModel` (si no coinciden: `Failed`).
4. Vuelve a comprobar con `LoadChar` que el slot sigue vacío (el `Store` del R307 sobrescribe). `LoadChar` carga en el
   **CharBuffer 2**, así el modelo del CharBuffer 1 queda intacto.
5. `Store` → registro `Stored` → `POST Enrolled + slot`.

| Respuesta | Acción |
|---|---|
| `204` | Borra el registro; el slot queda |
| sin respuesta, timeout, 5xx, 429 | Conserva slot y registro; reenvía **el mismo** resultado con espera creciente |
| `400`, `404`, `409` | Borra el slot (compensación) y después el registro |
| `403` | Si ningún envío anterior fue incierto, el servidor nunca lo procesó: compensa. Si hubo uno incierto, conserva (pudo crear la referencia) |
| `401` | Conserva todo y pasa a `AUTH_FAILED` |

El vencimiento local (`expiresAtUtc`) no borra nada: el lector reenvía y el servidor decide (`409` si venció sin
resultado, `204` si ya lo había aceptado). Las capturas de baja calidad se reintentan hasta 3 veces antes de informar
`LowQuality`; si nadie pone el dedo en 20 s no se envía resultado (el registro sigue `Pending` y se vuelve a pedir
hasta que venza en el servidor).

**Arranque con registro:** `Storing` + slot vacío → se borra el registro. `Storing` + slot ocupado → `Stored`.
`Stored` → reenvía el mismo resultado. `Compensating` → borra el slot y luego el registro. Sin red → conserva todo
(nunca borra solo porque reinició). Un registro **ilegible** bloquea el enrolamiento y todo borrado de huérfanos hasta
`journal discard`.

### Identificación

Captura → `Img2Tz(1)` → `Search` sobre toda la capacidad (`fingerSearch`; el `fingerFastSearch` de la librería solo
cubre 163 slots) → `Match + slot` / `NoMatch` / `LowQuality` / `SensorError`. Nunca envía puntaje. Ante una respuesta
incierta reenvía exactamente la misma sesión, resultado y slot; `404`/`409` terminan esa sesión.

### Borrado (ADR-011)

`deletions/pending` → `LoadChar`: vacío → `NotFound`; ocupado → `DeletChar` + verificación → `Deleted`; fallo →
`SensorError`. Cada intento físico tiene **un** `attemptId`, guardado en NVS hasta la respuesta: tras un reinicio se
reenvía el mismo. Tras un `SensorError` aceptado, el siguiente intento físico espera 30 s y usa otro `attemptId`. Si el
registro en curso usa ese slot, el borrado espera.

### Reconciliación de huérfanos (P0)

Huérfanos candidatos = ocupados físicamente − inventario del servidor (`GET /api/device/fingerprint/slots`: activos +
borrados pendientes) − slot del registro en curso. Un candidato se borra solo si:

1. el inventario se confirmó (`200` válido; cualquier otra cosa → el sensor no se toca),
2. no lo usa el registro en curso (un registro ilegible bloquea todo),
3. **un segundo inventario**, pedido justo antes de borrar, tampoco lo incluye,
4. `LoadChar` confirma que sigue ocupado,
5. no hay más de 10 candidatos (más → `reconcile force`; protege ante un sensor aprovisionado con otro `deviceCode`).

Sin red no se borra nada. Nunca se borra un slot activo, uno pendiente de borrado, el del registro en curso ni uno
desconocido.

### Logs

Los logs normales muestran operación, correlación corta (8 caracteres del id), estado, estado HTTP, categoría de
error y latencia. **Nunca** números de slot, plantillas, imágenes, la clave del dispositivo, la clave Wi-Fi, ids de
paciente, `SubjectReference` ni datos clínicos. `SCIPH_DEBUG_SLOTS=1` (solo fuera de producción; en `esp32dev` es
error de compilación) añade líneas `[debug-slots]` para diagnóstico.

### Indicaciones al usuario

Mensajes por serie (y LED/buzzer opcionales): coloque el dedo, retire el dedo, coloque el mismo dedo otra vez, huella
leída, no reconocida, baja calidad, error del sensor, sin espacio, error de red, autenticación rechazada (LED fijo).
Nunca datos del paciente.

## Estructura

```
platformio.ini
include/firmware_config.h             pines, baudios, NTP, banderas de compilación
src/main.cpp                          solo cableado (Arduino)
src/config/                           configuración (NVS), validación, ajustes de tiempos
src/provisioning/                     consola serie
src/network/                          Wi-Fi + SNTP (ESP32), backoff
src/api/                              cliente del Device API, transporte HTTPS (ESP32)
src/fingerprint/r307/                 interfaz del sensor y adaptador R307 (Adafruit)
src/fingerprint/capture/              captura con reintentos
src/fingerprint/enrollment/           registro + journal + compensación
src/fingerprint/identification/       identificación
src/fingerprint/cleanup/              borrado de slots revocados (attemptId)
src/fingerprint/reconciliation/       escaneo físico y reconciliación de huérfanos
src/storage/journal/, slot_map/       registro durable, mapa de slots en RAM, NVS (ESP32)
src/state/                            máquina de estados y planificador
src/feedback/                         mensajes, LED, buzzer
test/                                 pruebas Unity (native) con hardware simulado
```

Los archivos `*_esp32.*` y `main.cpp` son los únicos que dependen de Arduino; el resto es C++17 portable y se prueba
en el PC.

## Pruebas

```bash
pio test -e native
```

Cubren: estados y arranque, prioridad y sondeo, presupuesto de peticiones, clasificación HTTP, 401/403/429 con
`Retry-After`, backoff de Wi-Fi, journal y recuperación al arrancar, reenvío del mismo resultado, compensación,
`attemptId` (mismo tras incertidumbre o reinicio, nuevo tras `SensorError`), mapa de slots y capacidad, escaneo por
tabla de índices o `LoadChar`, reconciliación y todas sus reglas de seguridad, aprovisionamiento y ausencia de
secretos y slots en logs.

Después, con hardware: [lista de pruebas físicas](../../docs/testing/fingerprint-reader-physical-checklist.md).

## Limitaciones aceptadas

- Sin validación en hardware todavía; los códigos de respuesta del R307 (por ejemplo `0x0C` en un slot vacío) y la
  disponibilidad de `ReadIndexTable` deben confirmarse con el módulo real.
- Una huella registrada en un lector solo se reconoce en ese lector (sin sincronización de plantillas).
- El escaneo por `LoadChar` de un sensor de 1000 slots tarda (cada consulta ~decenas de ms) y bloquea el sondeo al
  arrancar o al recuperarse de un error del sensor.
- Las operaciones físicas son bloqueantes (hasta ~20 s esperando el dedo); durante ellas no se procesa otra cosa.
- NVS sin cifrar (prototipo); clave Wi-Fi y del dispositivo recuperables con acceso físico a la flash.
- Un solo certificado CA en PEM (≤ 3800 bytes).
- Sin OTA ni pantalla; las indicaciones son por serie, LED y buzzer.
- `forget` no borra plantillas: reasignar un sensor usado a otro `deviceCode` exige limpiarlo a mano; la
  reconciliación se niega a borrar más de 10 huérfanos sin `reconcile force`.
