import { useState } from 'react';
import type { CatalogItemMap, CatalogKind } from '../../../types';
import { Button, Modal } from '../../../components/shared';
import { CATALOG_OFF_STATUS } from '../../../services';
import { fieldValue } from '../helpers';
import type { CatalogConfig } from '../catalogConfig';

export interface DeleteCatalogItemDialogProps<K extends CatalogKind> {
  config: CatalogConfig<K>;
  item: CatalogItemMap[K] | null;
  onConfirm: (mode: 'delete' | 'deactivate', item: CatalogItemMap[K]) => Promise<void>;
  onCancel: () => void;
}

/** Eliminar un elemento sin uso, o desactivarlo si está referenciado en episodios/asignaciones. */
export function DeleteCatalogItemDialog<K extends CatalogKind>({ config, item, onConfirm, onCancel }: DeleteCatalogItemDialogProps<K>) {
  const [busy, setBusy] = useState(false);
  if (!item) return null;
  const name = config.itemName(item);
  const off = CATALOG_OFF_STATUS[config.kind] as string;
  const inUse = item.uso > 0;
  const alreadyOff = fieldValue(item, 'estado') === off;

  const run = async (mode: 'delete' | 'deactivate') => {
    setBusy(true);
    try {
      await onConfirm(mode, item);
    } finally {
      setBusy(false);
    }
  };

  const title = inUse ? `No se puede eliminar «${name}»` : `¿Eliminar «${name}»?`;
  const body = !inUse
    ? 'No está en uso. Se eliminará del catálogo; esta acción no se puede deshacer y queda registrada en auditoría.'
    : alreadyOff
      ? `Está referenciado en ${item.uso}${config.usageUnit}. Ya está en «${off}» y no aparece en formularios nuevos.`
      : `Está referenciado en ${item.uso}${config.usageUnit}. Para conservar la trazabilidad, cámbialo a «${off}» para que deje de aparecer en formularios nuevos.`;

  return (
    <Modal
      open
      onClose={onCancel}
      dismissible={!busy}
      role="alertdialog"
      size="sm"
      title={title}
      description={body}
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={busy}>
            {inUse && alreadyOff ? 'Cerrar' : 'Cancelar'}
          </Button>
          {!inUse && (
            <Button variant="danger" onClick={() => run('delete')} loading={busy} loadingText="Eliminando…">
              Eliminar
            </Button>
          )}
          {inUse && !alreadyOff && (
            <Button onClick={() => run('deactivate')} loading={busy} loadingText="Guardando…">
              {off === 'Mantenimiento' ? 'Pasar a mantenimiento' : 'Desactivar'}
            </Button>
          )}
        </>
      }
    />
  );
}
