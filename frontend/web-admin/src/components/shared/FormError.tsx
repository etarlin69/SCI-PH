import { AlertCircle } from 'lucide-react';

export interface FormErrorProps {
  message: string | null | undefined;
}

/** Banner de error de envío dentro de un formulario o modal (anunciado con role="alert"). */
export function FormError({ message }: FormErrorProps) {
  if (!message) return null;
  return (
    <div
      role="alert"
      className="flex items-start gap-2.5 rounded-lg border border-danger-600/30 bg-danger-50 px-3.5 py-3 text-[13px] text-danger-text"
    >
      <AlertCircle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
      <span>{message}</span>
    </div>
  );
}
