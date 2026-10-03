import { LayoutDashboard, Layers, ShieldCheck, Users, type LucideIcon } from 'lucide-react';

export interface NavItem {
    to: string;
    label: string;
    icon: LucideIcon;
}

export interface NavSection {
    title: string;
    items: NavItem[];
}

export const NAV_SECTIONS: NavSection[] = [
    { title: 'Análisis', items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard }] },
    {
        title: 'Gestión',
        items: [
            { to: '/usuarios', label: 'Usuarios', icon: Users },
            { to: '/catalogos', label: 'Catálogos', icon: Layers },
        ],
    },
    { title: 'Cumplimiento', items: [{ to: '/auditoria', label: 'Auditoría', icon: ShieldCheck }] },
];

export function findNavEntry(pathname: string): { section: string; page: string } | null {
    for (const section of NAV_SECTIONS) {
        const item = section.items.find((i) => (i.to === '/' ? pathname === '/' : pathname.startsWith(i.to)));
        if (item) return { section: section.title, page: item.label };
    }
    return null;
}
