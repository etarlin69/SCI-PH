import { Users } from 'lucide-react';
import { EmptyState, PageHeader } from '../../../components/shared';

/** Pacientes: búsqueda, ficha e identificadores llegarán con su contrato en el backend (docs/HOSPITAL_CONTRATOS.md). */
export function PatientsPage() {
  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Pacientes" description="Búsqueda y ficha de pacientes." />
      <EmptyState
        icon={Users}
        title="Pendiente de integración con el backend"
        description="Mientras tanto, la identificación de pacientes y la atención NN se hacen desde Recepción."
      />
    </div>
  );
}
