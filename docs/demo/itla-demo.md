# Guion de demostración (ITLA)

Recorrido de ~20 minutos por el flujo clínico completo de SCI-PH, con datos ficticios. Cada paso indica la aplicación,
el rol y qué mostrar. Lo que depende de credenciales o hardware se presenta según su nivel real de validación (ver
[Estado de las integraciones](../../README.md#estado-de-las-integraciones)).

## Preparación

- Stack local con cuentas de prueba por rol y la clínica de desarrollo: [Stack local en contenedores](../architecture/local-containers.md)
  (`IDENTITY_DEV_ACCOUNTS_ENABLED=true`). Una base **desechable** para la demo; nunca las bases de trabajo del equipo.
- Documentos: `DocumentStorage:Provider=FileSystem` (carpeta temporal) o Azurite (`AzureBlob`).
- Una segunda clínica (Administración → Clínicas) para el traslado entre hospitales, con un médico afiliado.
- App móvil (Android) apuntando al Mobile BFF; navegador con la web en `https`.

## Recorrido

| # | App · rol | Qué hacer | Qué mostrar |
|---|---|---|---|
| 1 | Web · Recepción | Registrar a la paciente con cédula **y correo** | El documento queda enmascarado; el correo permite invitarla |
| 2 | Mobile · Paramédico | Nueva emergencia (paciente NN) | Código NN temporal, sin paciente identificado |
| 3 | Mobile · Paramédico | Signos vitales (dos tomas), procedimientos (oxigenoterapia, vía venosa), medicación, nota | Todo en la MISMA emergencia, idempotente ante reintentos |
| 4 | Mobile · Paramédico | Identificar por documento: buscar → candidato → **Confirmar** (o QR / huella con el simulador) | Nada se identifica sin confirmación humana; el NN se conserva como trazabilidad |
| 5 | Mobile · Paramédico | Traslado al hospital y "En camino" | El hospital lo ve en Transferencias entrantes |
| 6 | Web · Médico | Registrar llegada | La misma atención pasa al hospital (Recibida); la base deja de verla |
| 7 | Web · Médico | Expediente | Signos y **procedimientos prehospitalarios** (marcados), con quién los hizo |
| 8 | Web · Enfermería / Médico | Triage, nota de evolución, diagnóstico, orden de **resultado** → valor final | El primer registro inicia la atención; el resultado tiene ciclo Pendiente → Final → Corregido |
| 9 | Web · Médico | Adjuntar un PDF de laboratorio y descargarlo | Validación por contenido real; descarga como adjunto; auditoría |
| 10 | Web · Médico | Prescribir y registrar administración (Kardex) | Medicación y egreso |
| 11 | Web · Médico | (Opcional) Traslado a la segunda clínica y recepción allí | La atención continúa en el hospital B con todo su historial |
| 12 | Web · Médico | Resumen asistido por IA | Proveedor simulado por defecto; requiere revisión profesional |
| 13 | Web · Médico | Egreso | Atención cerrada; queda de solo lectura |
| 14 | Web · Recepción | Invitar a la paciente a la app | Llega la invitación al correo registrado |
| 15 | Mobile · Paciente | Activar cuenta, Historial → **detalle de la atención** | Solo lo publicado: diagnósticos definitivos, resultados finales, recetas emitidas |
| 16 | Mobile · Paciente | Mi QR; Seguridad de la cuenta | QR de 15 minutos; cambiar contraseña cierra todas las sesiones |

## Límites de seguridad para demostrar

- Un médico de otra clínica no ve la atención (403); la base deja de verla al entregar al hospital.
- Recepción no lee datos clínicos; enfermería lee resultados pero no los registra.
- La paciente solo ve su propio expediente; la atención de otra persona es "no disponible" (404).
- No hay acceso administrativo "general" a datos clínicos: Administración no es personal clínico.

## Lo que se presenta como no validado en real

- IA con Azure OpenAI (requiere credenciales), precisión facial con cámara real, lector ESP32 + R307 físico, Azure Blob
  real (solo Azurite), cámara QR en dispositivo físico (solo emulador).
