import { Minus } from 'lucide-react';
import { PERMISSION_ACTIONS } from '../../../data/mock';
import type { ModuleDefinition, PermissionMatrix as Matrix } from '../../../types';
import { Badge } from '../../../components/shared';
import { cn } from '../../../utils/cn';
import { has, setModuleAll, togglePermission } from '../permissions';

export interface PermissionMatrixProps {
  modules: ModuleDefinition[];
  value: Matrix;
  saved: Matrix;
  onChange: (value: Matrix) => void;
  readOnly: boolean;
  roleName: string;
}

/** Matriz módulos × acciones. */
export function PermissionMatrix({ modules, value, saved, onChange, readOnly, roleName }: PermissionMatrixProps) {
  return (
    <div
      className="relative overflow-x-auto rounded-[10px] border border-line bg-white"
      role="region"
      aria-label={`Permisos de ${roleName}`}
      tabIndex={0}
    >
      <table className="w-full min-w-[720px] border-collapse text-[13px]">
        <caption className="sr-only">Permisos de {roleName} por módulo</caption>
        <thead>
          <tr className="bg-surface-subtle text-xs font-semibold text-ink-3">
            <th scope="col" className="sticky left-0 z-[1] bg-surface-subtle px-4 py-3 text-left shadow-[1px_0_0_#DCE2E8]">
              Módulo
            </th>
            {PERMISSION_ACTIONS.map((a) => (
              <th key={a.key} scope="col" className="w-[110px] px-2 py-3 text-center">
                {a.label}
              </th>
            ))}
            {!readOnly && (
              <th scope="col" className="w-[90px] px-2 py-3 text-center">
                Todo
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {modules.map((mod) => {
            const applicable = PERMISSION_ACTIONS.filter((a) => !mod.notApplicable.includes(a.key));
            const allOn = applicable.every((a) => has(value, mod.key, a.key));
            return (
              <tr key={mod.key} className="group border-t border-line-soft hover:bg-surface-subtle">
                <th
                  scope="row"
                  className="sticky left-0 z-[1] bg-white px-4 py-3 text-left font-normal shadow-[1px_0_0_#EDF0F3] group-hover:bg-surface-subtle"
                >
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-ink">{mod.label}</span>
                    {mod.sensitive && (
                      <Badge tone="warning" dot={false}>
                        Sensible
                      </Badge>
                    )}
                  </span>
                  <span className="block text-xs text-ink-muted">{mod.description}</span>
                </th>
                {PERMISSION_ACTIONS.map((a) => {
                  if (mod.notApplicable.includes(a.key)) {
                    return (
                      <td key={a.key} className="text-center text-ink-muted">
                        <span title={mod.key === 'audit' ? 'No aplica: la auditoría es de solo lectura (append-only)' : 'No aplica'}>
                          <Minus size={14} className="mx-auto" aria-hidden="true" />
                          <span className="sr-only">No aplica</span>
                        </span>
                      </td>
                    );
                  }
                  const checked = has(value, mod.key, a.key);
                  const changed = checked !== has(saved, mod.key, a.key);
                  return (
                    <td key={a.key} className={cn('text-center', changed && 'bg-primary-50')}>
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={readOnly}
                        onChange={() => onChange(togglePermission(value, mod, a.key))}
                        aria-label={`${a.label} en ${mod.label}${changed ? ' (cambio sin guardar)' : ''}`}
                        className="h-[18px] w-[18px] cursor-pointer accent-primary-600 disabled:cursor-not-allowed"
                      />
                    </td>
                  );
                })}
                {!readOnly && (
                  <td className="text-center">
                    <button
                      type="button"
                      onClick={() => onChange(setModuleAll(value, mod, !allOn))}
                      className="rounded px-2 py-1 text-xs font-semibold text-primary-600 hover:bg-primary-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
                      aria-label={`${allOn ? 'Quitar' : 'Conceder'} todos los permisos de ${mod.label}`}
                    >
                      {allOn ? 'Ninguno' : 'Todos'}
                    </button>
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
