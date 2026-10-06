import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { CheckCircle2, Clock } from 'lucide-react';

export interface LinkStatusProps {
  tone: 'success' | 'invalid';
  title: string;
  children: ReactNode;
  action?: { to: string; label: string };
}

/** Resultado de un enlace recibido por correo (activación, restablecimiento, confirmación). */
export function LinkStatus({ tone, title, children, action }: LinkStatusProps) {
  return (
    <div className="flex flex-col gap-4" role="status">
      <span
        className={
          tone === 'success'
            ? 'flex h-11 w-11 items-center justify-center rounded-full bg-success-50 text-success-600'
            : 'flex h-11 w-11 items-center justify-center rounded-full bg-warning-50 text-warning-700'
        }
      >
        {tone === 'success' ? <CheckCircle2 size={20} aria-hidden="true" /> : <Clock size={20} aria-hidden="true" />}
      </span>
      <h1 className="text-2xl font-semibold text-ink">{title}</h1>
      <div className="text-sm text-ink-3">{children}</div>
      {action && (
        <Link
          to={action.to}
          className="inline-flex h-11 items-center justify-center rounded-lg bg-primary-600 px-4 text-sm font-semibold text-white hover:bg-primary-700"
        >
          {action.label}
        </Link>
      )}
    </div>
  );
}

/** Error 400 de un enlace: ValidationProblem (política de contraseña) o enlace inválido/caducado. */
export function isPasswordPolicyError(fieldErrors: Record<string, string[]>): boolean {
  return Object.keys(fieldErrors).some((k) => k === 'password' || k === 'newPassword');
}
