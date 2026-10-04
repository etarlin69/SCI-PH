export interface Actor {
  nombre: string;
  email: string;
  rol: string;
}

let current: Actor = { nombre: 'Admin SCIH', email: 'admin@scih.com', rol: 'Administrador' };

export function getActor(): Actor {
  return current;
}

export function setActor(actor: Actor): void {
  current = actor;
}
