import { IncomingTransfersPanel } from '../components/IncomingTransfersPanel';

/** Traslados entrantes al hospital (lado receptor). */
export function TransfersPage() {
    return (
        <div className="flex flex-col gap-5">
            <div>
                <h1 className="m-0 text-2xl font-bold text-ink">Transferencias</h1>
                <p className="m-0 mt-1 text-sm text-ink-muted">Traslados en camino a este hospital y registro de su llegada.</p>
            </div>
            <IncomingTransfersPanel />
        </div>
    );
}
