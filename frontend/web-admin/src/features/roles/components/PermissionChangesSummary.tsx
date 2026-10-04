import type { ModuleDefinition, PermissionChange } from '../../../types';
import { cn } from '../../../utils/cn';
import { ACTION_LABEL } from '../permissions';

export interface PermissionChangesSummaryProps {
  changes: PermissionChange[];
  modules: ModuleDefinition[];
}

/** Lista "+ Conceder / − Retirar" mostrada en la confirmación de guardado. */
export function PermissionChangesSummary({ changes, modules }: PermissionChangesSummaryProps) {
  const label = (key: string) => modules.find((m) => m.key === key)?.label ?? key;
  const sensitive = (key: string) => !!modules.find((m) => m.key === key)?.sensitive;
  return (
    <ul className="flex max-h-60 flex-col gap-1.5 overflow-y-auto rounded-lg border border-line bg-surface-subtle p-3">
      {changes.map((c) => (
        <li key={`${c.module}-${c.action}`} className="flex items-center gap-2 text-[13px]">
          <span
            className={cn(
              'w-[68px] shrink-0 rounded px-1.5 py-0.5 text-center text-[11px] font-semibold',
              c.granted ? 'bg-success-50 text-success-700' : 'bg-danger-50 text-danger-text',
            )}
          >
            {c.granted ? '+ Conceder' : '− Retirar'}
          </span>
          <span className="text-ink">
            {label(c.module)} · {ACTION_LABEL[c.action]}
          </span>
          {c.granted && sensitive(c.module) && <span className="text-xs font-semibold text-warning-700">Sensible</span>}
        </li>
      ))}
    </ul>
  );
}
