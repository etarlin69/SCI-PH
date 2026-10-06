import {
    Ambulance,
    ClipboardPlus,
    Layers,
    LayoutDashboard,
    ShieldCheck,
    Siren,
    Stethoscope,
    Users,
    type LucideIcon,
} from 'lucide-react';

export interface NavItem {
    to: string;
    label: string;
    icon: LucideIcon;
    /**
     * Permiso del backend (Permissions.cs) que necesitará la sección. Hoy solo documenta: el filtrado del menú y de las
     * rutas llegará con el AuthProvider/RequirePermission compartido (Rol 5).
     */
    permission?: string;
}

export interface NavSection {
    title: string;
    items: NavItem[];
}

export type AppModule = 'admin' | 'hospital';

export interface ModuleNav {
    label: string;
    subtitle: string;
    home: string;
    sections: NavSection[];
}

export const MODULES: Record<AppModule, ModuleNav> = {
    admin: {
        label: 'Administración',
        subtitle: 'Administración y Analítica',
        home: '/',
        sections: [
            { title: 'Análisis', items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard }] },
            {
                title: 'Gestión',
                items: [
                    { to: '/usuarios', label: 'Usuarios', icon: Users },
                    { to: '/catalogos', label: 'Catálogos', icon: Layers },
                ],
            },
            { title: 'Cumplimiento', items: [{ to: '/auditoria', label: 'Auditoría', icon: ShieldCheck }] },
        ],
    },
    hospital: {
        label: 'Hospital',
        subtitle: 'Atención hospitalaria',
        home: '/hospital',
        sections: [
            {
                title: 'Hospital',
                items: [
                    { to: '/hospital/recepcion', label: 'Recepción', icon: ClipboardPlus, permission: 'Encounters.Create' },
                    { to: '/hospital/pacientes', label: 'Pacientes', icon: Users, permission: 'Patients.Read' },
                    { to: '/hospital/atenciones', label: 'Atenciones', icon: Stethoscope, permission: 'Encounters.Read' },
                    { to: '/hospital/triage', label: 'Triage', icon: Siren, permission: 'Triage.Write' },
                    { to: '/hospital/transferencias', label: 'Transferencias', icon: Ambulance, permission: 'Transfers.Read' },
                ],
            },
        ],
    },
};

export function moduleForPath(pathname: string): AppModule {
    return pathname === '/hospital' || pathname.startsWith('/hospital/') ? 'hospital' : 'admin';
}

// Vistas de una atención: /hospital/atenciones/:id[/triage|/medicacion].
const ENCOUNTER_VIEW = /^\/hospital\/atenciones\/[^/]+(?:\/(triage|medicacion))?\/?$/;
const ENCOUNTER_VIEW_LABELS: Record<string, string> = { '': 'Expediente', triage: 'Triage', medicacion: 'Medicación y egreso' };

export function findNavEntry(pathname: string): { section: string; page: string } | null {
    const encounterView = ENCOUNTER_VIEW.exec(pathname);
    if (encounterView) return { section: 'Atenciones', page: ENCOUNTER_VIEW_LABELS[encounterView[1] ?? ''] };

    for (const section of MODULES[moduleForPath(pathname)].sections) {
        const item = section.items.find((i) => (i.to === '/' ? pathname === '/' : pathname.startsWith(i.to)));
        if (item) return { section: section.title, page: item.label };
    }
    return null;
}
