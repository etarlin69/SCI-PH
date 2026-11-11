import { describe, expect, it } from 'vitest';
import { formatVitalValue, vitalSignMeta } from '../utils/clinicalLabels';

// Las escalas muestran el valor sobre su máximo: nunca "14 3–15" (el rango del campo no es una unidad).
describe('formatVitalValue', () => {
  it.each([
    ['GlasgowComaScale', 14, '14/15'],
    ['PainScore', 7, '7/10'],
    ['HeartRate', 84, '84 lpm'],
    ['Temperature', 37.2, '37.2 °C'],
    ['OxygenSaturation', 97, '97 %'],
  ] as const)('%s %d → %s', (type, value, expected) => {
    expect(formatVitalValue(value, vitalSignMeta(type))).toBe(expected);
  });
});
