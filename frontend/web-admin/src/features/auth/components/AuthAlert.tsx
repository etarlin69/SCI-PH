import type { ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, Info, Lock, XCircle } from 'lucide-react';
import { cn } from '../../../utils/cn';

export type AuthAlertTone = 'error' | 'locked' | 'warning' | 'info' | 'success';

export interface AuthAlertProps {
  tone: AuthAlertTone;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}

const STYLE: Record<AuthAlertTone, string> = {
  error: 'border-danger-600/30 bg-danger-50 text-danger-text',
  locked: 'border-danger-600/30 bg-danger-50 text-danger-text',
  warning: 'border-warning-500/40 bg-warning-50 text-warning-700',
  info: 'border-primary-300 bg-primary-50 text-primary-700',
  success: 'border-success-600/30 bg-success-50 text-success-700',
};

const ICON = { error: XCircle, locked: Lock, warning: AlertTriangle, info: Info, success: CheckCircle2 };

/** Banner de estado de las pantallas de acceso (errores, bloqueo, sesión caducada). */
export function AuthAlert({ tone, title, children, action }: AuthAlertProps) {
  const Icon = ICON[tone];
  return (
    <div
      role={tone === 'error' || tone === 'locked' ? 'alert' : 'status'}
      className={cn('flex gap-3 rounded-lg border px-3.5 py-3 text-[13px]', STYLE[tone])}
    >
      <Icon size={18} className="mt-0.5 shrink-0" aria-hidden="true" />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="font-semibold">{title}</p>
        {children && <div className="leading-relaxed">{children}</div>}
        {action && <div className="mt-1">{action}</div>}
      </div>
    </div>
  );
}
