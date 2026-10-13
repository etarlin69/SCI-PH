import type { AuditChange } from '../../../types';

export interface AuditChangesTableProps {
  changes: AuditChange[];
}

/** Cambios "antes / después" de un evento. */
export function AuditChangesTable({ changes }: AuditChangesTableProps) {
  if (changes.length === 0) return null;
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-[13px] font-semibold text-ink-2">Cambios</h3>
      <div className="hidden overflow-hidden rounded-lg border border-line sm:block">
        <table className="w-full border-collapse text-[13px]">
          <caption className="sr-only">Cambios antes y después</caption>
          <thead>
            <tr className="bg-surface-subtle text-left text-xs font-semibold text-ink-3">
              <th scope="col" className="h-9 px-3">
                Campo
              </th>
              <th scope="col" className="h-9 px-3">
                Antes
              </th>
              <th scope="col" className="h-9 px-3">
                Después
              </th>
            </tr>
          </thead>
          <tbody>
            {changes.map((c) => (
              <tr key={c.campo} className="border-t border-line-soft align-top">
                <th scope="row" className="px-3 py-2.5 text-left font-medium text-ink">
                  {c.campo}
                </th>
                <td className="px-3 py-2.5">
                  <span className="rounded bg-danger-50 px-1.5 py-0.5 text-danger-text">{c.antes}</span>
                </td>
                <td className="px-3 py-2.5">
                  <span className="rounded bg-success-50 px-1.5 py-0.5 text-success-700">{c.despues}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-col gap-2 sm:hidden" aria-label="Cambios antes y después">
        {changes.map((c) => (
          <li key={c.campo} className="flex flex-col gap-1 rounded-lg border border-line px-3 py-2.5 text-[13px]">
            <span className="font-semibold text-ink">{c.campo}</span>
            <span className="text-ink-3">
              Antes: <span className="text-danger-text">{c.antes}</span>
            </span>
            <span className="text-ink-3">
              Después: <span className="text-success-700">{c.despues}</span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
