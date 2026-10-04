import { useCallback, useEffect, useState } from 'react';
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Info } from 'lucide-react';
import { PageHeader, Tabs, tabIds } from '../../../components/shared';
import { catalogsService } from '../../../services';
import type { CatalogKind } from '../../../types';
import { AmbulancesCatalog } from '../components/AmbulancesCatalog';
import { DiagnosesCatalog } from '../components/DiagnosesCatalog';
import { MedicationsCatalog } from '../components/MedicationsCatalog';
import { ProceduresCatalog } from '../components/ProceduresCatalog';
import { ShiftsCatalog } from '../components/ShiftsCatalog';

const TABS: { value: CatalogKind; label: string }[] = [
  { value: 'medicamentos', label: 'Medicamentos' },
  { value: 'diagnosticos', label: 'Diagnósticos (CIE-10)' },
  { value: 'procedimientos', label: 'Procedimientos' },
  { value: 'ambulancias', label: 'Ambulancias' },
  { value: 'turnos', label: 'Turnos' },
];

const isKind = (v: string | null): v is CatalogKind => TABS.some((t) => t.value === v);

/** Catálogos maestros en pestañas. */
export function CatalogsPage() {
  const { tipo } = useParams<{ tipo?: string }>();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const legacy = params.get('tab');
  const tab: CatalogKind = isKind(tipo ?? null) ? (tipo as CatalogKind) : isKind(legacy) ? legacy : 'medicamentos';
  const [counts, setCounts] = useState<Partial<Record<CatalogKind, number>>>({});

  useEffect(() => {
    catalogsService.counts().then(setCounts, () => undefined);
  }, []);

  const onCountChange = useCallback((kind: CatalogKind, n: number) => setCounts((c) => (c[kind] === n ? c : { ...c, [kind]: n })), []);
  const setTab = (value: CatalogKind) => navigate(`/admin/catalogos/${value}`, { replace: true });

  // Tipo desconocido en la URL → 404 coherente con el resto del portal.
  if (tipo !== undefined && !isKind(tipo)) return <Navigate to="/404" replace />;

  const ids = tabIds('catalogs', tab);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Catálogos maestros" description="Tablas de referencia que alimentan los formularios clínicos y operativos." />
      <p className="flex items-center gap-2 text-[13px] text-ink-muted">
        <Info size={14} aria-hidden="true" />
        Los hospitales ahora se gestionan en{' '}
        <Link to="/admin/clinicas" className="font-semibold text-primary-600 hover:underline">
          Clínicas
        </Link>
        .
      </p>
      <section className="flex flex-col rounded-[10px] border border-line bg-white" aria-label="Catálogos">
        <div className="relative overflow-x-auto border-b border-line px-4 pt-2 sm:px-5">
          <Tabs<CatalogKind>
            label="Catálogos"
            idPrefix="catalogs"
            value={tab}
            onChange={setTab}
            tabs={TABS.map((t) => ({ ...t, count: counts[t.value] }))}
          />
        </div>
        <div role="tabpanel" id={ids.panel} aria-labelledby={ids.tab} className="p-4 sm:p-5">
          {tab === 'medicamentos' && <MedicationsCatalog onCountChange={onCountChange} />}
          {tab === 'diagnosticos' && <DiagnosesCatalog onCountChange={onCountChange} />}
          {tab === 'procedimientos' && <ProceduresCatalog onCountChange={onCountChange} />}
          {tab === 'ambulancias' && <AmbulancesCatalog onCountChange={onCountChange} />}
          {tab === 'turnos' && <ShiftsCatalog onCountChange={onCountChange} />}
        </div>
      </section>
    </div>
  );
}

export default CatalogsPage;
