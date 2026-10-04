# SCI-PH · Portal Web de Administración y Analítica (React)

Exportación a React de las tres tandas diseñadas en el canvas (Auth, Usuarios, Roles, Invitaciones, Dispositivos, Auditoría, Clínicas, Personal, Catálogos + Dashboard), con **datos mock** y **servicios mock** listos para sustituir por el Web.BFF.

**Stack:** Vite 5 · React 18 · TypeScript estricto · Tailwind 3 · React Router 7 (páginas con carga diferida) · react-hook-form + zod 4 · lucide-react · Recharts 3 (solo Dashboard).

## Arranque

```bash
npm install
npm run dev        # http://localhost:5173  → redirige a /login
npm run build      # tsc --noEmit + vite build
npm run lint
```

### Cuentas de demostración

| Cuenta | Contraseña | Para qué |
|---|---|---|
| `admin@scih.com` | `Clave2026!` | Administrador: acceso a todo |
| `cruz@auditores.do` | `Clave2026!` | Auditor externo (solo lectura): `/admin/usuarios`, `/admin/clinicas`, `/admin/personal`… muestran el **403** |

- MFA: código TOTP `123456` · código de recuperación `7KQ2-M9XD`.
- 5 intentos fallidos de contraseña o de código → bloqueo de 15 min.
- Restablecer contraseña: `/reset-password?token=demo` (válido), `?token=caducado`, `?token=usado`. Emails que empiezan por `limite` simulan el límite de envíos.
- Activar cuenta (invitación): `/activate-account?token=demo` · `caducado` · `usado`.
- Confirmar email: `/confirm-email?token=demo` · `caducado`.

### Escenarios forzables (añade a cualquier URL)

| Parámetro | Efecto |
|---|---|
| `?mock=slow` | Latencia de 2,5 s (estados de carga) |
| `?mock=error` | Todas las lecturas fallan con 503 (estado de error + Reintentar) |
| `?mock=empty` | Todas las listas vuelven vacías (estados vacíos con CTA) |
| `?mock=failwrite` | La primera escritura de cada operación falla (banner de error y reintento) |

## Rutas (`src/routes/index.tsx`)

| Públicas | Protegidas (sesión + RBAC) |
|---|---|
| `/login` · `/mfa` · `/forgot-password` · `/reset-password` · `/activate-account` · `/confirm-email` · `/403` · `/404` | `/admin` → `/admin/dashboard` · `/admin/usuarios[/:id]` · `/admin/invitaciones` · `/admin/roles[/:id/permisos]` · `/admin/dispositivos[/:id]` · `/admin/auditoria` · `/admin/clinicas[/:id]` · `/admin/personal[/:id]` · `/admin/catalogos[/:tipo]` · `/admin/perfil` |

`:tipo` de catálogos: `medicamentos`, `diagnosticos`, `procedimientos`, `ambulancias`, `turnos`.

## Estructura

```
src/
├── App.tsx                 BrowserRouter + <AppRoutes />
├── main.tsx                ToastProvider + AuthProvider
├── routes/                 index.tsx (mapa de rutas, carga diferida) · StatusShell (403/404 con o sin sesión)
├── auth/                   AuthContext (useAuth, useCan), RequireAuth, RequirePermission (403)
├── components/
│   ├── common/             Layout responsive, Sidebar (rail/drawer), Header (migas), MobileNav, PageMeta, navigation
│   ├── shared/             Design system (Button, Input, Table, Modal, Toast, Tabs, Badge, EmptyState, ErrorState…)
│   └── dashboard/          Widgets del Dashboard
├── features/<módulo>/      auth · users · invitations · roles · devices · audit · clinics · staff · catalogs · profile · dashboard
│   ├── pages/              Una página por ruta (export nombrado + default)
│   ├── components/         Un componente por archivo
│   ├── services/           <módulo>Service.ts (mock async; endpoints reales en docs/INTEGRACION_BACKEND.md)
│   ├── types/              <módulo>.types.ts
│   └── hooks/              (auth) useAuth
├── data/
│   ├── mock/               mockUsers · mockInvitations · mockDevices · mockAuditLog · mockClinics · mockStaff · mockCatalogs · mockRoles · mockAuth
│   └── index.ts            Agregados del Dashboard (dashboardFixtures.json, sin datos de pacientes)
├── services/               index.ts (barrel de todos los servicios) + core/ (errors, mock, actor)
├── types/                  index.ts (barrel) + common.ts
├── hooks/                  useAsync, useSubmit, useMediaQuery, useDebouncedValue, useDocumentTitle, useDashboard
└── utils/                  cn, csv, dates, formatters
```

### Componentes por tanda

