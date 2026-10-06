/** Agrupa el secreto de 4 en 4 caracteres para leerlo o dictarlo sin errores. */
export function groupSecret(secret: string): string {
  return secret.replace(/(.{4})/g, '$1 ').trim();
}
