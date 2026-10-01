import { Outlet, useLocation } from 'react-router-dom';
import { CURRENT_USER } from '../../data';
import { useToast } from '../shared';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { findNavEntry } from './navigation';

export interface LayoutProps {
    mockData?: boolean;
}
export function Layout({ mockData = true }: LayoutProps) {
    const { pathname } = useLocation();
    const entry = findNavEntry(pathname);
    const toast = useToast();

    return (
        <div className="flex min-h-screen bg-surface-ground">
            <Sidebar
                user={CURRENT_USER}
                onLogout={() =>
                    toast({ title: 'Cierre de sesión', description: 'Se integrará con Web.BFF (cookies seguras).', tone: 'info' })
                }
            />
            <div className="flex min-w-0 flex-1 flex-col">
                <Header section={entry?.section} page={entry?.page} environmentLabel={mockData ? 'Datos de prueba' : undefined} />
                <main className="flex-1 px-8 pb-8 pt-7">
                    <Outlet />
                </main>
            </div>
        </div>
    );
}
