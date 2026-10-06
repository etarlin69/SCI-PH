import { useState, type FormEvent } from 'react';
import { Plus, Tablet } from 'lucide-react';
import { Badge, Button, Checkbox, ConfirmDialog, EmptyState, ErrorState, FormError, Input, Modal, PageHeader, Table, useToast, type Column } from '../../../components/shared';
import { useAsync } from '../../../hooks/useAsync';
import { formatUtcDateTime } from '../../../utils/dates';
import { OneTimeSecret } from '../components/OneTimeSecret';
import { describeDeviceError, devicesService } from '../services/devicesService';
import {
  DEVICE_CODE_PATTERN,
  DEVICE_SCOPE_LABELS,
  DeviceCredentialStatus,
  DeviceScopes,
  type DeviceScope,
  type DeviceSecret,
  type DeviceSummary,
} from '../types/devices.types';

const scopeLabel = (s: string) => DEVICE_SCOPE_LABELS[s as DeviceScope] ?? s;

/**
 * Dispositivos (GET/POST /api/web/admin/devices). La clave en claro se recibe solo al crear o rotar y se muestra
 * una única vez en un diálogo; no se guarda en ningún almacenamiento ni vuelve a mostrarse.
 */
export function DevicesPage() {
  const toast = useToast();
  const q = useAsync(() => devicesService.list());
  const [createOpen, setCreateOpen] = useState(false);
  const [secret, setSecret] = useState<{ value: DeviceSecret; title: string } | null>(null);
  const [confirm, setConfirm] = useState<{ kind: 'rotate' | 'revoke'; device: DeviceSummary } | null>(null);

  const runConfirmed = async () => {
    if (!confirm) return;
    const { kind, device } = confirm;
    try {
      if (kind === 'rotate') {
        const result = await devicesService.rotateSecret(device.id);
        setSecret({ value: result, title: 'Clave rotada' });
      } else {
        await devicesService.revoke(device.id);
        toast({ title: 'Dispositivo revocado', description: device.deviceCode, tone: 'success' });
      }
      q.reload();
    } catch (e) {
      toast({ title: kind === 'rotate' ? 'No se pudo rotar la clave' : 'No se pudo revocar', description: describeDeviceError(e), tone: 'error' });
    } finally {
      setConfirm(null);
    }
  };

  const columns: Column<DeviceSummary>[] = [
    {
      id: 'device',
      header: 'Dispositivo',
      cell: (d) => (
        <span className="flex flex-col">
          <span className="font-semibold text-ink">{d.name}</span>
          <span className="font-mono text-xs text-ink-muted">{d.deviceCode}</span>
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Estado',
      cell: (d) =>
        d.status === DeviceCredentialStatus.Active ? <Badge tone="success">Activo</Badge> : <Badge tone="danger">Revocado</Badge>,
    },
    {
      id: 'scopes',
      header: 'Permisos',
      cell: (d) => (
        <span className="flex flex-wrap gap-1">
          {d.scopes.map((s) => (
            <Badge key={s} tone="info" dot={false}>
              {scopeLabel(s)}
            </Badge>
          ))}
        </span>
      ),
    },
    { id: 'firmware', header: 'Firmware', cell: (d) => d.firmwareVersion ?? '—' },
    { id: 'seen', header: 'Último contacto', cell: (d) => <span className="tabular">{formatUtcDateTime(d.lastSeenAtUtc)}</span> },
    {
      id: 'actions',
      header: <span className="sr-only">Acciones</span>,
      align: 'right',
      cell: (d) =>
        d.status === DeviceCredentialStatus.Active ? (
          <span className="flex justify-end gap-1.5">
            <Button size="sm" variant="secondary" onClick={() => setConfirm({ kind: 'rotate', device: d })}>
              Rotar clave
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setConfirm({ kind: 'revoke', device: d })}>
              Revocar
            </Button>
          </span>
        ) : (
          <span className="text-xs text-ink-muted">Revocado {formatUtcDateTime(d.revokedAtUtc)}</span>
        ),
    },
  ];

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Dispositivos"
        description="Credenciales de los dispositivos (ESP32) que llaman a la Device API."
        actions={
          <Button leftIcon={<Plus size={16} aria-hidden="true" />} onClick={() => setCreateOpen(true)}>
            Registrar dispositivo
          </Button>
        }
      />

      {q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : (
        <Table
          caption="Dispositivos registrados"
          columns={columns}
          rows={q.data ?? []}
          getRowId={(d) => d.id}
          loading={q.loading}
          pageSize={25}
          emptyState={<EmptyState icon={Tablet} title="No hay dispositivos registrados" />}
          stickyFirstColumn
        />
      )}

      <CreateDeviceModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(result) => {
          setCreateOpen(false);
          setSecret({ value: result, title: 'Dispositivo registrado' });
          q.reload();
        }}
      />

      {/* La clave solo vive en este estado mientras el diálogo está abierto. */}
      <Modal
        open={secret !== null}
        onClose={() => setSecret(null)}
        closeOnBackdrop={false}
        title={secret?.title ?? ''}
        description={secret ? `Configura esta clave en ${secret.value.deviceCode}.` : undefined}
        footer={<Button onClick={() => setSecret(null)}>He guardado la clave</Button>}
      >
        {secret && <OneTimeSecret secret={secret.value.secret} />}
      </Modal>

      {confirm && (
        <ConfirmDialog
          open
          title={confirm.kind === 'rotate' ? 'Rotar la clave' : 'Revocar el dispositivo'}
          message={
            confirm.kind === 'rotate'
              ? `La clave actual de ${confirm.device.deviceCode} dejará de funcionar y se mostrará una nueva una sola vez.`
              : `${confirm.device.deviceCode} no podrá volver a autenticarse. Esta acción no se puede deshacer.`
          }
          confirmLabel={confirm.kind === 'rotate' ? 'Rotar clave' : 'Revocar'}
          onConfirm={runConfirmed}
          onCancel={() => setConfirm(null)}
        />
      )}
    </div>
  );
}

function CreateDeviceModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (s: DeviceSecret) => void }) {
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<Set<DeviceScope>>(new Set());
  const [touched, setTouched] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reset = () => {
    setCode('');
    setName('');
    setScopes(new Set());
    setTouched(false);
    setError(null);
  };
  const close = () => {
    if (saving) return;
    reset();
    onClose();
  };

  const codeError = touched && !DEVICE_CODE_PATTERN.test(code.trim()) ? '3–60 caracteres: letras, números y guiones; empieza por letra o número.' : undefined;
  const nameError = touched && !name.trim() ? 'Escribe un nombre.' : undefined;
  const scopesError = touched && scopes.size === 0 ? 'Elige al menos un permiso.' : null;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setTouched(true);
    if (saving || !DEVICE_CODE_PATTERN.test(code.trim()) || !name.trim() || scopes.size === 0) return;
    setSaving(true);
    setError(null);
    try {
      const result = await devicesService.provision({ deviceCode: code.trim(), name: name.trim(), scopes: [...scopes] });
      reset();
      onCreated(result);
    } catch (err) {
      setError(describeDeviceError(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={close}
      dismissible={!saving}
      title="Registrar dispositivo"
      footer={
        <>
          <Button variant="secondary" onClick={close} disabled={saving}>
            Cancelar
          </Button>
          <Button type="submit" form="create-device" loading={saving} loadingText="Registrando…">
            Registrar
          </Button>
        </>
      }
    >
      <form id="create-device" noValidate onSubmit={submit} className="flex flex-col gap-3.5">
        <Input label="Código del dispositivo" value={code} maxLength={60} onChange={(e) => setCode(e.target.value)} error={codeError} className="font-mono" required />
        <Input label="Nombre" value={name} maxLength={120} onChange={(e) => setName(e.target.value)} error={nameError} required />
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-[13px] font-semibold text-ink-2">Permisos del dispositivo</legend>
          {Object.values(DeviceScopes).map((s) => (
            <Checkbox
              key={s}
              label={DEVICE_SCOPE_LABELS[s]}
              description={s}
              checked={scopes.has(s)}
              onChange={() =>
                setScopes((prev) => {
                  const next = new Set(prev);
                  if (next.has(s)) next.delete(s);
                  else next.add(s);
                  return next;
                })
              }
            />
          ))}
        </fieldset>
        <FormError message={scopesError ?? error} />
      </form>
    </Modal>
  );
}

export default DevicesPage;
