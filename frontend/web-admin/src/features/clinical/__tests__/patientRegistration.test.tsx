import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '../../../components/shared';
import { createFakeBff, json } from '../../../test/fakeBff';
import { PatientRegistrationForm } from '../components/PatientRegistrationForm';

const WAIT = { timeout: 5000 };

function renderForm() {
  const bff = createFakeBff({
    'POST /api/web/patients': (req) =>
      json(201, {
        id: 'p-1',
        displayName: 'Laura Méndez',
        birthDate: null,
        biologicalSex: 'Female',
        identityStatus: 'Unverified',
        primaryIdentifier: { type: 'NationalId', value: '***5678' },
        _received: req.body,
      }),
  });
  const onRegistered = vi.fn();
  render(
    <ToastProvider>
      <PatientRegistrationForm onRegistered={onRegistered} />
    </ToastProvider>,
  );
  return { bff, onRegistered };
}

async function fillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/Nombres/), 'Laura');
  await user.type(screen.getByLabelText(/Apellidos/), 'Méndez');
  await user.selectOptions(screen.getByLabelText(/Sexo biológico/), 'Female');
  await user.type(screen.getByLabelText(/Identificador \(opcional\)/), '001-1234567-8');
}

// El correo de contacto se registra junto al documento: es a donde se envía la invitación a la app del paciente.
describe('registro de paciente', () => {
  it('envía el documento y el correo de contacto', async () => {
    const { bff, onRegistered } = renderForm();
    const user = userEvent.setup();

    await fillRequired(user);
    await user.type(screen.getByLabelText(/Correo electrónico/), 'laura@correo.test');
    await user.click(screen.getByRole('button', { name: 'Registrar paciente' }));

    await waitFor(() => expect(onRegistered).toHaveBeenCalledOnce(), WAIT);
    expect(bff.calls('POST /api/web/patients')[0].body).toMatchObject({
      givenNames: 'Laura',
      identifier: { type: 'NationalId', value: '001-1234567-8' },
      email: 'laura@correo.test',
    });
    expect(screen.getByLabelText(/Correo electrónico/)).toHaveValue('');
  });

  it('sin correo lo envía vacío', async () => {
    const { bff, onRegistered } = renderForm();
    const user = userEvent.setup();

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar paciente' }));

    await waitFor(() => expect(onRegistered).toHaveBeenCalledOnce(), WAIT);
    expect((bff.calls('POST /api/web/patients')[0].body as { email: unknown }).email).toBeNull();
  });
});
