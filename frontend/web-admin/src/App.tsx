import { Suspense, lazy } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Layout } from './components/common/Layout';
import { EncounterDashboardPage } from './features/clinical/pages/EncounterDashboardPage';
import { PharmacyAndDischargePage } from './features/clinical/pages/PharmacyAndDischargePage';
import { ReceptionPage } from './features/clinical/pages/ReceptionPage';
import { TransfersPage } from './features/clinical/pages/TransfersPage';
import { TriagePage } from './features/clinical/pages/TriagePage';
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

// Sin guards todavía: la sesión (login, MFA, refresh, /me) y RequireAuth/RequirePermission los aporta la
// implementación compartida del Rol 5. El permiso esperado de cada sección hospitalaria está en navigation.ts.
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

                    <Route path="hospital">
                        <Route index element={<Navigate to="recepcion" replace />} />
                        <Route path="recepcion" element={<ReceptionPage />} />
                        <Route path="pacientes" element={<NotImplemented />} />
                        <Route path="atenciones" element={<EncounterDashboardPage />} />
                        <Route path="atenciones/:encounterId" element={<EncounterDashboardPage />} />
                        <Route path="atenciones/:encounterId/triage" element={<TriagePage />} />
                        <Route path="atenciones/:encounterId/medicacion" element={<PharmacyAndDischargePage />} />
                        <Route path="triage" element={<TriagePage />} />
                        <Route path="transferencias" element={<TransfersPage />} />
                    </Route>

                    <Route path="403" element={<Forbidden />} />
                    <Route path="*" element={<NotFound />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}
