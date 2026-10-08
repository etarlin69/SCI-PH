import { Fragment, useEffect, useId, useMemo, useState, type MouseEvent, type ReactNode } from 'react';
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight } from 'lucide-react';
import { cn } from '../../utils/cn';
import { formatNumber } from '../../utils/formatters';

export type SortDirection = 'asc' | 'desc';

export interface Column<T> {
  id: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  sortValue?: (row: T) => string | number;
  width?: string;
  align?: 'left' | 'right' | 'center';
  className?: string;
}

export interface SortState {
  columnId: string;
  direction: SortDirection;
}

export interface TableProps<T> {
  columns: Column<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  caption: string;
  pageSize?: number;
  initialSort?: SortState;
  loading?: boolean;
  emptyState?: ReactNode;
  dense?: boolean;
  rowClassName?: (row: T) => string | undefined;
  resetPageKey?: unknown;
  expandedRowIds?: ReadonlySet<string>;
  renderExpanded?: (row: T) => ReactNode;
  onRowClick?: (row: T) => void;
  pageSizeOptions?: number[];
  stickyFirstColumn?: boolean;
  minWidth?: string;
}

/** Id del contenedor expandido de una fila, para enlazarlo con `aria-controls`. */
export const expandedRegionId = (rowId: string) => `row-detail-${rowId}`;

const ALIGN = { left: 'text-left', right: 'text-right', center: 'text-center' };

