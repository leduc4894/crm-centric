// SetFieldValue is a closed object containing exactly one JSON scalar. Keep a
// numeric token as text through both directions, including exponent notation.
const numberPattern = /^(?:-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)$/;
export interface ExactNumber { readonly numberToken: string }
export type FieldValue = ExactNumber | string | boolean | null;
export function exactNumber(token: string): ExactNumber {
  if (typeof token !== 'string' || !token || token.trim() !== token || !numberPattern.test(token)) throw new SyntaxError('Invalid JSON number');
  return { numberToken: token };
}
export function readField(text: string): FieldValue {
  // Match one member before decoding so JSON.parse cannot collapse duplicate keys.
  const member = /^[ \t\r\n]*\{[ \t\r\n]*("(?:[^"\\]|\\.)*")[ \t\r\n]*:[ \t\r\n]*("(?:[^"\\]|\\.)*"|true|false|null|[-+.\deE]+)[ \t\r\n]*\}[ \t\r\n]*$/.exec(text);
  if (!member || member[0] !== text || JSON.parse(member[1]!) !== 'value') throw new SyntaxError('Invalid field response');
  const raw = member[2]!;
  if (/^[-\d]/.test(raw)) return exactNumber(raw);
  const scalar: unknown = JSON.parse(raw);
  if (scalar === null || typeof scalar === 'string' || typeof scalar === 'boolean') return scalar;
  throw new SyntaxError('Invalid field response');
}
export function writeField(value: FieldValue): string {
  if (value !== null && typeof value === 'object') return '{"value":' + exactNumber(value.numberToken).numberToken + '}';
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return JSON.stringify({ value });
  throw new SyntaxError('Invalid field value');
}
