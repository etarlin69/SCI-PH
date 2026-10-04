import { useState, type ReactNode } from 'react';
import { SlidersHorizontal } from 'lucide-react';
import { useIsMobile } from '../../hooks/useMediaQuery';
import { Button } from './Button';
import { Modal } from './Modal';

export interface ResponsiveFiltersProps {
  children: ReactNode;
  activeCount: number;
  onClear: () => void;
  resultCount?: number;
}

/** Escritorio/tablet: los filtros se muestran en línea. */
export function ResponsiveFilters({ children, activeCount, onClear, resultCount }: ResponsiveFiltersProps) {
  const isMobile = useIsMobile();
  const [open, setOpen] = useState(false);

  if (!isMobile) return <div className="flex flex-wrap items-end gap-3">{children}</div>;

  return (
    <>
      <Button
        variant="secondary"
        leftIcon={<SlidersHorizontal size={16} aria-hidden="true" />}
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
      >
        Filtros{activeCount > 0 ? ` (${activeCount})` : ''}
      </Button>
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Filtros"
        placement="bottom"
        footer={
          <>
            <Button variant="ghost" onClick={onClear} disabled={activeCount === 0}>
              Limpiar
            </Button>
            <Button onClick={() => setOpen(false)}>{resultCount === undefined ? 'Aplicar' : `Ver ${resultCount} resultados`}</Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">{children}</div>
      </Modal>
    </>
  );
}
