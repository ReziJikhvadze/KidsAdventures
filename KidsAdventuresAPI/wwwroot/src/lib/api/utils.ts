/**
 * The name the server gave a downloaded file, from its `Content-Disposition`.
 *
 * `filename*` first: it is the RFC 5987 parameter and the only one that can carry a Georgian
 * title, which is what every book here is called. The plain `filename` is an ASCII fallback for
 * clients that predate it, so it is read only when the encoded one is absent.
 */
export function filenameFromContentDisposition(header: string | null): string | null {
  if (!header) return null;
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      /* a malformed percent-escape is not a name; fall through to the plain parameter */
    }
  }
  return /filename="?([^";]+)"?/i.exec(header)?.[1] ?? null;
}

const ILLEGAL_IN_FILE_NAMES = '\\/:*?"<>|';

/**
 * Strips only what a file system refuses — the reserved punctuation and control characters, plus
 * a trailing dot or space, which Windows will not accept. Unicode is kept on purpose: a Georgian
 * title is a perfectly legal file name and it is the name on the book's cover.
 */
export function safeFileName(name: string): string {
  const kept = [...name].filter((character) => {
    const code = character.codePointAt(0) ?? 0;
    return code > 31 && code !== 127 && !ILLEGAL_IN_FILE_NAMES.includes(character);
  });
  return kept
    .join("")
    .trim()
    .replace(/[. ]+$/, "");
}

export function mimeToImageExtension(mime: string): "jpg" | "png" | "webp" {
  if (mime === "image/png") return "png";
  if (mime === "image/webp") return "webp";
  return "jpg";
}

export function dataUrlToFile(dataUrl: string, filename: string): File {
  const [header, base64] = dataUrl.split(",");
  const mime = header.match(/:(.*?);/)?.[1] ?? "image/jpeg";
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const extension = mimeToImageExtension(mime);
  const resolvedName = filename.includes(".") ? filename : `${filename}.${extension}`;
  return new File([bytes], resolvedName, { type: mime });
}
