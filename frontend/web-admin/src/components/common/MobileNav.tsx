import { useEffect, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';

export interface MobileNavProps {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
}

/** Menú desplegable lateral para móvil (< 640 px). */
export function MobileNav({ open, onClose, children }: MobileNavProps) {
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const prev = document.activeElement as HTMLElement | null;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    panelRef.current?.querySelector<HTMLElement>('a, button')?.focus();
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = overflow;
      document.removeEventListener('keydown', onKey);
      prev?.focus?.();
    };
  }, [open, onClose]);

  if (!open) return null;
  return createPortal(
    <div className="fixed inset-0 z-modal sm:hidden">
      <div aria-hidden="true" className="absolute inset-0 animate-fade-in bg-nav/50" onClick={onClose} />
      <div
        id="mobile-nav"
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label="Menú"
        className="relative h-full w-fit animate-slide-in-left"
      >
        {children}
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar menú"
          className="absolute -right-12 top-3 flex h-10 w-10 items-center justify-center rounded-full bg-white text-ink shadow"
        >
          <X size={18} aria-hidden="true" />
        </button>
      </div>
    </div>,
    document.body,
  );
}
