import type { Role } from '../types';

// Etiquetas en español de los roles del backend. Las claves son los identificadores reales de SecurityRoles.
export const ROLE_LABELS: Record<Role, string> = {
    Administrator: 'Administrador',
    Doctor: 'Médico',
    Nurse: 'Enfermería',
    Paramedic: 'Paramédico',
    Receptionist: 'Recepción',
    Patient: 'Paciente',
};
