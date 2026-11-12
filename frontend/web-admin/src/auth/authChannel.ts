/**
 * Canal entre pestañas (`BroadcastChannel('sciph-auth')`) para EVENTOS de sesión. Nunca transporta credenciales: ni
 * access token, ni refresh token, ni datos del usuario. Cada mensaje se reconstruye con una lista blanca de campos al
 * enviarlo y se valida al recibirlo; lo desconocido se descarta.
 *
 * BroadcastChannel no entrega un mensaje a quien lo envía, y quien recibe nunca reenvía: no hay bucles entre pestañas.
 */
export const AUTH_CHANNEL_NAME = 'sciph-auth';

/** Motivos de fin de sesión que se comunican (el cierre voluntario es `logged-out`). */
export const SESSION_END_REASONS = ['expired', 'password-changed', 'mfa-changed'] as const;
export type SessionEndReason = (typeof SESSION_END_REASONS)[number];

export type AuthChannelEvent =
  /** Otra pestaña renovó la sesión o inició una: la cookie del navegador ya es la nueva. */
  | { type: 'session-refreshed' }
  /** Otra pestaña cerró la sesión (o revocó todas las sesiones). */
  | { type: 'logged-out' }
  /** La sesión terminó por otro motivo (caducó, cambio de contraseña, cambio de MFA). */
  | { type: 'session-ended'; reason: SessionEndReason };

export type AuthChannelListener = (event: AuthChannelEvent) => void;

export interface AuthChannel {
  post(event: AuthChannelEvent): void;
  subscribe(listener: AuthChannelListener): () => void;
  close(): void;
}

/** Solo los campos permitidos: aunque el llamante pase un objeto con más datos, no salen de la pestaña. */
function toWire(event: AuthChannelEvent): AuthChannelEvent {
  return event.type === 'session-ended' ? { type: event.type, reason: event.reason } : { type: event.type };
}

export function parseAuthChannelEvent(data: unknown): AuthChannelEvent | null {
  if (typeof data !== 'object' || data === null) return null;
  const { type, reason } = data as { type?: unknown; reason?: unknown };
  if (type === 'session-refreshed' || type === 'logged-out') return { type };
  if (type === 'session-ended' && SESSION_END_REASONS.includes(reason as SessionEndReason)) {
    return { type, reason: reason as SessionEndReason };
  }
  return null;
}

const NOOP_CHANNEL: AuthChannel = { post: () => undefined, subscribe: () => () => undefined, close: () => undefined };

/** Sin BroadcastChannel (navegador antiguo, algunos entornos de test) cada pestaña sigue funcionando sola. */
export function openAuthChannel(): AuthChannel {
  if (typeof BroadcastChannel === 'undefined') return NOOP_CHANNEL;
  let channel: BroadcastChannel;
  try {
    channel = new BroadcastChannel(AUTH_CHANNEL_NAME);
  } catch {
    return NOOP_CHANNEL;
  }

  const listeners = new Set<AuthChannelListener>();
  channel.onmessage = (message: MessageEvent) => {
    const event = parseAuthChannelEvent(message.data);
    if (event) for (const listener of [...listeners]) listener(event);
  };

  return {
    post: (event) => {
      try {
        channel.postMessage(toWire(event));
      } catch {
        // Canal cerrado: el evento solo era una optimización; cada pestaña acabará viendo el 401 por su cuenta.
      }
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    close: () => {
      listeners.clear();
      channel.close();
    },
  };
}

/** Resuelve con el próximo `session-refreshed` de otra pestaña o, como mucho, tras `timeoutMs`. */
export function waitForPeerRefresh(channel: AuthChannel | null, timeoutMs: number): Promise<void> {
  return new Promise((resolve) => {
    let unsubscribe: () => void = () => undefined;
    const timer = setTimeout(done, timeoutMs);
    if (channel) unsubscribe = channel.subscribe((event) => event.type === 'session-refreshed' && done());
    function done() {
      clearTimeout(timer);
      unsubscribe();
      resolve();
    }
  });
}
