export type PageInteractiveSession = {
  avatarTapped?: boolean;
  findItFound?: boolean;
  countingDone?: boolean;
  countValue?: number;
  tappedCountIndices?: number[];
  revealDone?: boolean;
};

function storageKey(packId: string, pageIndex: number) {
  return `adventrya-interactive:${packId}:${pageIndex}`;
}

export function loadPageInteractiveSession(
  packId: string | undefined,
  pageIndex: number,
): PageInteractiveSession {
  if (!packId || typeof window === "undefined") return {};
  try {
    const raw = sessionStorage.getItem(storageKey(packId, pageIndex));
    if (!raw) return {};
    return JSON.parse(raw) as PageInteractiveSession;
  } catch {
    return {};
  }
}

export function savePageInteractiveSession(
  packId: string | undefined,
  pageIndex: number,
  patch: Partial<PageInteractiveSession>,
) {
  if (!packId || typeof window === "undefined") return;
  const current = loadPageInteractiveSession(packId, pageIndex);
  const next = { ...current, ...patch };
  sessionStorage.setItem(storageKey(packId, pageIndex), JSON.stringify(next));
}
