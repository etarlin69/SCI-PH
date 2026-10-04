import fixtures from './dashboardFixtures.json';
import type { CurrentUser, Hospital, HospitalId, TransferStatus } from '../types';

// MOCK: el Dashboard usa datos de prueba hasta que existan endpoints de agregados en el backend.
// La interfaz lo indica con la etiqueta "Datos de prueba".
export const USING_MOCK_DATA = true;

// Solo agregados: sin identificadores ni nombres de pacientes.
export interface MockTransfer {
    diasAtras: number;
    origen: string;
    destino: string;
    estado: TransferStatus;
}

export const HOSPITALES = fixtures.hospitales as Hospital[];
export const HOSPITAL_IDS: HospitalId[] = HOSPITALES.map((h) => h.id);
export const TRANSFERENCIAS_MOCK = fixtures.transferencias as MockTransfer[];

// MOCK: usuario mostrado en el shell hasta integrar GET /api/web/auth/me con el AuthProvider (PR posterior).
// No representa una sesión real, no concede permisos y no guarda tokens.
export const MOCK_CURRENT_USER: CurrentUser = {
    nombre: 'Usuario de prueba',
    email: 'admin@sci-ph.test',
    rol: 'Administrator',
};
