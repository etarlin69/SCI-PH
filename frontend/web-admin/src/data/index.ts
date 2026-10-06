import fixtures from './dashboardFixtures.json';
import type { Hospital, HospitalId, TransferStatus } from '../types';

export const USING_MOCK_DATA = true;

export interface MockTransfer {
  diasAtras: number;
  origen: string;
  destino: string;
  estado: TransferStatus;
}

export const HOSPITALES = fixtures.hospitales as Hospital[];
export const HOSPITAL_IDS: HospitalId[] = HOSPITALES.map((h) => h.id);
export const TRANSFERENCIAS_MOCK = fixtures.transferencias as MockTransfer[];
