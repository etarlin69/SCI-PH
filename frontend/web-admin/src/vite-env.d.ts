/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origen del Web BFF para el navegador. Vacío (por defecto) = mismo origen vía proxy. */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
