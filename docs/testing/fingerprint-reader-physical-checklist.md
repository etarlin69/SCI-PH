# Lista de pruebas físicas: lector ESP32 + R307

Pruebas del firmware [`firmware/sciph-fingerprint-reader`](../../firmware/sciph-fingerprint-reader/README.md) con el
ESP32 y el R307 conectados. **Ningún punto se marca como aprobado sin hardware real.** Estado inicial: todos
pendientes (el firmware aún no se ha ejecutado en un dispositivo).

Registre en cada punto la fecha, quién lo probó, la versión de firmware (`status`), el entorno (`esp32dev` o
`esp32dev-devhttp`) y la observación. No anote claves, números de slot en documentos compartidos, datos de pacientes
reales ni imágenes de huellas: use pacientes sintéticos.

**Preparación:** Device API en el host/LAN (D3), dispositivo registrado con `Device.Heartbeat`,
`Device.Fingerprint.Identify` y `Device.Fingerprint.Enroll`, lector aprovisionado por serie, paciente sintético con
permisos de registro en el Web.

| # | Prueba | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| 1 | Sensor detectado | Encender con el R307 conectado (5 V, GND común, TX→GPIO16, RX←GPIO17) | `SENSOR_INIT` → `READ_PARAMETERS` sin `SENSOR_ERROR`. Antes: nivel del TX del R307 medido ≤ 3,3 V (o adaptado) | Pendiente |
| 2 | Capacidad leída | `status` | "capacidad del sensor" coincide con la del módulo (no se asume 1000) | Pendiente |
| 3 | Lectura de ocupación | Arranque con slots conocidos ocupados | Log "slot scan done": cantidad correcta; anotar si se usó la tabla de índices (`index table yes/no`) | Pendiente |
| 4 | Código de slot vacío | Con `SCIPH_DEBUG_SLOTS=1` en `esp32dev-devhttp`, consultar un slot vacío | `LoadChar` de un slot vacío responde `0x0C` (si responde otro código, el firmware lo trata como desconocido: anotarlo) | Pendiente |
| 5 | Registro | Web: "Registrar huella" con el lector; poner, retirar y volver a poner el mismo dedo | Mensajes en orden; `204`; la huella aparece activa en el Web | Pendiente |
| 6 | Mismo dedo identificado | App: sesión de identificación con el lector; mismo dedo | `Match`; el paramédico ve el candidato y confirma | Pendiente |
| 7 | Otro dedo: NoMatch | Sesión con un dedo no registrado | `NoMatch`; la atención sigue NN | Pendiente |
| 8 | Segundo registro | Otro paciente sintético en el mismo lector | Usa otro slot; ambos se identifican | Pendiente |
| 9 | Captura de baja calidad | Dedo seco/parcial repetidas veces | Hasta 3 reintentos locales, luego `LowQuality` | Pendiente |
| 10 | Dedos distintos al registrar | Captura 1 y 2 con dedos distintos | `Failed`; ningún slot nuevo ocupado | Pendiente |
| 11 | Revocación | Web: revocar la huella del punto 5 | La identificación deja de reconocerla de inmediato | Pendiente |
| 12 | Borrado físico | Esperar el sondeo de borrados (≤ 30 s) | `Deleted`; el Web deja de mostrar "pendiente de borrar"; el slot queda vacío | Pendiente |
| 13 | Re-registro | Registrar de nuevo al paciente del punto 11 | `204`; nueva referencia activa | Pendiente |
| 14 | Red caída | Apagar el punto de acceso | `NETWORK_RECOVERY`; reintentos 1, 2, 4… hasta 30 s; sin peticiones | Pendiente |
| 15 | Red recuperada | Encender el punto de acceso | Vuelve a `IDLE` sin reescanear; el sondeo continúa | Pendiente |
| 16 | Reinicio antes del Store | Cortar la alimentación tras la primera captura de un registro | Al arrancar: registro `Storing` + slot vacío → se descarta; nada en el servidor | Pendiente |
| 17 | Reinicio después del Store | Cortar la alimentación entre el `Store` y el `204` (Device API detenido justo antes) | Al arrancar: reenvía `Enrolled` + el mismo slot; `204` o `409` + borrado del slot | Pendiente |
| 18 | Confirmación perdida | Device API que no responde al `POST` del resultado (o red cortada en ese instante) | El slot se conserva; reenvío del mismo resultado; un solo registro en el servidor | Pendiente |
| 19 | Registro vencido | Abrir un registro y dejarlo vencer (120 s) sin capturar, o capturar tras vencer | Sin resultado o `409`; ningún slot queda ocupado | Pendiente |
| 20 | 409 del servidor | Registrar a un paciente que ya tiene huella activa en este lector | `409`; el slot recién escrito se borra | Pendiente |
| 21 | Lector revocado | Web: revocar el dispositivo | `401` → `AUTH_FAILED`, LED fijo, sin sondeo; heartbeat cada ~5 min; la clave sigue guardada | Pendiente |
| 22 | Scope faltante | Registrar un lector sin `Device.Fingerprint.Enroll` | `403` → `missing_scope`; la identificación sigue; sin reintentos en bucle | Pendiente |
| 23 | Límite de peticiones | Bajar `DevicePermitLimit` en Development | `429` → pausa según `Retry-After` | Pendiente |
| 24 | Reconciliación | Con el lector en marcha, `reconcile` | Log con cantidad de candidatos/borrados; ningún slot del inventario tocado | Pendiente |
| 25 | Recuperación de huérfano | Crear un huérfano (punto 17 con el registro del NVS borrado por `forget` + reaprovisionar, o slot escrito con otro programa) | Tras el segundo inventario, el huérfano se borra; los slots del servidor no | Pendiente |
| 26 | Huérfanos sin red | Repetir 25 con el Device API detenido | No se borra nada | Pendiente |
| 27 | Sensor desconectado | Desenchufar el R307 en `IDLE` | `SENSOR_ERROR`, reintento cada 10 s; al reconectar vuelve a leer capacidad y ocupación | Pendiente |
| 28 | Reinicio del lector | `restart` o reset | Arranque completo hasta `IDLE`; registros y borrados pendientes se reanudan | Pendiente |
| 29 | HTTPS | Device API con certificado firmado por el CA aprovisionado | Conecta; con un CA distinto, ninguna petición (sin `setInsecure`) | Pendiente |
| 30 | Sin hora | Bloquear NTP con HTTPS | Espera en `TIME_SYNC`; no hace peticiones | Pendiente |
| 31 | Logs | Revisar el monitor serie de todas las pruebas (sin `SCIPH_DEBUG_SLOTS`) | Ningún número de slot, clave, clave Wi-Fi ni dato de paciente | Pendiente |
| 32 | `forget` | `forget` + `FORGET` | Credencial, Wi-Fi, URL y CA borrados; plantillas del sensor intactas | Pendiente |