export function Table<T>({
  columns,
  rows,
  getRowId,
  caption,
  pageSize,
  initialSort,
  loading = false,
  emptyState = 'Sin resultados.',
  dense = false,
  rowClassName,
  resetPageKey,
  expandedRowIds,
  renderExpanded,
  onRowClick,
  pageSizeOptions,
  stickyFirstColumn = false,
  minWidth,
}: TableProps<T>) {
  const [sort, setSort] = useState<SortState | null>(initialSort ?? null);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(pageSize);
  useEffect(() => setSize(pageSize), [pageSize]);
  pageSize = size;

  // Al cambiar los filtros, vuelve a la primera página.
  useEffect(() => setPage(1), [resetPageKey]);

  const sorted = useMemo(() => {
    const column = sort ? columns.find((c) => c.id === sort.columnId) : undefined;
    if (!sort || !column?.sortValue) return rows;
    const value = column.sortValue;
    return [...rows].sort((a, b) => {
      const va = value(a);
      const vb = value(b);
      const r = typeof va === 'number' && typeof vb === 'number' ? va - vb : String(va).localeCompare(String(vb), 'es');
      return sort.direction === 'asc' ? r : -r;
    });
  }, [rows, sort, columns]);

  const pageCount = pageSize ? Math.max(1, Math.ceil(sorted.length / pageSize)) : 1;
  const current = Math.min(page, pageCount);
  const visible = pageSize ? sorted.slice((current - 1) * pageSize, current * pageSize) : sorted;

  const toggleSort = (columnId: string) =>
    setSort((s) =>
      s?.columnId === columnId ? { columnId, direction: s.direction === 'asc' ? 'desc' : 'asc' } : { columnId, direction: 'asc' },
    );

  return (
    <div className="flex flex-col">
      <div
        className="relative overflow-x-auto"
        tabIndex={minWidth ? 0 : undefined}
        role={minWidth ? 'region' : undefined}
        aria-label={minWidth ? `${caption} (desplazable)` : undefined}
      >
        <table className="w-full border-collapse text-[13px]" style={{ minWidth }} aria-busy={loading || undefined}>
          <caption className="sr-only">{caption}</caption>
          <thead>
            <tr>
              {columns.map((c) => {
                const active = sort?.columnId === c.id;
                const ariaSort = active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : c.sortValue ? 'none' : undefined;
                return (
                  <th
                    key={c.id}
                    scope="col"
                    aria-sort={ariaSort}
                    style={{ width: c.width }}
                    className={cn(
                      'h-9 whitespace-nowrap bg-surface-subtle px-3 text-xs font-semibold text-ink-3 first:rounded-l-md last:rounded-r-md',
                      ALIGN[c.align ?? 'left'],
                      stickyFirstColumn && 'first:sticky first:left-0 first:z-[1] first:shadow-[1px_0_0_#DCE2E8]',
                    )}
                  >
                    {c.sortValue ? (
                      <button
                        type="button"
                        onClick={() => toggleSort(c.id)}
                        className="inline-flex items-center gap-1 rounded hover:text-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
                      >
                        {c.header}
                        {active ? (
                          sort.direction === 'asc' ? (
                            <ArrowUp size={12} aria-hidden="true" />
                          ) : (
                            <ArrowDown size={12} aria-hidden="true" />
                          )
                        ) : (
                          <ArrowUpDown size={12} className="opacity-50" aria-hidden="true" />
                        )}
                      </button>
                    ) : (
                      c.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              Array.from({ length: Math.min(pageSize ?? 5, 5) }, (_, i) => (
                <tr key={`sk-${i}`} className="border-b border-line-soft">
                  {columns.map((c) => (
                    <td key={c.id} className={cn('px-3', dense ? 'h-10' : 'h-12')}>
                      <span className="block h-3 w-3/4 animate-pulse rounded bg-line-soft" />
                    </td>
                  ))}
                </tr>
              ))
            ) : visible.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="px-3 py-10 text-center text-[13px] text-ink-muted">
                  {emptyState}
                </td>
              </tr>
            ) : (
              visible.map((row) => {
                const id = getRowId(row);
                const expanded = !!renderExpanded && !!expandedRowIds?.has(id);
                const handleClick = onRowClick
                  ? (e: MouseEvent<HTMLTableRowElement>) => {
                      if ((e.target as HTMLElement).closest('button, a, input, select, textarea, label')) return;
                      onRowClick(row);
                    }
                  : undefined;
                return (
                  <Fragment key={id}>
                    <tr
                      onClick={handleClick}
                      className={cn(
                        'group border-b border-line-soft hover:bg-surface-subtle',
                        onRowClick && 'cursor-pointer',
                        expanded && 'border-b-0 bg-surface-subtle',
                        rowClassName?.(row),
                      )}
                    >
                      {columns.map((c) => (
                        <td
                          key={c.id}
                          className={cn(
                            'px-3',
                            dense ? 'h-10' : 'h-12',
                            ALIGN[c.align ?? 'left'],
                            stickyFirstColumn &&
                              'first:sticky first:left-0 first:z-[1] first:bg-white first:shadow-[1px_0_0_#EDF0F3] group-hover:first:bg-surface-subtle',
                            c.className,
                          )}
                        >
                          {c.cell(row)}
                        </td>
                      ))}
                    </tr>
                    {expanded && (
                      <tr className="border-b border-line-soft bg-surface-subtle">
                        <td id={expandedRegionId(id)} colSpan={columns.length} className="px-3 pb-4 pt-1">
                          {renderExpanded(row)}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })
            )}
          </tbody>
        </table>
      </div>
      {pageSize !== undefined && !loading && (
        <Pagination
          page={current}
          pageCount={pageCount}
          pageSize={pageSize}
          total={sorted.length}
          onPageChange={setPage}
          pageSizeOptions={pageSizeOptions}
          onPageSizeChange={(n) => {
            setSize(n);
            setPage(1);
          }}
        />
      )}
    </div>
  );
}

export interface PaginationProps {
  page: number;
  pageCount: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number) => void;
  pageSizeOptions?: number[];
  onPageSizeChange?: (size: number) => void;
}

function pageWindow(page: number, count: number): (number | '…')[] {
  if (count <= 7) return Array.from({ length: count }, (_, i) => i + 1);
  const pages = new Set([1, count, page - 1, page, page + 1]);
  const sorted = [...pages].filter((p) => p >= 1 && p <= count).sort((a, b) => a - b);
  const out: (number | '…')[] = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - sorted[i - 1] > 1) out.push('…');
    out.push(p);
  });
  return out;
}

export function Pagination({ page, pageCount, pageSize, total, onPageChange, pageSizeOptions, onPageSizeChange }: PaginationProps) {
  const sizeId = useId();
  const start = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, total);
  // Base sin color/fondo: el estado activo e inactivo son excluyentes (evita conflictos de clases Tailwind).
  const btnBase =
    'flex h-8 min-w-8 items-center justify-center rounded-md border px-2 text-[13px] focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600';
  const btn = cn(
    btnBase,
    'border-line bg-white text-ink-3 hover:bg-surface-hover disabled:cursor-not-allowed disabled:bg-surface-subtle disabled:text-[#9AA6B4]',
  );
  const btnActive = cn(btnBase, 'border-primary-600 bg-primary-600 font-semibold text-white hover:bg-primary-700');

  return (
    <div className="flex flex-col gap-3 border-t border-line px-3 py-3 text-[13px] text-ink-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="tabular" aria-live="polite">
          Mostrando {formatNumber(start)}–{formatNumber(end)} de {formatNumber(total)}
          {!pageSizeOptions && ` · ${pageSize} por página`}
        </span>
        {pageSizeOptions && onPageSizeChange && (
          <span className="flex items-center gap-2">
            <label htmlFor={sizeId}>Filas por página</label>
            <select
              id={sizeId}
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              className="h-8 rounded-md border border-line bg-white px-2 text-[13px] text-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
            >
              {pageSizeOptions.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
          </span>
        )}
      </div>
      <nav aria-label="Paginación" className="flex items-center gap-1">
        <button type="button" className={btn} disabled={page <= 1} onClick={() => onPageChange(page - 1)} aria-label="Página anterior">
          <ChevronLeft size={16} aria-hidden="true" />
        </button>
        {pageWindow(page, pageCount).map((p, i) =>
          p === '…' ? (
            <span key={`gap-${i}`} className="px-1 text-ink-muted" aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              aria-current={p === page ? 'page' : undefined}
              onClick={() => onPageChange(p)}
              className={p === page ? btnActive : btn}
            >
              {p}
            </button>
          ),
        )}
        <button
          type="button"
          className={btn}
          disabled={page >= pageCount}
          onClick={() => onPageChange(page + 1)}
          aria-label="Página siguiente"
        >
          <ChevronRight size={16} aria-hidden="true" />
        </button>
      </nav>
    </div>
  );
}
