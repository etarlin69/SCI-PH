export type QrErrorCorrection = 'L' | 'M';

export interface QrMatrix {
  /** Lado en módulos (21 + 4 × (versión − 1)). */
  size: number;
  version: number;
  ecl: QrErrorCorrection;
  mask: number;
  /** `modules[y][x]` = módulo oscuro. */
  modules: boolean[][];
}

const MIN_VERSION = 1;
const MAX_VERSION = 6;

/** Codewords de corrección por bloque, por nivel y versión (índice 0 sin uso). */
const ECC_CODEWORDS_PER_BLOCK: Record<QrErrorCorrection, number[]> = {
  L: [-1, 7, 10, 15, 20, 26, 18],
  M: [-1, 10, 16, 26, 18, 24, 16],
};

/** Número de bloques de corrección, por nivel y versión. */
const NUM_ERROR_CORRECTION_BLOCKS: Record<QrErrorCorrection, number[]> = {
  L: [-1, 1, 1, 1, 1, 1, 2],
  M: [-1, 1, 1, 1, 2, 2, 4],
};

/** Bits del nivel en la información de formato. */
const ECL_FORMAT_BITS: Record<QrErrorCorrection, number> = { L: 1, M: 0 };

const getBit = (x: number, i: number): boolean => ((x >>> i) & 1) !== 0;

/** Módulos disponibles para datos + ECC (sin patrones de función). */
function numRawDataModules(ver: number): number {
  let result = (16 * ver + 128) * ver + 64;
  if (ver >= 2) {
    const numAlign = Math.floor(ver / 7) + 2;
    result -= (25 * numAlign - 10) * numAlign - 55;
  }
  return result;
}

/** Codewords de datos (sin ECC) que caben en la versión y nivel dados. */
function numDataCodewords(ver: number, ecl: QrErrorCorrection): number {
  return Math.floor(numRawDataModules(ver) / 8) - ECC_CODEWORDS_PER_BLOCK[ecl][ver] * NUM_ERROR_CORRECTION_BLOCKS[ecl][ver];
}

// ---------- Reed-Solomon sobre GF(2^8) con polinomio 0x11D ----------

function rsMultiply(x: number, y: number): number {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z & 0xff;
}

function rsComputeDivisor(degree: number): number[] {
  const result = new Array<number>(degree).fill(0);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < result.length; j++) {
      result[j] = rsMultiply(result[j], root);
      if (j + 1 < result.length) result[j] ^= result[j + 1];
    }
    root = rsMultiply(root, 0x02);
  }
  return result;
}

function rsComputeRemainder(data: number[], divisor: number[]): number[] {
  const result = divisor.map(() => 0);
  for (const b of data) {
    const factor = b ^ (result.shift() as number);
    result.push(0);
    divisor.forEach((coef, i) => {
      result[i] ^= rsMultiply(coef, factor);
    });
  }
  return result;
}

// ---------- Codificación de datos ----------

function utf8Bytes(text: string): number[] {
  return Array.from(new TextEncoder().encode(text));
}

function pushBits(bits: number[], value: number, length: number): void {
  for (let i = length - 1; i >= 0; i--) bits.push((value >>> i) & 1);
}

/** Segmento en modo byte + terminador + relleno hasta la capacidad. */
function encodeDataCodewords(bytes: number[], ver: number, ecl: QrErrorCorrection): number[] {
  const capacityBits = numDataCodewords(ver, ecl) * 8;
  const bits: number[] = [];
  pushBits(bits, 0x4, 4); // modo byte
  pushBits(bits, bytes.length, 8); // contador de caracteres (8 bits en versiones 1–9)
  bytes.forEach((b) => pushBits(bits, b, 8));
  pushBits(bits, 0, Math.min(4, capacityBits - bits.length)); // terminador
  pushBits(bits, 0, (8 - (bits.length % 8)) % 8);
  for (let pad = 0xec; bits.length < capacityBits; pad ^= 0xec ^ 0x11) pushBits(bits, pad, 8);
  const codewords: number[] = [];
  for (let i = 0; i < bits.length; i += 8) {
    codewords.push(bits.slice(i, i + 8).reduce((acc, bit) => (acc << 1) | bit, 0));
  }
  return codewords;
}

