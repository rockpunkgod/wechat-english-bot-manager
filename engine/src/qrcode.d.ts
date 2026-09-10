declare module 'qrcode' {
  export interface ToFileOptions {
    width?: number;
    margin?: number;
  }

  export function toFile(path: string, text: string, options?: ToFileOptions): Promise<void>;

  const QRCode: { toFile: typeof toFile };
  export default QRCode;
}
