# SCI-PH · Frontend web (Administración; base compartida con Hospital)

SPA en React que se autentica contra **SCI-PH.Web.BFF**. La autenticación, la sesión, los permisos y los módulos
Usuarios, Roles, Invitaciones y Dispositivos usan la API real. Dashboard, Auditoría, Clínicas, Personal y Catálogos
son **pantallas de demostración** con datos ficticios hasta que el backend publique sus endpoints (se indican con un
aviso en pantalla y la etiqueta «Datos de demostración»).

**Stack:** Vite 5 · React 18 · TypeScript estricto · Tailwind 3 · React Router 7 · react-hook-form + zod · Vitest + Testing Library.

## Comandos

```bash
npm ci
npm run dev      # http://localhost:5173
npm run build    # tsc --noEmit + vite build
npm run lint
npm test         # vitest run
```

## Desarrollo con el Web BFF

1. Copia `.env.example` a `.env.development.local` (ignorado por git). `VITE_WEB_BFF_URL` es el origen del BFF
   (perfil `https` de `launchSettings.json`: `https://localhost:7003`). Vite redirige `/api` a esa URL.
2. Arranca el BFF con su configuración de desarrollo (base de datos local, secretos en User Secrets; nunca en el repo).
   `Security:AllowedOrigins` de Development ya admite `http(s)://localhost:5173`.
3. `npm run dev`.

**HTTPS y la cookie `__Host-sciph-refresh`.** La cookie es `Secure`. Con el proxy el navegador solo habla con
`localhost:5173`, que Chrome, Edge y Firefox tratan como contexto seguro, así que la cookie funciona también en
`http://localhost`. Para servir la SPA por HTTPS usa el certificado de desarrollo de .NET (`dotnet dev-certs https
--trust` y exportarlo en PEM) y define `VITE_DEV_HTTPS_CERT` y `VITE_DEV_HTTPS_KEY`. No se desactiva `Secure` en el backend.

**Otros orígenes.** Si en un despliegue la SPA y el BFF no comparten origen, define `VITE_API_BASE_URL`; el BFF
debe incluir ese origen en `Security:AllowedOrigins` (con ese montaje `Retry-After` no es legible desde JS).

No hay cuentas ni contraseñas de demostración: para probar en local usa el bootstrap de Identity del backend
(`IdentityBootstrap`, credenciales solo en User Secrets) o invita una cuenta.

## Seguridad en la SPA

- `src/api/` — cliente HTTP único: Bearer en memoria, `credentials: 'include'`, `X-SCIPH-CSRF: 1` en login,
  mfa/verify, refresh y logout, ProblemDetails (`ApiError`), `Retry-After`, refresh single-flight con un reintento.
- `src/auth/` — `AuthProvider`/`useAuth` (`initializing | authenticated | unauthenticated`), `RequireAuth`,
  `RequirePermission` (permisos reales `Users.Manage`, `Audit.Read`…), roles reales (`Administrator`, `Doctor`…),
  y `APP_MODULES` para decidir el destino tras el login sin depender de la navegación de un módulo.
- Ni el access token, ni el desafío MFA, ni los códigos de recuperación, ni las claves de dispositivo se guardan en
  `localStorage`/`sessionStorage`. El refresh token nunca es accesible desde JavaScript.
- El frontend solo oculta lo que el usuario no puede usar: el backend vuelve a autorizar cada petición.

Contratos y huecos de backend: [`docs/INTEGRACION_BACKEND.md`](docs/INTEGRACION_BACKEND.md).

## Reutilización desde el módulo Hospital (Rol 4)

`src/api` y `src/auth` no dependen de ninguna pantalla de administración. El módulo hospitalario usa `apiRequest`,
`useAuth`, `RequireAuth` y `RequirePermission`, y se registra en `APP_MODULES` (`src/auth/modules.ts`) para que el
login lleve a `/hospital` a quien tenga permisos clínicos.

## Estructura

```
src/
├── api/        Cliente HTTP, ApiError, ProblemDetails
├── auth/       AuthProvider, guards, permisos y roles, authApi, módulos
├── routes/     Mapa de rutas (públicas, /admin protegidas)
├── components/ common (Layout, Sidebar, navegación) · shared (design system) · security (QR)
├── features/   auth · users · roles · invitations · devices · profile (reales)
│               dashboard · audit · clinics · staff · catalogs (DEMO)
├── data/mock/  Datos ficticios de los módulos DEMO (sin cuentas ni credenciales)
├── services/   Barrel de servicios + core (errores y utilidades de los mocks DEMO)
└── test/       Setup de Vitest y BFF simulado para los tests
```
