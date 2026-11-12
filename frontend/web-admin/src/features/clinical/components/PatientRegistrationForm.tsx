import { useState, type FormEvent } from 'react';
import { Button, Input, Select } from '../../../components/shared';
import { clinicalApi } from '../api';
import { useClinicalFeedback } from '../hooks/useClinicalFeedback';
import type { BiologicalSex, PatientIdentifierType, PatientSummary } from '../types/clinical.types';
import { IDENTIFIER_TYPE_LABELS, SEX_LABELS } from '../utils/clinicalLabels';

const IDENTIFIER_TYPES: PatientIdentifierType[] = ['NationalId', 'Passport', 'InternalCode'];
const SEXES: BiologicalSex[] = ['Female', 'Male', 'Intersex', 'Unknown'];

/**
 * Registro de paciente (Patients.Create): Patient.Register + identificador inicial opcional. El valor se envía tal como
 * se escribió: lo normaliza el backend (Domain), nunca el cliente.
 */
export function PatientRegistrationForm({ onRegistered }: { onRegistered: (patient: PatientSummary) => void }) {
    const feedback = useClinicalFeedback('patients');
    const [givenNames, setGivenNames] = useState('');
    const [familyNames, setFamilyNames] = useState('');
    const [dateOfBirth, setDateOfBirth] = useState('');
    const [sex, setSex] = useState<BiologicalSex | ''>('');
    const [identifierType, setIdentifierType] = useState<PatientIdentifierType>('NationalId');
    const [identifierValue, setIdentifierValue] = useState('');
    const [email, setEmail] = useState('');
    const [saving, setSaving] = useState(false);
    const today = new Date().toISOString().slice(0, 10);

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        if (saving || !givenNames.trim() || !familyNames.trim() || !sex) return;
        setSaving(true);
        try {
            const patient = await clinicalApi.registerPatient({
                givenNames: givenNames.trim(),
                familyNames: familyNames.trim(),
                dateOfBirth: dateOfBirth || null,
                sex,
                identifier: identifierValue.trim() ? { type: identifierType, value: identifierValue } : null,
                email: email.trim() || null,
            });
            feedback.success('paciente registrado');
            setGivenNames('');
            setFamilyNames('');
            setDateOfBirth('');
            setSex('');
            setIdentifierValue('');
            setEmail('');
            onRegistered(patient);
        } catch (error) {
            feedback.failure('No se pudo registrar al paciente.', error);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} className="flex flex-col gap-3" aria-label="Registrar paciente">
            <div className="grid gap-3 sm:grid-cols-2">
                <Input label="Nombres" value={givenNames} onChange={(e) => setGivenNames(e.target.value)} maxLength={120} required autoComplete="off" />
                <Input label="Apellidos" value={familyNames} onChange={(e) => setFamilyNames(e.target.value)} maxLength={120} required autoComplete="off" />
                <Input label="Fecha de nacimiento (opcional)" type="date" max={today} value={dateOfBirth} onChange={(e) => setDateOfBirth(e.target.value)} />
                <Select
                    label="Sexo biológico"
                    value={sex}
                    onChange={(e) => setSex(e.target.value as BiologicalSex)}
                    options={[{ value: '', label: 'Selecciona…' }, ...SEXES.map((s) => ({ value: s, label: SEX_LABELS[s] }))]}
                    required
                />
                <Select
                    label="Tipo de identificador"
                    value={identifierType}
                    onChange={(e) => setIdentifierType(e.target.value as PatientIdentifierType)}
                    options={IDENTIFIER_TYPES.map((t) => ({ value: t, label: IDENTIFIER_TYPE_LABELS[t] }))}
                />
                <Input
                    label="Identificador (opcional)"
                    value={identifierValue}
                    onChange={(e) => setIdentifierValue(e.target.value)}
                    maxLength={250}
                    autoComplete="off"
                />
                <Input
                    label="Correo electrónico (opcional)"
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    maxLength={250}
                    autoComplete="off"
                    hint="Necesario para invitar al paciente a la app SCI-PH."
                />
            </div>
            <Button type="submit" loading={saving} disabled={!givenNames.trim() || !familyNames.trim() || !sex} className="w-full sm:w-fit">
                Registrar paciente
            </Button>
        </form>
    );
}
