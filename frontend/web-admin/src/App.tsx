import { Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { Layout } from './components/common/Layout';
import Dashboard from './pages/Dashboard';
import NotImplemented from './pages/NotImplemented';
import { LoadingSpinner } from './components/shared';

function PageFallback() {
    return (
        <div className="flex justify-center py-24">
            <LoadingSpinner size="lg" label="Cargando…" />
        </div>
    );
}

export default function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route element={<Layout />}>
                    <Route
                        index
                        element={
                            <Suspense fallback={<PageFallback />}>
                                <Dashboard />
                            </Suspense>
                        }
                    />
                    <Route path="usuarios" element={<NotImplemented />} />
                    <Route path="auditoria" element={<NotImplemented />} />
                    <Route path="catalogos" element={<NotImplemented />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
