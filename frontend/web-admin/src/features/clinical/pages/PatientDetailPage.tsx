import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Permissions, useAuth } from '../../../auth';
import { Card, ErrorState, LoadingSpinner, PageHeader } from '../../../components/shared';
import { clinicalApi, describeError } from '../api';
import { EncounterSubject } from '../components/EncounterSubject';
import { FaceCard } from '../components/FaceCard';
import { FingerprintCard } from '../components/FingerprintCard';
import type { PatientSummary } from '../types/clinical.types';

type State = { status: 'loading' } | { status: 'error'; error: string } | { status: 'ready'; patient: PatientSummary };

/**
 * Detalle de un paciente (GET /api/web/patients/{id}, Patients.Read) con su huella digital (Biometrics.Manage) y su rostro
 * (registrar y revocar: Biometrics.Manage; verificar: Patients.Identify).
 */
export function PatientDetailPage() {
    const { patientId = '' } = useParams();
    const auth = useAuth();
    const canManageBiometrics = auth.can(Permissions.Biometrics.Manage);
    const canVerifyFace = auth.can(Permissions.Patients.Identify);
    const [state, setState] = useState<State>({ status: 'loading' });

    useEffect(() => {
        const controller = new AbortController();
        setState({ status: 'loading' });
        clinicalApi
            .getPatient(patientId, controller.signal)
            .then((patient) => setState({ status: 'ready', patient }))
            .catch((error) => {
                if (!controller.signal.aborted) setState({ status: 'error', error: describeError(error) });
            });
        return () => controller.abort();
    }, [patientId]);

    return (
        <div className="flex flex-col gap-5">
            <PageHeader title="Paciente" back={{ to: '/hospital/pacientes', label: 'Volver a pacientes' }} />
            {state.status === 'loading' && <LoadingSpinner label="Cargando paciente…" />}
            {state.status === 'error' && <ErrorState title="No se pudo cargar el paciente." message={state.error} />}
            {state.status === 'ready' && (
                <div className="grid gap-5 xl:grid-cols-2">
                    <Card title="Datos del paciente">
                        <EncounterSubject patient={state.patient} temporaryPatientCode={null} variant="full" />
                    </Card>
                    {canManageBiometrics && <FingerprintCard patientId={state.patient.id} />}
                    {(canManageBiometrics || canVerifyFace) && (
                        <FaceCard patientId={state.patient.id} canManage={canManageBiometrics} canVerify={canVerifyFace} />
                    )}
                </div>
            )}
        </div>
    );
}