/** Divide en bloques, añade ECC a cada uno y los intercala. */
function addEccAndInterleave(data: number[], ver: number, ecl: QrErrorCorrection): number[] {
  const numBlocks = NUM_ERROR_CORRECTION_BLOCKS[ecl][ver];
  const blockEccLen = ECC_CODEWORDS_PER_BLOCK[ecl][ver];
  const rawCodewords = Math.floor(numRawDataModules(ver) / 8);
  const numShortBlocks = numBlocks - (rawCodewords % numBlocks);
  const shortBlockLen = Math.floor(rawCodewords / numBlocks);

  const divisor = rsComputeDivisor(blockEccLen);
  const blocks: number[][] = [];
  for (let i = 0, k = 0; i < numBlocks; i++) {
    const dat = data.slice(k, k + shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1));
    k += dat.length;
    const ecc = rsComputeRemainder(dat, divisor);
    if (i < numShortBlocks) dat.push(0);
    blocks.push(dat.concat(ecc));
  }

  const result: number[] = [];
  for (let i = 0; i < blocks[0].length; i++) {
    blocks.forEach((block, j) => {
      if (i !== shortBlockLen - blockEccLen || j >= numShortBlocks) result.push(block[i]);
    });
  }
  return result;
}

// ---------- Matriz ----------

class Builder {
  readonly size: number;
  readonly modules: boolean[][];
  readonly isFunction: boolean[][];

  constructor(readonly version: number) {
    this.size = version * 4 + 17;
    this.modules = Array.from({ length: this.size }, () => new Array<boolean>(this.size).fill(false));
    this.isFunction = Array.from({ length: this.size }, () => new Array<boolean>(this.size).fill(false));
  }

  setFunction(x: number, y: number, dark: boolean): void {
    this.modules[y][x] = dark;
    this.isFunction[y][x] = true;
  }

  drawFunctionPatterns(): void {
    for (let i = 0; i < this.size; i++) {
      this.setFunction(6, i, i % 2 === 0);
      this.setFunction(i, 6, i % 2 === 0);
    }
    this.drawFinder(3, 3);
    this.drawFinder(this.size - 4, 3);
    this.drawFinder(3, this.size - 4);

    const positions = this.alignmentPositions();
    const n = positions.length;
    for (let i = 0; i < n; i++) {
      for (let j = 0; j < n; j++) {
        const overlapsFinder = (i === 0 && j === 0) || (i === 0 && j === n - 1) || (i === n - 1 && j === 0);
        if (!overlapsFinder) this.drawAlignment(positions[i], positions[j]);
      }
    }
    this.drawFormatBits('M', 0); // reserva el área; se sobrescribe al elegir máscara
  }

  private alignmentPositions(): number[] {
    if (this.version === 1) return [];
    // Versiones 2–6: un único patrón (además de las esquinas de los buscadores).
    return [6, this.version * 4 + 10];
  }

