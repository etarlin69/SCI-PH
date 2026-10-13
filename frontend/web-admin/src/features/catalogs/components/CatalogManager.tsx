import { useEffect, useMemo, useState } from 'react';
import { Layers, Plus, SearchX, Upload } from 'lucide-react';
import { useCanAny } from '../../../auth';
import { DEMO_ADMIN_PERMISSIONS } from '../../../components/common/navigation';
import {
  Button,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  ResponsiveFilters,
  SearchInput,
  SegmentedFilter,
  Select,
  useToast,
} from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue';
import { CATALOG_OFF_STATUS, catalogsService, errorMessage } from '../../../services';
import type { CatalogItemMap, CatalogKind } from '../../../types';
import { fieldValue, filterCatalog } from '../helpers';
import type { CatalogConfig } from '../catalogConfig';
import { CatalogFormModal } from './CatalogFormModal';
import { CatalogTable } from './CatalogTable';
import { DeleteCatalogItemDialog } from './DeleteCatalogItemDialog';
import { ImportCsvModal } from './ImportCsvModal';

export interface CatalogManagerProps<K extends CatalogKind> {
  config: CatalogConfig<K>;
  onCountChange?: (kind: K, count: number) => void;
}

/** Gestión genérica de un catálogo: búsqueda, filtros, tabla y alta/edición/baja/desactivación. */
export function CatalogManager<K extends CatalogKind>({ config, onCountChange }: CatalogManagerProps<K>) {
  const toast = useToast();
  const canCreate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canUpdate = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const canDelete = useCanAny(DEMO_ADMIN_PERMISSIONS);
  const { data, loading, error, reload, setData } = useAsync(() => catalogsService.list(config.kind), [config.kind]);
  const items = useMemo(() => data ?? [], [data]);

  const [q, setQ] = useState('');
  const [filter, setFilter] = useState('');
  const [estado, setEstado] = useState('');
  const dq = useDebouncedValue(q);
  const filtered = useMemo(() => filterCatalog(items, config, { q: dq, filter, estado }), [items, config, dq, filter, estado]);
  const [form, setForm] = useState<{ open: boolean; item?: CatalogItemMap[K] }>({ open: false });
  const [toDelete, setToDelete] = useState<CatalogItemMap[K] | null>(null);
  const [toDeactivate, setToDeactivate] = useState<CatalogItemMap[K] | null>(null);
  const off = CATALOG_OFF_STATUS[config.kind] as string;
  const [importOpen, setImportOpen] = useState(false);

  useEffect(() => {
    if (data) onCountChange?.(config.kind, data.length);
  }, [data, config.kind, onCountChange]);

  const clear = () => {
    setQ('');
    setFilter('');
    setEstado('');
  };
  const replace = (item: CatalogItemMap[K]) => setData((prev) => prev?.map((x) => (x.id === item.id ? item : x)));

  const onConfirmDelete = async (mode: 'delete' | 'deactivate', item: CatalogItemMap[K]) => {
    const name = config.itemName(item);
    try {
      if (mode === 'delete') {
        await catalogsService.remove(config.kind, item.id);
        setData((prev) => prev?.filter((x) => x.id !== item.id));
        toast({ title: 'Registro eliminado', description: name, tone: 'success' });
      } else {
        replace(await catalogsService.deactivate(config.kind, item.id));
        toast({ title: 'Registro desactivado', description: `${name} ya no aparece en formularios nuevos.`, tone: 'success' });
      }
    } catch (e) {
      toast({ title: 'No se pudo completar la acción', description: errorMessage(e), tone: 'error' });
    } finally {
      setToDelete(null);
      setToDeactivate(null);
    }
  };

  const statusCounts = filterCatalog(items, config, { q: dq, filter, estado: '' });
  const filterOptions = config.filter
    ? [...new Set(items.map((i) => fieldValue(i, config.filter!.field)))].sort((a, b) => a.localeCompare(b, 'es'))
    : [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <p className="max-w-2xl text-[13px] text-ink-muted">{config.description}</p>
        <div className="flex shrink-0 flex-wrap gap-2">
          {config.csvImport && canCreate && (
            <Button variant="secondary" leftIcon={<Upload size={16} aria-hidden="true" />} onClick={() => setImportOpen(true)}>
              Importar CSV
            </Button>
          )}
          {canCreate && (
            <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setForm({ open: true })}>
              {config.newLabel}
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
          <div className="flex flex-1 flex-wrap items-end gap-3">
            <SearchInput label={`Buscar en ${config.label}`} placeholder={config.searchPlaceholder} value={q} onChange={setQ} />
            {config.filter && (
              <ResponsiveFilters activeCount={filter ? 1 : 0} onClear={() => setFilter('')} resultCount={filtered.length}>
                <Select
                  label={config.filter.label}
                  layout="inline"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  options={[{ value: '', label: config.filter.allLabel }, ...filterOptions.map((o) => ({ value: o, label: o }))]}
                  containerClassName="sm:min-w-[200px]"
                />
              </ResponsiveFilters>
            )}
          </div>
          {(q || filter || estado) && (
            <Button variant="ghost" size="sm" onClick={clear}>
              Limpiar
            </Button>
          )}
        </div>
        <SegmentedFilter<string>
          label="Filtrar por estado"
          value={estado}
          onChange={setEstado}
          options={[
            { value: '', label: 'Todos', count: statusCounts.length },
            ...config.statuses.map((s) => ({
              value: s as string,
              label: s as string,
              count: statusCounts.filter((i) => fieldValue(i, 'estado') === s).length,
            })),
          ]}
        />
      </div>

      {error ? (
        <ErrorState title="No se pudo cargar el catálogo" message={error} onRetry={reload} />
      ) : (
        <CatalogTable
          config={config}
          items={filtered}
          loading={loading}
          canUpdate={canUpdate}
          canDelete={canDelete}
          onEdit={(item) => setForm({ open: true, item })}
          onDeactivate={setToDeactivate}
          onDelete={setToDelete}
          resetPageKey={`${dq}|${filter}|${estado}`}
          emptyState={
            items.length === 0 ? (
              <EmptyState
                icon={Layers}
                title="Este catálogo está vacío"
                description="Añade el primer registro para que aparezca en los formularios clínicos."
                action={canCreate && <Button onClick={() => setForm({ open: true })}>{config.newLabel}</Button>}
              />
            ) : (
              <EmptyState
                icon={SearchX}
                title="Ningún registro coincide con los filtros"
                action={
                  <Button variant="secondary" onClick={clear}>
                    Limpiar filtros
                  </Button>
                }
              />
            )
          }
        />
      )}

      <CatalogFormModal
        open={form.open}
        config={config}
        item={form.item}
        onClose={() => setForm({ open: false })}
        onSubmit={(input) =>
          form.item ? catalogsService.update(config.kind, form.item.id, input) : catalogsService.create(config.kind, input)
        }
        onSaved={(item, created) => {
          setForm({ open: false });
          if (created) setData((prev) => [item, ...(prev ?? [])]);
          else replace(item);
          toast({ title: created ? config.createdLabel : 'Cambios guardados', description: config.itemName(item), tone: 'success' });
        }}
      />
      <ConfirmDialog
        open={!!toDeactivate}
        tone="primary"
        title={`¿Cambiar «${toDeactivate ? config.itemName(toDeactivate) : ''}» a «${off}»?`}
        message="Dejará de aparecer en formularios nuevos. Los registros que ya lo usan se conservan. Queda registrado en auditoría."
        confirmLabel={off === 'Mantenimiento' ? 'Pasar a mantenimiento' : 'Desactivar'}
        onConfirm={() => (toDeactivate ? onConfirmDelete('deactivate', toDeactivate) : undefined)}
        onCancel={() => setToDeactivate(null)}
      />
      <DeleteCatalogItemDialog config={config} item={toDelete} onConfirm={onConfirmDelete} onCancel={() => setToDelete(null)} />
      {config.csvImport && (
        <ImportCsvModal
          open={importOpen}
          onClose={() => setImportOpen(false)}
          onImported={(n, skipped) => {
            setImportOpen(false);
            reload();
            toast({
              title: 'Importación completada',
              description: `${n} diagnósticos añadidos${skipped ? `, ${skipped} filas omitidas` : ''}.`,
              tone: 'success',
            });
          }}
        />
      )}
    </div>
  );
}
