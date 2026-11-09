/** 小端字节读写，语义对齐 C# 的 BinaryReader / BinaryWriter */
export class ByteReader {
  private view: DataView;
  private bytes: Uint8Array;
  pos: number;

  constructor(buf: Uint8Array, pos = 0) {
    this.bytes = buf;
    this.view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
    this.pos = pos;
  }

  get length() {
    return this.bytes.length;
  }

  get remaining() {
    return this.bytes.length - this.pos;
  }

  need(n: number) {
    if (this.pos + n > this.bytes.length) {
      throw new RangeError(`越界读取: 需要 ${n} 字节，剩余 ${this.remaining}`);
    }
  }

  u8() {
    this.need(1);
    return this.view.getUint8(this.pos++);
  }

  i8() {
    this.need(1);
    return this.view.getInt8(this.pos++);
  }

  u16() {
    this.need(2);
    const v = this.view.getUint16(this.pos, true);
    this.pos += 2;
    return v;
  }

  i16() {
    this.need(2);
    const v = this.view.getInt16(this.pos, true);
    this.pos += 2;
    return v;
  }

  u32() {
    this.need(4);
    const v = this.view.getUint32(this.pos, true);
    this.pos += 4;
    return v;
  }

  i32() {
    this.need(4);
    const v = this.view.getInt32(this.pos, true);
    this.pos += 4;
    return v;
  }

  /** C# long -> JS number（协议里没有超过 2^53 的数值） */
  i64() {
    this.need(8);
    const v = this.view.getBigInt64(this.pos, true);
    this.pos += 8;
    return Number(v);
  }

  u64() {
    this.need(8);
    const v = this.view.getBigUint64(this.pos, true);
    this.pos += 8;
    return Number(v);
  }

  f32() {
    this.need(4);
    const v = this.view.getFloat32(this.pos, true);
    this.pos += 4;
    return v;
  }

  f64() {
    this.need(8);
    const v = this.view.getFloat64(this.pos, true);
    this.pos += 8;
    return v;
  }

  bool() {
    return this.u8() !== 0;
  }

  /** C# BinaryWriter.Write(string): 7-bit 变长字节数 + UTF8 */
  str() {
    let len = 0;
    let shift = 0;
    for (;;) {
      const b = this.u8();
      len |= (b & 0x7f) << shift;
      if ((b & 0x80) === 0) break;
      shift += 7;
      if (shift > 28) throw new Error('字符串长度前缀非法');
    }
    this.need(len);
    const slice = this.bytes.subarray(this.pos, this.pos + len);
    this.pos += len;
    return new TextDecoder().decode(slice);
  }

  raw(n: number) {
    this.need(n);
    const slice = this.bytes.subarray(this.pos, this.pos + n);
    this.pos += n;
    return slice;
  }

  point() {
    return { x: this.i32(), y: this.i32() };
  }
}

export class ByteWriter {
  private buf: number[] = [];

  u8(v: number) {
    this.buf.push(v & 0xff);
    return this;
  }

  i8(v: number) {
    return this.u8(v);
  }

  u16(v: number) {
    this.buf.push(v & 0xff, (v >> 8) & 0xff);
    return this;
  }

  i16(v: number) {
    return this.u16(v);
  }

  u32(v: number) {
    this.buf.push(v & 0xff, (v >>> 8) & 0xff, (v >>> 16) & 0xff, (v >>> 24) & 0xff);
    return this;
  }

  i32(v: number) {
    return this.u32(v);
  }

  i64(v: number) {
    const b = new DataView(new ArrayBuffer(8));
    b.setBigInt64(0, BigInt(Math.trunc(v)), true);
    for (let i = 0; i < 8; i++) this.buf.push(b.getUint8(i));
    return this;
  }

  u64(v: number) {
    const b = new DataView(new ArrayBuffer(8));
    b.setBigUint64(0, BigInt(Math.trunc(v)), true);
    for (let i = 0; i < 8; i++) this.buf.push(b.getUint8(i));
    return this;
  }

  bool(v: boolean) {
    return this.u8(v ? 1 : 0);
  }

  str(s: string) {
    const bytes = new TextEncoder().encode(s ?? '');
    let len = bytes.length;
    do {
      let b = len & 0x7f;
      len >>>= 7;
      if (len) b |= 0x80;
      this.buf.push(b);
    } while (len);
    for (const b of bytes) this.buf.push(b);
    return this;
  }

  raw(bytes: Uint8Array) {
    for (const b of bytes) this.buf.push(b);
    return this;
  }

  build() {
    return new Uint8Array(this.buf);
  }
}
