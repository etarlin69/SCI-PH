import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, Check, Info, X } from 'lucide-react';
import { cn } from '../../utils/cn';

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

export interface ToastOptions {
    title: string;
    description?: string;
    tone?: ToastTone;
    durationMs?: number;
}

interface ToastItem extends ToastOptions {
    id: number;
}

type PushToast = (options: ToastOptions) => void;

const ToastContext = createContext<PushToast | null>(null);

const ICON_BG: Record<ToastTone, string> = {
    success: 'bg-[#2E9E63] text-white',
    error: 'bg-[#D9412F] text-white',
    warning: 'bg-warning-500 text-ink',
    info: 'bg-primary-300 text-ink',
};

function ToastIcon({ tone }: { tone: ToastTone }) {
    if (tone === 'success') return <Check size={13} aria-hidden="true" />;
    if (tone === 'error') return <X size={13} aria-hidden="true" />;
    if (tone === 'warning') return <AlertTriangle size={13} aria-hidden="true" />;
    return <Info size={13} aria-hidden="true" />;
}

export function ToastProvider({ children }: { children: ReactNode }) {
    const [items, setItems] = useState<ToastItem[]>([]);
    const nextId = useRef(0);

    const dismiss = useCallback((id: number) => setItems((xs) => xs.filter((x) => x.id !== id)), []);

    const push = useCallback<PushToast>(
        (_options) => {
            const id = ++nextId.current;
            setItems((xs) => [...xs, { ..._options, id }]);
            window.setTimeout(() => dismiss(id), _options.durationMs ?? 4000);
        },
        [dismiss],
    );

    return (
        <ToastContext.Provider value={push}>
            {children}
            {createPortal(
                <div className="pointer-events-none fixed bottom-8 right-8 z-toast flex w-[380px] max-w-[calc(100vw-2rem)] flex-col gap-3">
                    {items.map((t) => {
                        const tone = t.tone ?? 'success';
                        return (
                            <div
                                key={t.id}
                                role={tone === 'error' ? 'alert' : 'status'}
                                aria-live={tone === 'error' ? 'assertive' : 'polite'}
                                className="pointer-events-auto flex animate-toast-in items-start gap-3 rounded-[10px] bg-ink px-4 py-3.5 text-white shadow-[0_12px_32px_rgba(13,31,51,0.3)]"
                            >
                                <span className={cn('flex h-6 w-6 shrink-0 items-center justify-center rounded-full', ICON_BG[tone])}>
                                    <ToastIcon tone={tone} />
                                </span>
                                <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                                    <span className="text-sm font-semibold">{t.title}</span>
                                    {t.description && <span className="text-[13px] text-nav-text">{t.description}</span>}
                                </div>
                                <button
                                    type="button"
                                    onClick={() => dismiss(t.id)}
                                    aria-label="Cerrar notificación"
                                    className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-nav-text hover:bg-white/10"
                                >
                                    <X size={16} aria-hidden="true" />
                                </button>
                            </div>
                        );
                    })}
                </div>,
                document.body,
            )}
        </ToastContext.Provider>
    );
}

export function useToast(): PushToast {
    const ctx = useContext(ToastContext);
    if (!ctx) throw new Error('useToast debe usarse dentro de <ToastProvider>.');
    return ctx;
}
