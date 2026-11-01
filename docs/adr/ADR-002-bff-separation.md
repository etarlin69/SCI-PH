# ADR-002: Separación en BFFs por tipo de cliente

- **Estado:** Aceptado
- **Fecha:** 2026-09-27

## Contexto

SCI-PH tiene tres tipos de cliente con necesidades distintas:

| Cliente | Necesidades |
|---|---|
| Web (React + TypeScript) | Área hospitalaria (recepción, traslados, evolución, resultados) y área de administración (usuarios, roles, auditoría, dispositivos) |
| Mobile (.NET MAUI) | Paciente (su cuenta y su propio expediente; QR de identificación, ADR-015) y paramédicos (emergencias en campo), con conectividad variable |
| ESP32 + R307 | Hardware limitado, payloads pequeños, autenticación de dispositivo en lugar de usuario |

Una única API genérica obligaría a cada cliente a adaptar respuestas que no fueron pensadas para él, y mezclaría modelos de autenticación diferentes (usuario vs. dispositivo).

## Decisión

Usamos el patrón **Backend for Frontend (BFF)** con tres aplicaciones independientes:

| Cliente | Punto de entrada | Prefijo de rutas |
|---|---|---|
| React | `SCI-PH.Web.BFF` | `/api/web` |
| .NET MAUI | `SCI-PH.Mobile.BFF` | `/api/mobile` |
| ESP32 | `SCI-PH.Device.API` | `/api/device` |

Reglas:

- Cada cliente llama **solo** a su propio BFF o API.
- Los BFF no contienen lógica de negocio: la lógica vive en Application y Domain y es compartida.
- Un BFF no llama a otro BFF.
- Cada BFF puede tener su propio esquema de autenticación y su propia forma de respuesta.

## Consecuencias

**Positivas**
- Cada contrato se diseña para su cliente.
- La seguridad de dispositivos queda aislada de la de usuarios.
- Cada punto de entrada se puede desplegar y escalar por separado.

**Negativas**
- Tres aplicaciones que configurar, desplegar y monitorear.
- Riesgo de duplicar lógica en los BFF si no se respeta que la lógica vive en Application.

**Pendiente**
- Mecanismo de autenticación de usuarios (web y móvil).
- Mecanismo de autenticación de dispositivos ESP32.
