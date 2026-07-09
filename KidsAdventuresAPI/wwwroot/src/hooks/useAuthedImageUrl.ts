import { useEffect, useState } from "react";

/**
 * Loads an authenticated image (illustration, hero portrait, etc.) as a blob object URL,
 * since the underlying API routes require a Bearer token and can't be used as a bare `<img src>`.
 * Revokes the previous object URL whenever `source` changes or the component unmounts.
 */
export function useAuthedImageUrl(
  source: string | null | undefined,
  loader: (source: string) => Promise<string>,
): string | null {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!source) {
      setUrl(null);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;

    void loader(source)
      .then((result) => {
        if (cancelled) {
          URL.revokeObjectURL(result);
          return;
        }
        objectUrl = result;
        setUrl(result);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [source, loader]);

  return url;
}
