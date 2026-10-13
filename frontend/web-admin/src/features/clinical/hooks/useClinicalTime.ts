import { useState } from 'react';
import { localInputToUtc, nowForDateTimeInput } from '../utils/clinicalLabels';

/**
 * Hora clínica en un <input type="datetime-local"> (precisión de minutos). Si el usuario no la cambia se envía la hora
 * exacta del envío: redondear al minuto podría dejarla antes del inicio de una atención recién abierta, y el backend
 * rechaza (400) horas futuras o anteriores al inicio de la atención.
 */
export function useClinicalTime() {
    const [value, setValue] = useState(nowForDateTimeInput);
    const [edited, setEdited] = useState(false);
    return {
        value,
        change: (next: string) => {
            setValue(next);
            setEdited(true);
        },
        toUtc: () => (edited ? localInputToUtc(value) : new Date().toISOString()),
        reset: () => {
            setValue(nowForDateTimeInput());
            setEdited(false);
        },
    };
}
