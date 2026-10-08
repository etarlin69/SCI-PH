/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origen del Web BFF para el navegador. Vacío (por defecto) = mismo origen vía proxy. */
  readonly VITE_API_BASE_URL?: string;
  /** `mock` fuerza el adaptador clínico simulado completo (desarrollo visual sin backend). Por defecto: HTTP real. */
  readonly VITE_CLINICAL_API_MODE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
