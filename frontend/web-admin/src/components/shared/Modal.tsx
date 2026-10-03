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
    placement?: 'center' | 'right';
    role?: 'dialog' | 'alertdialog';
    closeOnBackdrop?: boolean;
    dismissible?: boolean;
    initialFocusRef?: RefObject<HTMLElement>;
}

const FOCUSABLE =
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const SIZES = { sm: 'w-[440px]', md: 'w-[560px]', lg: 'w-[720px]' };

export function Modal({
    open,
    onClose,
    title,
    description,
    children,
    footer,
    size = 'md',
    placement = 'center',
    role = 'dialog',
    closeOnBackdrop = true,
    dismissible = true,
    initialFocusRef,
}: ModalProps) {
    const panelRef = useRef<HTMLDivElement>(null);
    const id = useId();
    const titleId = `${id}-title`;
    const descriptionId = `${id}-desc`;
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
                    placement === 'center' ? 'items-center justify-center p-4' : 'justify-end',
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
                        placement === 'center'
                            ? cn('max-w-full rounded-xl shadow-[0_24px_48px_rgba(13,31,51,0.25)]', SIZES[size])
                            : 'h-full w-[380px] max-w-full animate-slide-in-right shadow-[-12px_0_32px_rgba(13,31,51,0.18)]',
                    )}
                >
                    <div className="flex items-start justify-between gap-4 border-b border-line px-6 py-5">
                        <div className="flex min-w-0 flex-col gap-1">
                            <h2 id={titleId} className="text-lg font-semibold text-ink">
                                {title}
                            </h2>
                            {description && (
                                <p id={descriptionId} className="text-[13px] text-ink-muted">
                                    {description}
                                </p>
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
                    <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">{children}</div>
                    {footer && <div className="flex items-center justify-end gap-2.5 border-t border-line px-6 py-4">{footer}</div>}
                </div>
            </div>
        </div>,
        document.body,
    );
}
