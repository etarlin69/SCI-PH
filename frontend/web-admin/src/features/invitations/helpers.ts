import { INVITATION_TTL_DAYS } from '../../services';
import type { Invitation, InvitationFilters, InvitationStatus } from '../../types';

export const INVITATION_STATUSES: InvitationStatus[] = ['Pendiente', 'Aceptada', 'Expirada', 'Revocada'];
export const EMPTY_INVITATION_FILTERS: InvitationFilters = { q: '', tipo: '', estado: '' };
export const MESSAGE_MAX = 500;
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

/** `YYYY-MM-DD HH:mm` → Date local. */
export function parseDateTime(value: string): Date {
  const [date, time = '00:00'] = value.split(' ');
  const [y, m, d] = date.split('-').map(Number);
  const [h, min] = time.split(':').map(Number);
  return new Date(y, m - 1, d, h, min);
}

const pad = (n: number) => String(n).padStart(2, '0');
export const formatDateTime = (d: Date) =>
  `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;

/** Caducidad: `enviadaEl` + 7 días (cada reenvío la reinicia). */
export function expiryOf(inv: Invitation): Date {
  const d = parseDateTime(inv.enviadaEl);
  d.setDate(d.getDate() + INVITATION_TTL_DAYS);
  return d;
}

/** Texto de caducidad para la tabla y el detalle. */
export function expiryText(inv: Invitation, now = new Date()): string {
  if (inv.estado === 'Aceptada') return inv.aceptadaEl ? `Aceptada ${formatDateTime(parseDateTime(inv.aceptadaEl))}` : 'Aceptada';
  if (inv.estado === 'Revocada') return 'El enlace dejó de funcionar';
  const exp = expiryOf(inv);
  const ms = exp.getTime() - now.getTime();
  if (inv.estado === 'Expirada' || ms <= 0) return `Caducó el ${formatDateTime(exp).slice(0, 10)}`;
  const days = Math.floor(ms / 86_400_000);
  if (days >= 1) return `Caduca en ${days} ${days === 1 ? 'día' : 'días'}`;
  return `Caduca en ${Math.max(1, Math.round(ms / 3_600_000))} h`;
}

export function filterInvitations(list: Invitation[], f: InvitationFilters): Invitation[] {
  const q = f.q.trim().toLowerCase();
  return list.filter(
    (i) => (!q || `${i.email} ${i.id}`.toLowerCase().includes(q)) && (!f.tipo || i.tipo === f.tipo) && (!f.estado || i.estado === f.estado),
  );
}

export const canResend = (i: Invitation) => i.estado !== 'Aceptada';
export const canRevoke = (i: Invitation) => i.estado === 'Pendiente';
