import { Building2 } from 'lucide-react';
import { ErrorState, Select } from '../../../components/shared';
import { selectClinic } from '../api/clinicSelection';
import { useClinicSelection } from '../hooks/useClinicSelection';

/**
 * Clínica en la que trabaja el usuario. Con una sola afiliación solo la muestra; con varias permite elegir (el backend
 * valida cada clinicId contra las afiliaciones activas). Si el usuario no es personal clínico, lo explica.
 */
export function ClinicSelector() {
    const { ready, error, clinics, selectedClinicId } = useClinicSelection();

    if (error) return <ErrorState variant="banner" title="Sin contexto clínico." message={error} />;
    if (!ready || clinics.length === 0) return null;

    if (clinics.length === 1) {
        return (
            <p className="m-0 flex items-center gap-1.5 text-[13px] text-ink-muted">
                <Building2 size={14} aria-hidden="true" />
                {clinics[0].name}
            </p>
        );
    }

    return (
        <Select
            label="Clínica"
            value={selectedClinicId ?? ''}
            onChange={(e) => selectClinic(e.target.value)}
            options={clinics.map((clinic) => ({ value: clinic.id, label: clinic.name }))}
            containerClassName="sm:w-72"
        />
    );
}
