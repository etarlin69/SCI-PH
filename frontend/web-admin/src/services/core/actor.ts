export interface Actor {
  nombre: string;
  email: string;
  rol: string;
}

// DEMO: autor de los eventos que generan las pantallas de demostración en el registro de auditoría ficticio.
// No es el usuario autenticado (ese llega de /api/web/auth/me) ni concede ningún permiso.
const current: Actor = { nombre: 'Usuario de demostración', email: 'demo@sci-ph.test', rol: 'Administrator' };

export function getActor(): Actor {
  return current;
}
