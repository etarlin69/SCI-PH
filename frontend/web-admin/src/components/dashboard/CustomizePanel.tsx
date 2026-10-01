import { useEffect, useState, type DragEvent } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowDown, ArrowUp, GripVertical } from 'lucide-react';
import type { SavedView, WidgetConfig } from '../../types';
import { cn } from '../../utils/cn';
import { Button, Checkbox, Input, Modal } from '../shared';

export interface CustomizePanelProps {
    open: boolean;
    onClose: () => void;
    widgets: WidgetConfig[];
    viewName: string;
    onSave: (_: SavedView) => void;
}

const schema = z.object({
    name: z
        .string()
        .trim()
        .min(3, 'Escribe al menos 3 caracteres.')
        .max(40, 'Máximo 40 caracteres.')
        .refine((v) => v.toLowerCase() !== 'predeterminada', 'Ese nombre está reservado para la vista por defecto.'),
});

type FormValues = z.infer<typeof schema>;

function move<T>(list: T[], from: number, to: number): T[] {
    const next = [...list];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    return next;
}

export function CustomizePanel({ open, onClose, widgets, viewName, onSave }: CustomizePanelProps) {
    const [draft, setDraft] = useState<WidgetConfig[]>(widgets);
    const [dragIndex, setDragIndex] = useState<number | null>(null);
    const {
        register,
        handleSubmit,
        reset,
        formState: { errors: _errors },
    } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { name: '' } });

    useEffect(() => {
        if (!open) return;
        setDraft(widgets);
        reset({ name: viewName === 'Predeterminada' ? 'Mi Dashboard Personal' : viewName });
    }, [open, widgets, viewName, reset]);

    const noneVisible = draft.every((w) => !w.visible);

    const onDrop = (e: DragEvent<HTMLLIElement>, index: number) => {
        e.preventDefault();
        if (dragIndex !== null && dragIndex !== index) setDraft((d) => move(d, dragIndex, index));
        setDragIndex(null);
    };

    const submit = handleSubmit(({ name }) => {
        if (noneVisible) return;
        onSave({ name, widgets: draft });
        onClose();
    });

    return (
        <Modal
            open={open}
            onClose={onClose}
            placement="right"
            title="Personalizar dashboard"
            description="Muestra u oculta widgets y arrástralos por el asa para cambiar su orden."
            footer={
                <>
                    <Button variant="secondary" onClick={onClose}>
                        Cancelar
                    </Button>
                    <Button type="submit" form="customize-dashboard" disabled={noneVisible}>
                        Guardar vista
                    </Button>
                </>
            }
        >
            <form id="customize-dashboard" onSubmit={submit} className="flex flex-col gap-5" noValidate>
                <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-label="Widgets del dashboard">
                    {draft.map((w, i) => (
                        <li
                            key={w.id}
                            draggable
                            onDragStart={() => setDragIndex(i)}
                            onDragOver={(e) => e.preventDefault()}
                            onDrop={(e) => onDrop(e, i)}
                            onDragEnd={() => setDragIndex(null)}
                            className={cn(
                                'flex h-12 items-center gap-2 rounded-lg border bg-white pl-2 pr-1',
                                dragIndex === i ? 'border-primary-600 opacity-60' : 'border-line',
                            )}
                        >
                            <GripVertical size={16} className="shrink-0 cursor-grab text-ink-muted" aria-hidden="true" />
                            <Checkbox
                                label={w.label}
                                checked={w.visible}
                                onChange={() => setDraft((d) => d.map((x) => (x.id === w.id ? { ...x, visible: !x.visible } : x)))}
                                containerClassName="flex-1 items-center"
                            />
                            <button
                                type="button"
                                onClick={() => setDraft((d) => move(d, i, i - 1))}
                                disabled={i === 0}
                                aria-label={`Subir ${w.label}`}
                                className="flex h-8 w-8 items-center justify-center rounded-md text-ink-3 hover:bg-surface-hover disabled:cursor-not-allowed disabled:text-[#AEB8C4]"
                            >
                                <ArrowUp size={16} aria-hidden="true" />
                            </button>
                            <button
                                type="button"
                                onClick={() => setDraft((d) => move(d, i, i + 1))}
                                disabled={i === draft.length - 1}
                                aria-label={`Bajar ${w.label}`}
                                className="flex h-8 w-8 items-center justify-center rounded-md text-ink-3 hover:bg-surface-hover disabled:cursor-not-allowed disabled:text-[#AEB8C4]"
                            >
                                <ArrowDown size={16} aria-hidden="true" />
                            </button>
                        </li>
                    ))}
                </ul>
                {noneVisible && (
                    <p role="alert" className="m-0 text-xs text-danger-text">
                        Deja al menos un widget visible.
                    </p>
                )}
                <Input
                    label="Guardar como vista"
                    placeholder="Ej.: Mi Dashboard Personal"
                    error={_errors.name?.message}
                    {...register('name')}
                />
            </form>
        </Modal>
    );
}
