import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from '../../utils/cn';
import { Button } from './Button';
import { Checkbox } from './Checkbox';

export interface MultiSelectOption<V extends string> {
    value: V;
    label: string;
    color?: string;
}

export interface MultiSelectProps<V extends string> {
    label: string;
    options: MultiSelectOption<V>[];
    value: V[];
    onChange: (value: V[]) => void;
    allLabel?: string;
    minSelected?: number;
    className?: string;
}
export function MultiSelect<V extends string>({
    label,
    options,
    value,
    onChange,
    allLabel = 'Todos',
    minSelected = 1,
    className,
}: MultiSelectProps<V>) {
    const [open, setOpen] = useState(false);
    const rootRef = useRef<HTMLDivElement>(null);
    const labelId = useId();

    useEffect(() => {
        if (!open) return;
        const onPointerDown = (e: MouseEvent) => {
            if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
        };
        document.addEventListener('mousedown', onPointerDown);
        return () => document.removeEventListener('mousedown', onPointerDown);
    }, [open]);

    const all = value.length === options.length;
    const summary = all
        ? `${allLabel} (${options.length})`
        : value.length === 1
          ? options.find((o) => o.value === value[0])?.label
          : `${value.length} seleccionados`;

    const toggle = (v: V) => {
        const next = value.includes(v) ? value.filter((x) => x !== v) : [...value, v];
        if (next.length >= minSelected) onChange(options.map((o) => o.value).filter((x) => next.includes(x)));
    };

    const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
        if (e.key === 'Escape') {
            e.stopPropagation();
            setOpen(false);
        }
    };

    return (
        <div ref={rootRef} className={cn('relative flex flex-col gap-1.5', className)}>
            <span id={labelId} className="text-xs font-semibold text-ink-3">
                {label}
            </span>
            <button
                type="button"
                aria-haspopup="dialog"
                aria-expanded={open}
                aria-labelledby={labelId}
                aria-describedby={`${labelId}-value`}
                onClick={() => setOpen((o) => !o)}
                className={cn(
                    'flex h-9 min-w-[250px] items-center justify-between gap-2 rounded-md border bg-white px-3 text-[13px] text-ink hover:bg-surface-hover',
                    open ? 'border-primary-600 ring-[3px] ring-primary-200' : 'border-line-strong',
                )}
            >
                <span id={`${labelId}-value`}>{summary}</span>
                <ChevronDown size={16} className={cn('text-ink-3 transition-transform', open && 'rotate-180')} aria-hidden="true" />
            </button>

            {open && (
                <div
                    role="dialog"
                    aria-label={label}
                    onKeyDown={onKeyDown}
                    className="absolute left-0 top-full z-popover mt-2 flex w-[300px] flex-col rounded-[10px] border border-line bg-white shadow-[0_16px_40px_rgba(13,31,51,0.18)]"
                >
                    <div className="flex flex-col gap-0.5 p-2">
                        <Checkbox
                            label={<span className="font-semibold">{allLabel}</span>}
                            checked={all}
                            indeterminate={!all && value.length > 0}
                            onChange={() => onChange(options.map((o) => o.value))}
                            containerClassName="min-h-10 items-center rounded-md px-2.5 hover:bg-surface-subtle"
                        />
                        <div className="my-1 h-px bg-line-soft" />
                        {options.map((o) => {
                            const checked = value.includes(o.value);
                            return (
                                <Checkbox
                                    key={o.value}
                                    checked={checked}
                                    disabled={checked && value.length <= minSelected}
                                    onChange={() => toggle(o.value)}
                                    containerClassName="min-h-10 items-center rounded-md px-2.5 hover:bg-surface-subtle"
                                    label={
                                        <span className="flex items-center gap-2">
                                            {o.color && (
                                                <span
                                                    className="h-2.5 w-2.5 rounded-sm"
                                                    style={{ background: o.color }}
                                                    aria-hidden="true"
                                                />
                                            )}
                                            {o.label}
                                        </span>
                                    }
                                />
                            );
                        })}
                    </div>
                    <div className="flex items-center justify-between border-t border-line px-3.5 py-2.5 text-xs text-ink-3">
                        <span>
                            {value.length} de {options.length} seleccionados
                        </span>
                        <Button size="sm" onClick={() => setOpen(false)}>
                            Listo
                        </Button>
                    </div>
                </div>
            )}
        </div>
    );
}
