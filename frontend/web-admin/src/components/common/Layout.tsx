import { Outlet, useLocation } from 'react-router-dom';
import { MOCK_CURRENT_USER, USING_MOCK_DATA } from '../../data';
import { useToast } from '../shared';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { findNavEntry } from './navigation';

export function Layout() {
    const { pathname } = useLocation();
    const entry = findNavEntry(pathname);
    const toast = useToast();

    return (
        <div className="flex min-h-screen bg-surface-ground">
            <Sidebar
                user={MOCK_CURRENT_USER}
                onLogout={() =>
                    toast({ title: 'Cierre de sesión', description: 'Se integrará con Web.BFF (cookies seguras).', tone: 'info' })
                }
            />
            <div className="flex min-w-0 flex-1 flex-col">
                <Header section={entry?.section} page={entry?.page} environmentLabel={USING_MOCK_DATA ? 'Datos de prueba' : undefined} />
                <main className="min-w-0 flex-1 px-4 pb-6 pt-5 sm:px-6 lg:px-8 lg:pb-8 lg:pt-7">
                    <Outlet />
                </main>
            </div>
        </div>
    );
}
