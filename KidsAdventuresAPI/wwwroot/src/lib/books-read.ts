/**
 * Books stamped as read during this session.
 *
 * The stamp itself lives on the server, but the POST that sets it is fire-and-forget: a parent
 * who opens a book and turns straight back can beat it, and the shelf would then re-fetch its
 * packs, see no LastReadAt, and show the book as unread for the rest of that visit. This set is
 * the optimistic half — held in memory only, and irrelevant after a reload, by which time the
 * server's own value is the answer.
 */
const readThisSession = new Set<string>();

export function rememberReadLocally(bookId: string): void {
  if (bookId) readThisSession.add(bookId);
}

export function wasReadThisSession(bookId: string): boolean {
  return readThisSession.has(bookId);
}
