import { useCallback, useState } from 'react';
import { errorMessage, isServiceError } from '../services';

export interface SubmitState {
  submitting: boolean;
  error: string | null;
  fieldError: { field: string; message: string } | null;
  run: <R>(fn: () => Promise<R>) => Promise<R | undefined>;
  reset: () => void;
}

/** Estado de envío de formularios y acciones que escriben en el servicio. */
export function useSubmit(): SubmitState {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldError, setFieldError] = useState<SubmitState['fieldError']>(null);

  const run = useCallback(async <R>(fn: () => Promise<R>) => {
    setSubmitting(true);
    setError(null);
    setFieldError(null);
    try {
      return await fn();
    } catch (e) {
      const field = isServiceError(e) && typeof e.details.field === 'string' ? e.details.field : null;
      if (field) setFieldError({ field, message: errorMessage(e) });
      else setError(errorMessage(e));
      return undefined;
    } finally {
      setSubmitting(false);
    }
  }, []);

  const reset = useCallback(() => {
    setError(null);
    setFieldError(null);
  }, []);

  return { submitting, error, fieldError, run, reset };
}