  private drawFinder(x: number, y: number): void {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const dist = Math.max(Math.abs(dx), Math.abs(dy));
        const xx = x + dx;
        const yy = y + dy;
        if (xx >= 0 && xx < this.size && yy >= 0 && yy < this.size) this.setFunction(xx, yy, dist !== 2 && dist !== 4);
      }
    }
  }

  private drawAlignment(x: number, y: number): void {
    for (let dy = -2; dy <= 2; dy++) {
      for (let dx = -2; dx <= 2; dx++) this.setFunction(x + dx, y + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
    }
  }

  drawFormatBits(ecl: QrErrorCorrection, mask: number): void {
    const data = (ECL_FORMAT_BITS[ecl] << 3) | mask;
    let rem = data;
    for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
    const bits = ((data << 10) | rem) ^ 0x5412;

    for (let i = 0; i <= 5; i++) this.setFunction(8, i, getBit(bits, i));
    this.setFunction(8, 7, getBit(bits, 6));
    this.setFunction(8, 8, getBit(bits, 7));
    this.setFunction(7, 8, getBit(bits, 8));
    for (let i = 9; i < 15; i++) this.setFunction(14 - i, 8, getBit(bits, i));

    for (let i = 0; i < 8; i++) this.setFunction(this.size - 1 - i, 8, getBit(bits, i));
    for (let i = 8; i < 15; i++) this.setFunction(8, this.size - 15 + i, getBit(bits, i));
    this.setFunction(8, this.size - 8, true); // módulo oscuro fijo
  }

  /** Recorre la matriz en zigzag (columnas de 2, de derecha a izquierda) colocando los bits. */
  drawCodewords(data: number[]): void {
    let i = 0;
    for (let right = this.size - 1; right >= 1; right -= 2) {
      if (right === 6) right = 5;
      for (let vert = 0; vert < this.size; vert++) {
        for (let j = 0; j < 2; j++) {
          const x = right - j;
          const upward = ((right + 1) & 2) === 0;
          const y = upward ? this.size - 1 - vert : vert;
          if (!this.isFunction[y][x] && i < data.length * 8) {
            this.modules[y][x] = getBit(data[i >>> 3], 7 - (i & 7));
            i++;
          }
        }
      }
    }
  }

  /** XOR de la máscara sobre los módulos de datos (aplicarla dos veces la deshace). */
  applyMask(mask: number): void {
    for (let y = 0; y < this.size; y++) {
      for (let x = 0; x < this.size; x++) {
        if (this.isFunction[y][x]) continue;
        let invert: boolean;
        switch (mask) {
          case 0:
            invert = (x + y) % 2 === 0;
            break;
          case 1:
            invert = y % 2 === 0;
            break;
          case 2:
            invert = x % 3 === 0;
            break;
          case 3:
            invert = (x + y) % 3 === 0;
            break;
          case 4:
            invert = (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0;
            break;
          case 5:
            invert = ((x * y) % 2) + ((x * y) % 3) === 0;
            break;
          case 6:
            invert = (((x * y) % 2) + ((x * y) % 3)) % 2 === 0;
            break;
          default:
            invert = (((x + y) % 2) + ((x * y) % 3)) % 2 === 0;
            break;
        }
        if (invert) this.modules[y][x] = !this.modules[y][x];
      }
    }
  }

  /** Penalización de la norma (reglas N1–N4) para elegir la mejor máscara. */
  penalty(): number {
    const n = this.size;
    const m = this.modules;
    let score = 0;
    const lines: boolean[][] = [];
    for (let y = 0; y < n; y++) lines.push(m[y]);
    for (let x = 0; x < n; x++) lines.push(m.map((row) => row[x]));

    const FINDER_A = [true, false, true, true, true, false, true, false, false, false, false];
    const FINDER_B = [...FINDER_A].reverse();
    for (const line of lines) {
      // N1: rachas de 5 o más módulos del mismo color.
      let run = 1;
      for (let i = 1; i <= n; i++) {
        if (i < n && line[i] === line[i - 1]) {
          run++;
        } else {
          if (run >= 5) score += 3 + (run - 5);
          run = 1;
        }
      }
      // N3: patrones parecidos a un buscador (1:1:3:1:1 con 4 claros a un lado).
      for (let i = 0; i + 11 <= n; i++) {
        const matches = (p: boolean[]) => p.every((v, k) => line[i + k] === v);
        if (matches(FINDER_A) || matches(FINDER_B)) score += 40;
      }
    }
    // N2: bloques 2×2 del mismo color.
    for (let y = 0; y < n - 1; y++) {
      for (let x = 0; x < n - 1; x++) {
        const c = m[y][x];
        if (c === m[y][x + 1] && c === m[y + 1][x] && c === m[y + 1][x + 1]) score += 3;
      }
    }
    // N4: equilibrio claro/oscuro.
    const dark = m.reduce((acc, row) => acc + row.filter(Boolean).length, 0);
    const total = n * n;
    const k = Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1;
    score += Math.max(0, k) * 10;
    return score;
  }
}

/**
 * Genera la matriz QR de `text` con la versión más pequeña posible.
 * Usa nivel M; si no cabe en la versión 6, prueba con L. Lanza un error si no cabe.
 */
export function encodeQr(text: string, preferred: QrErrorCorrection = 'M'): QrMatrix {
  const bytes = utf8Bytes(text);
  const levels: QrErrorCorrection[] = preferred === 'M' ? ['M', 'L'] : ['L'];
  for (const ecl of levels) {
    for (let ver = MIN_VERSION; ver <= MAX_VERSION; ver++) {
      // 4 bits de modo + 8 de longitud + datos.
      if (4 + 8 + bytes.length * 8 > numDataCodewords(ver, ecl) * 8) continue;
      const data = encodeDataCodewords(bytes, ver, ecl);
      const codewords = addEccAndInterleave(data, ver, ecl);
      const qr = new Builder(ver);
      qr.drawFunctionPatterns();
      qr.drawCodewords(codewords);

      let bestMask = 0;
      let bestPenalty = Infinity;
      for (let mask = 0; mask < 8; mask++) {
        qr.applyMask(mask);
        qr.drawFormatBits(ecl, mask);
        const p = qr.penalty();
        if (p < bestPenalty) {
          bestPenalty = p;
          bestMask = mask;
        }
        qr.applyMask(mask); // deshace
      }
      qr.applyMask(bestMask);
      qr.drawFormatBits(ecl, bestMask);
      return { size: qr.size, version: ver, ecl, mask: bestMask, modules: qr.modules };
    }
  }
  throw new Error(`El contenido (${bytes.length} bytes) no cabe en un QR de versión ${MAX_VERSION}.`);
}

/**
 * Trazado SVG (`d`) de los módulos oscuros, con `margin` módulos de zona silenciosa.
 * Une módulos contiguos de cada fila en un solo rectángulo para reducir el tamaño del trazado.
 */
export function qrToSvgPath(qr: QrMatrix, margin = 4): string {
  const parts: string[] = [];
  for (let y = 0; y < qr.size; y++) {
    let x = 0;
    while (x < qr.size) {
      if (!qr.modules[y][x]) {
        x++;
        continue;
      }
      const start = x;
      while (x < qr.size && qr.modules[y][x]) x++;
      parts.push(`M${start + margin} ${y + margin}h${x - start}v1h${start - x}z`);
    }
  }
  return parts.join('');
}
