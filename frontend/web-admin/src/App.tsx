import { Suspense, lazy } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { Layout } from './components/common/Layout';
import Forbidden from './pages/Forbidden';
import NotFound from './pages/NotFound';
import NotImplemented from './pages/NotImplemented';
import { LoadingSpinner } from './components/shared';

// El Dashboard arrastra Recharts: se carga en su propio chunk solo cuando se visita.
const Dashboard = lazy(() => import('./pages/Dashboard'));

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
                    <Route path="403" element={<Forbidden />} />
                    <Route path="*" element={<NotFound />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
