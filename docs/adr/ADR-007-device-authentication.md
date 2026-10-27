# ADR-007: Autenticación de dispositivos (ESP32)

- **Estado:** Propuesto para revisión en el PR de `feature/security-foundation`
- **Fecha:** 2026-10-02
- **Relacionado:** [ADR-006](ADR-006-identity-security.md), [contratos del Device API](../api/device-api-contracts.md)

## Contexto

Los ESP32 + R307 llaman al Device API sin una persona delante. Usar cuentas humanas (usuario, contraseña,
JWT, refresh tokens o roles) para el hardware mezclaría identidades, daría al dispositivo permisos pensados
para personas y obligaría a guardar credenciales humanas en el firmware.

## Decisión

- **Credencial propia por dispositivo**: `identity.DeviceCredentials` con `DeviceCode` único
  (`^[A-Z0-9][A-Z0-9-]{2,59}$`), nombre, scopes, **hash SHA-256 de la clave**, estado, firmware, fechas de
  alta, último contacto, rotación y revocación, y autor. Sin relación con `ApplicationUser`, roles, pacientes
  ni personal.
- **Clave**: 32 bytes de `RandomNumberGenerator` (Base64Url), devuelta una sola vez al crearla o rotarla; nunca
  se guarda ni se registra en logs. La comparación del hash es de tiempo constante y los dispositivos
  desconocidos también pagan el cálculo.
- **Esquema `DeviceApiKey`**: cabeceras `X-Device-Id` y `X-Device-Key`, nunca query string; exige HTTPS
  (`DeviceAuthentication:RequireHttps`, que solo el perfil local de desarrollo desactiva). El principal contiene
  únicamente `device_id`, `device_code` y `device_scope`: sin roles ni permisos humanos.
- **El Device API no registra JWT**: un token de usuario nunca sustituye a la credencial del dispositivo.
  Política por defecto: dispositivo autenticado; cada scope es una policy.
- **Scopes mínimos** según el contrato real: `Device.Heartbeat` y `Device.Fingerprint.Identify`. Nunca permisos
  clínicos: el dispositivo informa o resuelve una identificación; no recibe el expediente (también lo impide
  `CK_DeviceCredentials_Scopes`).
- **Administración** en el Web BFF con el permiso `Devices.Manage` (solo Administrador): crear, listar, rotar la
  clave (la anterior deja de funcionar al instante, sin periodo de gracia) y revocar.
- **Rate limiting** por dispositivo + IP (120/min). Se auditan los fallos (dispositivo desconocido, revocado o
  clave inválida), el alta, la rotación y la revocación, sin la clave.
- Implementado: `POST /api/device/heartbeat` (actualiza último contacto y firmware) y la identificación por huella
  (`GET /api/device/fingerprint/sessions/pending`, `POST …/{sessionId}/result`, scope `Device.Fingerprint.Identify`):
  el lector se identifica por su credencial y nunca recibe datos clínicos ([ADR-009](ADR-009-fingerprint-identification.md)).

## Consecuencias

- Rotar exige actualizar el firmware de inmediato; si hace falta continuidad, se agregará más adelante una
  ventana de doble clave.
- Las credenciales comparten la base y el DbContext de Identity para no crear otro contexto; siguen siendo un
  modelo separado de las cuentas.
- Certificados de cliente (mTLS) quedan como mejora posible si el despliegue lo permite.
