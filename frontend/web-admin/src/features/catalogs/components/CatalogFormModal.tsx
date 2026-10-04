import { useEffect, useId, useState, type FormEvent } from 'react';
import type { CatalogInput, CatalogItemMap, CatalogKind } from '../../../types';
import { Button, FormError, Input, Modal, Select } from '../../../components/shared';
import { useSubmit } from '../../../hooks/useSubmit';
import { cn } from '../../../utils/cn';
import { toFormValues } from '../helpers';
import type { CatalogConfig, CatalogFormValues } from '../catalogConfig';

export interface CatalogFormModalProps<K extends CatalogKind> {
  open: boolean;
  config: CatalogConfig<K>;
  item?: CatalogItemMap[K];
  onClose: () => void;
  onSubmit: (input: CatalogInput<K>) => Promise<CatalogItemMap[K]>;
  onSaved: (item: CatalogItemMap[K], created: boolean) => void;
}

/** Formulario genérico de alta/edición: campos y validación zod definidos por la configuración del catálogo. */
export function CatalogFormModal<K extends CatalogKind>({ open, config, item, onClose, onSubmit, onSaved }: CatalogFormModalProps<K>) {
  const editing = !!item;
  const listId = useId();
  const submit = useSubmit();
  const [values, setValues] = useState<CatalogFormValues>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!open) return;
    setValues(toFormValues(config, item));
    setErrors({});
    submit.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, item, config]);

  useEffect(() => {
    if (submit.fieldError) setErrors((e) => ({ ...e, [submit.fieldError!.field]: submit.fieldError!.message }));
  }, [submit.fieldError]);

  const set = (name: string, value: string) => {
    setValues((v) => ({ ...v, [name]: value }));
    if (errors[name]) setErrors(({ [name]: _removed, ...rest }) => rest);
  };

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = config.schema.safeParse(values);
    if (!parsed.success) {
      const next: Record<string, string> = {};
      parsed.error.issues.forEach((i) => {
        const key = String(i.path[0] ?? '');
        if (key && !next[key]) next[key] = i.message;
      });
      setErrors(next);
      return;
    }
    const result = await submit.run(() => onSubmit(parsed.data));
    if (result) onSaved(result, !editing);
  };

  const errorCount = Object.keys(errors).length;

  return (
    <Modal
      open={open}
      onClose={onClose}
      dismissible={!submit.submitting}
      title={editing ? `Editar ${config.singular}` : config.newLabel}
      description={editing ? config.itemName(item) : 'Los campos marcados con * son obligatorios.'}
      footer={
        <>
          <span className="mr-auto text-xs text-ink-muted" aria-live="polite">
            {errorCount > 0 ? 'Revisa los campos marcados' : ''}
          </span>
          <Button variant="secondary" onClick={onClose} disabled={submit.submitting}>
            Cancelar
          </Button>
          <Button type="submit" form="catalog-form" loading={submit.submitting} loadingText="Guardando…">
            {editing ? 'Guardar cambios' : 'Crear'}
          </Button>
        </>
      }
    >
      <form id="catalog-form" noValidate onSubmit={save} className="flex flex-col gap-4">
        {submit.error && <FormError message={`No se pudo guardar. ${submit.error} Tus datos siguen en el formulario.`} />}
        {config.fields.map((f) =>
          f.type === 'select' ? (
            <Select
              key={f.name}
              label={f.label}
              required={f.required}
              placeholder="Selecciona"
              hint={f.hint}
              value={values[f.name] ?? ''}
              onChange={(e) => set(f.name, e.target.value)}
              options={(f.options ?? []).map((o) => ({ value: o, label: o }))}
              error={errors[f.name]}
            />
          ) : (
            <div key={f.name}>
              <Input
                label={f.label}
                required={f.required}
                placeholder={f.placeholder}
                hint={f.hint}
                value={values[f.name] ?? ''}
                onChange={(e) => set(f.name, e.target.value)}
                error={errors[f.name]}
                list={f.options ? `${listId}-${f.name}` : undefined}
                className={cn(f.mono && 'font-mono')}
                autoComplete="off"
              />
              {f.options && (
                <datalist id={`${listId}-${f.name}`}>
                  {f.options.map((o) => (
                    <option key={o} value={o} />
                  ))}
                </datalist>
              )}
            </div>
          ),
        )}
        <div role="radiogroup" aria-label="Estado" className="flex flex-col gap-2">
          <span className="text-[13px] font-semibold text-ink-2">Estado</span>
          <div className="flex flex-wrap gap-2">
            {config.statuses.map((s) => (
              <label
                key={s}
                className={cn(
                  'flex h-9 cursor-pointer items-center rounded-full border px-3 text-[13px]',
                  values.estado === s
                    ? 'border-primary-600 bg-primary-50 font-semibold text-primary-700'
                    : 'border-line text-ink-3 hover:bg-surface-subtle',
                )}
              >
                <input
                  type="radio"
                  className="sr-only"
                  name="estado"
                  value={s}
                  checked={values.estado === s}
                  onChange={() => set('estado', s)}
                />
                {s}
              </label>
            ))}
          </div>
        </div>
      </form>
    </Modal>
  );
}
