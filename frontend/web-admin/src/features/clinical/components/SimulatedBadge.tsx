import { Badge } from '../../../components/shared';
import { isSimulated, type ClinicalFeature } from '../api/mode';

/** Marca «Simulación» solo en las secciones que todavía usan el adaptador MOCK (sin backend). */
export function SimulatedBadge({ feature }: { feature: ClinicalFeature }) {
    if (!isSimulated(feature)) return null;
    return (
        <span title="Datos ficticios en memoria: no se envían al backend y se pierden al recargar.">
            <Badge tone="warning">Simulación</Badge>
        </span>
    );
}
