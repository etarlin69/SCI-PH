import { useMemo } from 'react';
import { QrCode as QrCodeIcon } from 'lucide-react';
import { cn } from '../../utils/cn';
import { encodeQr, qrToSvgPath } from './qr';

export interface QrCodeProps {
  value: string;
  size?: number;
  label?: string;
  className?: string;
}

const MARGIN = 4;

/** Código QR real (modo byte, nivel M, versiones 1–6) generado en el navegador con `qr.ts` y pintado como SVG nítido (`crispEdges`). */
export function QrCode({ value, size = 184, label = 'Código QR', className }: QrCodeProps) {
  const qr = useMemo(() => {
    try {
      const matrix = encodeQr(value);
      return { size: matrix.size, path: qrToSvgPath(matrix, MARGIN) };
    } catch {
      return null;
    }
  }, [value]);

  if (!qr) {
    return (
      <div
        role="img"
        aria-label="No se pudo generar el código QR"
        style={{ width: size, height: size }}
        className={cn(
          'flex flex-col items-center justify-center gap-2 bg-surface-subtle p-4 text-center text-xs text-ink-muted',
          className,
        )}
      >
        <QrCodeIcon size={28} aria-hidden="true" />
        No se pudo generar el QR; usa la clave manual.
      </div>
    );
  }

  const dim = qr.size + MARGIN * 2;
  return (
    <svg
      role="img"
      aria-label={label}
      width={size}
      height={size}
      viewBox={`0 0 ${dim} ${dim}`}
      shapeRendering="crispEdges"
      className={cn('block', className)}
    >
      <rect width={dim} height={dim} fill="#FFFFFF" />
      <path d={qr.path} fill="#0F1B2A" />
    </svg>
  );
}
