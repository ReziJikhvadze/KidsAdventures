/**
 * Shrinks a portrait before it ever leaves the phone.
 *
 * A photo straight from a modern camera is four to eight megabytes, and reading it as a data
 * URL adds about a third again in base64. That comfortably exceeds the eight megabyte request
 * limit on the preview endpoint — and an oversized request is rejected by the server before any
 * of our code runs, so the rejection carries no CORS headers and the browser reports it as a
 * CORS error. The real cause is invisible from the console, which is what made it hard to see.
 *
 * Downscaling also makes the request faster on a phone connection and costs the vision model
 * less: it only ever needed a clear face, never twelve megapixels.
 */

/** Long edge, in pixels. Comfortably more than the vision model can use from a face. */
const MAX_EDGE = 1024;

/** JPEG quality. High enough that a face stays crisp, low enough to keep uploads small. */
const QUALITY = 0.82;

/** Anything already smaller than this is left exactly as it is. */
const SKIP_BELOW_BYTES = 400_000;

export type PreparedPortrait = {
  dataUrl: string;
  /** Bytes actually carried by the data URL, so callers can log or guard on it. */
  approximateBytes: number;
};

/**
 * Reads a chosen file and returns a data URL small enough to upload.
 *
 * Falls back to the untouched file whenever anything goes wrong. A parent whose browser cannot
 * decode the image should get the old behaviour rather than an error, since the server still
 * accepts photos under the limit.
 */
export async function preparePortrait(file: File): Promise<PreparedPortrait> {
  const original = await readAsDataUrl(file);

  if (file.size <= SKIP_BELOW_BYTES) {
    return { dataUrl: original, approximateBytes: approximateBytes(original) };
  }

  try {
    const image = await loadImage(original);
    const { width, height } = fit(image.naturalWidth, image.naturalHeight);

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext("2d");
    if (!context) {
      return { dataUrl: original, approximateBytes: approximateBytes(original) };
    }

    // White underneath, because a transparent PNG flattened onto nothing turns black in JPEG
    // and a child's portrait must not arrive as a silhouette.
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, width, height);
    context.drawImage(image, 0, 0, width, height);

    const shrunk = canvas.toDataURL("image/jpeg", QUALITY);

    // Only keep it if it actually helped. A small image re-encoded can come out larger.
    return shrunk.length < original.length
      ? { dataUrl: shrunk, approximateBytes: approximateBytes(shrunk) }
      : { dataUrl: original, approximateBytes: approximateBytes(original) };
  } catch {
    return { dataUrl: original, approximateBytes: approximateBytes(original) };
  }
}

function fit(width: number, height: number): { width: number; height: number } {
  const longest = Math.max(width, height);
  if (longest <= MAX_EDGE) {
    return { width, height };
  }

  const scale = MAX_EDGE / longest;
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () =>
      typeof reader.result === "string"
        ? resolve(reader.result)
        : reject(new Error("The photo could not be read."));
    reader.onerror = () => reject(reader.error ?? new Error("The photo could not be read."));
    reader.readAsDataURL(file);
  });
}

function loadImage(dataUrl: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("The photo could not be decoded."));
    image.src = dataUrl;
  });
}

/** Base64 carries three bytes in every four characters, ignoring the small header. */
function approximateBytes(dataUrl: string): number {
  const comma = dataUrl.indexOf(",");
  const payload = comma < 0 ? dataUrl : dataUrl.slice(comma + 1);
  return Math.floor((payload.length * 3) / 4);
}
