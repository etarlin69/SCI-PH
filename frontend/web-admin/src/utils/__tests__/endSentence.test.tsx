import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ErrorState } from '../../components/shared';
import { endSentence } from '../formatters';

// Issue #48: una sola puntuación final, también cuando la oración termina en una hora con "a. m." / "p. m.".
describe('endSentence', () => {
  it.each([
    ['Hola', 'Hola.'],
    ['No se pudieron cargar los traslados.', 'No se pudieron cargar los traslados.'],
    ['La atención está cerrada desde 9/11/26, 11:03 a. m.', 'La atención está cerrada desde 9/11/26, 11:03 a. m.'],
    ['¿Continuar?', '¿Continuar?'],
    ['Listo!  ', 'Listo!'],
    ['Cargando…', 'Cargando…'],
  ])('%s → %s', (input, expected) => {
    expect(endSentence(input)).toBe(expected);
  });
});

describe('ErrorState banner', () => {
  it('no duplica el punto de un título que ya lo tiene', () => {
    render(<ErrorState variant="banner" title="No se pudieron cargar los traslados." message="Inténtalo de nuevo." />);

    const alert = screen.getByRole('alert');
    expect(alert.textContent).toContain('No se pudieron cargar los traslados. Inténtalo de nuevo.');
    expect(alert.textContent).not.toContain('..');
  });

  it('añade el punto a un título que no lo tiene', () => {
    render(<ErrorState variant="banner" title="Sin contexto clínico" message="x" />);

    expect(screen.getByRole('alert').textContent).toContain('Sin contexto clínico. x');
  });
});
