import { Button } from '../../../components/shared';

export interface UnsavedChangesBarProps {
  count: number;
  roleName: string;
  saving: boolean;
  onDiscard: () => void;
  onSave: () => void;
}

/** Barra fija inferior con los cambios pendientes de la matriz. */
export function UnsavedChangesBar({ count, roleName, saving, onDiscard, onSave }: UnsavedChangesBarProps) {
  if (count === 0) return null;
  return (
    <div className="sticky bottom-3 z-[2] flex flex-col gap-3 rounded-xl border border-primary-300 bg-white px-4 py-3 shadow-[0_8px_24px_rgba(13,31,51,0.14)] sm:flex-row sm:items-center sm:justify-between">
      <p className="text-[13px] text-ink-3" aria-live="polite">
        <strong className="text-ink">
          {count} {count === 1 ? 'cambio sin guardar' : 'cambios sin guardar'}
        </strong>{' '}
        en «{roleName}». Se aplican en el próximo inicio de sesión.
      </p>
      <div className="flex gap-2">
        <Button variant="secondary" onClick={onDiscard} disabled={saving}>
          Descartar
        </Button>
        <Button onClick={onSave} loading={saving} loadingText="Guardando…">
          Guardar cambios
        </Button>
      </div>
    </div>
  );
}
