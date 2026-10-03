# Fixtures de referencia (no se incluyen en el bundle)

Datos de prueba que **no importa ningún archivo de `src/`**, así que Vite no los empaqueta. Sirven solo como
referencia para prototipos de PR posteriores; no son contratos del backend.

| Archivo | Uso previsto | Contrato real |
|---|---|---|
| `medication-catalog.json` | Prototipo de la pantalla de catálogos | Pendiente en el backend (el catálogo de medicamentos existe en Domain, sin endpoint admin todavía) |

Los datos del Dashboard viven en `src/data/dashboardFixtures.json` (mínimos y sin datos personales).

Se retiraron del prototipo original (recuperables con `git show 7ed26d2:frontend/web-admin/src/data/mockData.json`):

- usuarios, historial de permisos y log de auditoría: usaban roles que no existen en el backend (`Operador`,
  `Médico`, `Enfermero`, `Admin`) y formas que contradicen los contratos reales (`GET /api/web/admin/users`, auditoría
  de seguridad). Las pantallas de Usuarios y Auditoría deben construirse sobre la API.
- diagnósticos y procedimientos (catálogos clínicos, fuera del alcance del Rol 5) y ambulancias/turnos (sin concepto en
  el backend).
