import React, { Suspense, lazy } from 'react';
import { BrowserRouter, Route, Routes, Navigate } from 'react-router-dom';
import { Layout } from './components/common/Layout';
import { LoadingSpinner } from './components/shared';

import { LoginPage } from './features/auth/pages/LoginPage';
import { ReceptionPage } from './features/clinical/pages/ReceptionPage';
import { TriagePage } from './features/clinical/pages/TriagePage';
import { EncounterDashboardPage } from './features/clinical/pages/EncounterDashboardPage';
import { PharmacyAndDischargePage } from './features/clinical/pages/PharmacyAndDischargePage';

import Forbidden from './pages/Forbidden';
import NotFound from './pages/NotFound';
import NotImplemented from './pages/NotImplemented';

import './App.css';

const Dashboard = lazy(() => import('./pages/Dashboard'));

function PageFallback() {
  return (
    <div className="flex justify-center py-24">
      <LoadingSpinner size="lg" label="Cargando…" />
    </div>
  );
}

const PrivateRoute = ({ children }: { children: React.JSX.Element }) => {
  const token = localStorage.getItem('token');
  return token ? children : <Navigate to="/login" replace />;
};

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        {/* Ruta pública */}
        <Route path="/login" element={<LoginPage />} />

        {/* Rutas protegidas dentro del Layout de la aplicación */}
        <Route
          element={
            <PrivateRoute>
              <Layout />
            </PrivateRoute>
          }
        >
          {/* Dashboard principal lazy-loaded */}
          <Route
            index
            element={
              <Suspense fallback={<PageFallback />}>
                <Dashboard />
              </Suspense>
            }
          />

          {/* Rutas Clínicas */}
          <Route path="reception" element={<ReceptionPage />} />
          <Route path="triage" element={<TriagePage />} />
          <Route path="encounter-dashboard" element={<EncounterDashboardPage />} />
          <Route path="pharmacy-discharge" element={<PharmacyAndDischargePage />} />

          {/* Modulos del sistema / Administración */}
          <Route path="usuarios" element={<NotImplemented />} />
          <Route path="auditoria" element={<NotImplemented />} />
          <Route path="catalogos" element={<NotImplemented />} />

          {/* Páginas de estado y error */}
          <Route path="403" element={<Forbidden />} />
          <Route path="*" element={<NotFound />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
