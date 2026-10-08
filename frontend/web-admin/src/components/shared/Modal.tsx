import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';
import { cn } from '../../utils/cn';

export interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg';
  placement?: 'center' | 'right' | 'bottom';
  drawerWidth?: 'sm' | 'md' | 'lg';
  role?: 'dialog' | 'alertdialog';
  closeOnBackdrop?: boolean;
  dismissible?: boolean;
  initialFocusRef?: RefObject<HTMLElement>;
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const SIZES = { sm: 'sm:w-[440px]', md: 'sm:w-[560px]', lg: 'sm:w-[720px]' };
const DRAWER_WIDTHS = { sm: 'w-[380px]', md: 'w-[480px]', lg: 'w-[560px]' };

/** Modal accesible renderizado en un portal sobre `document.body`. */
export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  size = 'md',
  placement = 'center',
  drawerWidth = 'sm',
  role = 'dialog',
  closeOnBackdrop = true,
  dismissible = true,
  initialFocusRef,
}: ModalProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const id = useId();
  const titleId = `${id}-title`;
  const descriptionId = `${id}-desc`;

  // Bloquea el scroll del fondo, mueve el foco dentro y lo devuelve al cerrar.
  useEffect(() => {
    if (!open) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const timer = window.setTimeout(() => {
      const target = initialFocusRef?.current ?? panelRef.current?.querySelector<HTMLElement>(FOCUSABLE) ?? panelRef.current;
      target?.focus();
    }, 0);
    return () => {
      window.clearTimeout(timer);
      document.body.style.overflow = previousOverflow;
      previouslyFocused?.focus?.();
    };
  }, [open, initialFocusRef]);

  if (!open) return null;

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape' && dismissible) {
      e.stopPropagation();
      onClose();
      return;
    }
    if (e.key !== 'Tab' || !panelRef.current) return;
    const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE));
    if (items.length === 0) {
      e.preventDefault();
      return;
    }
    const first = items[0];
    const last = items[items.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };

  return createPortal(
    <div className="fixed inset-0 z-modal">
      <div
        aria-hidden="true"
        className="absolute inset-0 z-backdrop animate-fade-in bg-nav/50"
        onClick={closeOnBackdrop && dismissible ? onClose : undefined}
      />
      <div
        className={cn(
          'pointer-events-none absolute inset-0 z-modal flex',
          placement === 'center' && 'items-end justify-center sm:items-center sm:p-4',
          placement === 'right' && 'justify-end',
          placement === 'bottom' && 'items-end',
        )}
      >
        <div
          ref={panelRef}
          role={role}
          aria-modal="true"
          aria-labelledby={titleId}
          aria-describedby={description ? descriptionId : undefined}
          tabIndex={-1}
          onKeyDown={handleKeyDown}
          className={cn(
            'pointer-events-auto flex max-h-full flex-col bg-white focus:outline-none',
            placement === 'center' &&
              cn(
                'max-h-[92vh] w-full max-w-full animate-slide-in-up rounded-t-2xl shadow-[0_-12px_32px_rgba(13,31,51,0.18)]',
                'sm:max-h-full sm:animate-none sm:rounded-xl sm:shadow-[0_24px_48px_rgba(13,31,51,0.25)]',
                SIZES[size],
              ),
            placement === 'right' &&
              cn('h-full max-w-full animate-slide-in-right shadow-[-12px_0_32px_rgba(13,31,51,0.18)]', DRAWER_WIDTHS[drawerWidth]),
            placement === 'bottom' && 'max-h-[88vh] w-full animate-slide-in-up rounded-t-2xl shadow-[0_-12px_32px_rgba(13,31,51,0.18)]',
          )}
        >
          {placement !== 'right' && (
            <span
              aria-hidden="true"
              className={cn('mx-auto mt-2 h-1 w-10 shrink-0 rounded-full bg-line-strong', placement === 'center' && 'sm:hidden')}
            />
          )}
          <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4 sm:px-6 sm:py-5">
            <div className="flex min-w-0 flex-col gap-1">
              <h2 id={titleId} className="text-lg font-semibold text-ink">
                {title}
              </h2>
              {description && (
                <div id={descriptionId} className="text-[13px] text-ink-muted">
                  {description}
                </div>
              )}
            </div>
            {dismissible && (
              <button
                type="button"
                onClick={onClose}
                aria-label="Cerrar"
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-ink-3 hover:bg-surface-hover focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary-600"
              >
                <X size={18} aria-hidden="true" />
              </button>
            )}
          </div>
          {children !== undefined && children !== null && (
            <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 sm:px-6">{children}</div>
          )}
          {footer && <div className="flex flex-wrap items-center justify-end gap-2.5 border-t border-line px-5 py-4 sm:px-6">{footer}</div>}
        </div>
      </div>
    </div>,
    document.body,
  );
}