| Tanda | Módulo | Páginas | Componentes principales |
|---|---|---|---|
| 1 | **Auth** | LoginPage, MfaPage, ForgotPasswordPage, ResetPasswordPage, ForbiddenPage (403), NotFoundPage (404) | AuthLayout, PasswordInput, PasswordStrengthMeter, OtpInput, AuthAlert, DemoCredentialsHint |
| 1 | **Usuarios** | UsersPage, UserDetailPage | UserFormModal, TemporaryPasswordField, UsersTable, UserFilters, UserSummaryCard, UserSecurityPanel, UserSessionsCard, SessionsTable, RevokeAllSessionsDialog, UserActivityList, UserStatusDialog, DeleteUserDialog |
| 1 | **Roles** | RolesPage, RolePermissionsPage | PermissionMatrix, RoleFormModal, RolesTable, PermissionChangesSummary, UnsavedChangesBar, RoleTypeBadge |
| 2 | **Invitaciones** | InvitationsPage | InvitePatientModal, InviteStaffModal, InvitationDetailDrawer, InvitationsTable, InvitationFilters, InvitationActionDialog |
| 2 | **Dispositivos** | DevicesPage, DeviceDetailPage | CreateDeviceModal (2 pasos), EnrollmentPanel, QrCode (codificador QR propio, sin dependencias), OneTimeSecret, DeviceActionDialog, RotatedCredentialModal, DeviceInfoCard, DeviceCredentialCard, DeviceAccessTable |
| 2 | **Auditoría** | AuditPage | AuditFilterBar, AuditFiltersSheet (móvil), ActiveFilterChips, AuditTable (25/50/100), AuditEventModal, AuditChangesTable, ExportMenu |
| 3 | **Clínicas** | ClinicsPage, ClinicDetailPage | ClinicFormModal, ClinicsTable, ClinicFilters, ClinicServicesPanel, CapacityBar, ClinicStaffTab, ClinicDevicesTab, ClinicStatusDialog, DeleteClinicDialog |
| 3 | **Personal** | StaffPage, StaffDetailPage | StaffFormModal (con invitación de acceso), AssignmentModal, LicenseModal, AssignmentsTab, LicensesTab, HistoryTimeline, StaffProfileCard, DeleteStaffDialog |
| 3 | **Catálogos** | CatalogsPage (pestañas en `?tab=`) | CatalogManager / CatalogTable / CatalogFormModal genéricos + MedicationsCatalog, DiagnosesCatalog (CIE-10 + ImportCsvModal), ProceduresCatalog, AmbulancesCatalog, ShiftsCatalog |

Responsive: una sola implementación por pantalla. < 640 px: menú desplegable, tablas con scroll horizontal y primera columna fija, filtros secundarios en hoja inferior, paneles laterales a ancho completo. 640–1023 px: menú de iconos. ≥ 1024 px: menú completo.

### Datos mock

Usuarios 20 · Invitaciones 15 · Dispositivos 10 (+ histórico de accesos) · Auditoría 240 eventos · Clínicas 5 (+ servicios) · Personal 20 (+ asignaciones, licencias, histórico) · Catálogos 10 por tipo (medicamentos, diagnósticos, procedimientos, ambulancias, turnos) · Roles 7.

## Reglas de datos aplicadas

- **Auditoría append-only:** ningún servicio ni pantalla edita o borra eventos; toda escritura de los demás servicios registra un evento (`record()`), incluidas las exportaciones.
- **Contraseñas nunca en claro:** la contraseña temporal de un alta no se pinta en pantalla (solo se puede copiar al portapapeles o enviarse por email); no aparece en toasts, listas ni auditoría. Los secretos de vinculación de dispositivos se muestran ocultos, una sola vez y caducan a los 15 min.
- **RBAC:** el menú y las rutas se filtran por el permiso `read` del rol; los botones de crear/editar/eliminar por `create`/`update`/`delete` (`useCan`). Sin permiso → página 403 sin cambiar la URL. El backend debe volver a comprobarlo en cada petición.

## Integración con el backend

El endpoint del Web.BFF que sustituye a cada método mock está en [`docs/INTEGRACION_BACKEND.md`](docs/INTEGRACION_BACKEND.md). Pasos recomendados:

1. Crear un cliente HTTP (`fetch` con `credentials: 'include'`) que convierta los Problem Details del BFF en `ServiceError` (`src/services/core/errors.ts`): la UI ya interpreta `CONFLICT` + `details.field`, `IN_USE`, `RATE_LIMITED`, `ACCOUNT_LOCKED`, `INVALID_CODE`, etc.
2. Sustituir el cuerpo de cada método manteniendo su firma; eliminar `src/services/core/mock.ts`, `core/actor.ts`, `src/data/mock/` y `DemoCredentialsHint`.
3. Sesión: el BFF fija una cookie HttpOnly + SameSite=Strict tras el MFA; `authService.getSession()` pasa a `GET /api/auth/session` (hoy usa `sessionStorage` solo para el mock).
4. Listas grandes (auditoría, usuarios): mover filtros, orden y paginación al servidor (parámetros en docs/INTEGRACION_BACKEND.md).
