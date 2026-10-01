import { useState, type ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';
import { cn } from '../../utils/cn';
import { Button } from './Button';
import { Modal } from './Modal';

export interface ConfirmDialogProps {
    open: boolean;
    title: string;
    message: ReactNode;
    confirmLabel?: string;
    cancelLabel?: string;
    tone?: 'danger' | 'primary';
    onConfirm: () => void | Promise<void>;
    onCancel: () => void;
}

export function ConfirmDialog({
    open,
    title,
    message,
    confirmLabel = 'Confirmar',
    cancelLabel = 'Cancelar',
    tone = 'danger',
    onConfirm,
    onCancel,
}: ConfirmDialogProps) {
    const [busy, setBusy] = useState(false);

    const confirm = async () => {
        setBusy(true);
        try {
            await onConfirm();
        } finally {
            setBusy(false);
        }
    };

    return (
        <Modal
            open={open}
            onClose={onCancel}
            role="alertdialog"
            size="sm"
            dismissible={!busy}
            closeOnBackdrop={false}
            title={title}
            footer={
                <>
                    <Button variant="secondary" onClick={onCancel} disabled={busy}>
                        {cancelLabel}
                    </Button>
                    <Button variant={tone === 'danger' ? 'danger' : 'primary'} onClick={() => void confirm()} loading={busy}>
                        {confirmLabel}
                    </Button>
                </>
            }
        >
            <div className="flex gap-3.5">
                <span
                    className={cn(
                        'flex h-10 w-10 shrink-0 items-center justify-center rounded-full',
                        tone === 'danger' ? 'bg-danger-50 text-danger-600' : 'bg-primary-100 text-primary-600',
                    )}
                >
                    <AlertTriangle size={20} aria-hidden="true" />
                </span>
                <div className="text-sm leading-relaxed text-ink-3">{message}</div>
            </div>
        </Modal>
    );
}
