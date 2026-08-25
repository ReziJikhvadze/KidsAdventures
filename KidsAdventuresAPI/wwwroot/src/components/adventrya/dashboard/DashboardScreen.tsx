import {
  ArrowRight,
  BookOpen,
  Check,
  Download,
  Package,
  Plus,
  Printer,
  Sparkles,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { AppHeader } from "@/components/adventrya/AppHeader";
import { PasswordlessAuthDialog } from "@/components/auth/PasswordlessAuthDialog";
import { ApiError } from "@/lib/api/client";
import {
  downloadAdventurePack,
  generatePackPdf,
  listAdventurePacks,
  pollAdventurePack,
} from "@/lib/api/adventure-packs";
import { listCharacters } from "@/lib/api/characters";
import { createPrintUpgradeOrder } from "@/lib/api/orders";
import { listPrintOrders, updatePrintOrderAddress } from "@/lib/api/print-orders";
import type {
  AdventurePackResponse,
  CharacterResponse,
  PrintOrderResponse,
  ShippingAddressRequest,
} from "@/lib/api/types";
import { useAuth } from "@/lib/auth/AuthContext";
import { readBookIds } from "@/lib/books-read";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { newBookHref } from "@/lib/continue";
import { formatGel, normalizeGeorgianPhone, useT } from "@/lib/i18n";
import { DELIVERY_DAYS, PRICES } from "@/lib/pricing";
import { useWorldById, WORLD_COVER_ART, isWorldId } from "@/lib/worlds";

/** Books per shelf page. Six fills the grid without a wall of illustrations to load. */
const LIBRARY_PAGE_SIZE = 6;

/** Children per sidebar page, chosen so the nav links below stay on screen. */
const SIDEBAR_PAGE_SIZE = 5;

const emptyShipping = (): ShippingAddressRequest => ({
  recipientName: "",
  recipientPhone: "",
  city: "",
  addressLine1: "",
  addressLine2: "",
  postalCode: "",
  notes: "",
  saveForLater: true,
});

export function DashboardScreen() {
  const t = useT();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [characters, setCharacters] = useState<CharacterResponse[]>([]);
  const [characterId, setCharacterId] = useState<string | null>(null);
  const [packs, setPacks] = useState<AdventurePackResponse[]>([]);
  const [printOrders, setPrintOrders] = useState<PrintOrderResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [printBookId, setPrintBookId] = useState<string | null>(null);
  const [editPrintOrderId, setEditPrintOrderId] = useState<string | null>(null);
  const [shipping, setShipping] = useState<ShippingAddressRequest>(emptyShipping);
  const [printBusy, setPrintBusy] = useState(false);
  const [printError, setPrintError] = useState<string | null>(null);
  const navigate = useNavigate();
  /*
    Signing in closes the dialog too, and a close is otherwise read as "no thanks, take me back".
    Without this flag a parent who signed in with Google was returned to the home page by the
    same handler that exists to catch the ones who changed their mind.
  */
  const signedInHere = useRef(false);

  // Read once: the set only changes when the parent leaves for the reader and comes back, which
  // remounts this screen anyway.
  const [readBooks] = useState(() => readBookIds());

  useEffect(() => {
    if (authLoading) return;
    // Signed out there is nothing to load and nothing to open: the sign-in dialog is the screen.
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    void (async () => {
      try {
        const [chars, allPacks, prints] = await Promise.all([
          listCharacters(),
          listAdventurePacks(),
          listPrintOrders().catch(() => [] as PrintOrderResponse[]),
        ]);
        if (cancelled) return;
        const kids = chars.filter((c) => c.characterType === "child" || c.isPrimary);
        const list = kids.length ? kids : chars;
        setCharacters(list);
        setPacks(allPacks);
        setPrintOrders(prints);
        // The child whose book just arrived, ahead of the nominal primary. A parent lands
        // here straight from a purchase, and a dashboard that opens on a different child's
        // empty shelf reads as the book not existing — which is exactly how it was reported.
        const newestPack = [...allPacks].sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
        const byNewestBook = newestPack?.primaryCharacterId
          ? list.find((c) => c.id === newestPack.primaryCharacterId)
          : null;
        const primary = byNewestBook ?? list.find((c) => c.isPrimary) ?? list[0] ?? null;
        setCharacterId(primary?.id ?? null);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : t.common.states.dashboardFailed);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated, t]);

  // A book still being drawn becomes ready without the parent refreshing: while any pack is
  // mid-generation the shelf re-asks for the list, and the card at the top of the page turns
  // into a book on the shelf the moment the pipeline completes.
  useEffect(() => {
    if (!isAuthenticated) return;
    if (!packs.some((p) => isPackGenerating(p.status))) return;
    const timer = window.setInterval(() => {
      void listAdventurePacks()
        .then((fresh) => setPacks(fresh))
        .catch(() => {
          /* a failed refresh keeps the current shelf; the next tick tries again */
        });
    }, 10000);
    return () => window.clearInterval(timer);
  }, [packs, isAuthenticated]);

  const character = characters.find((c) => c.id === characterId) ?? null;
  const heroName = character?.name || t.common.fallbackHeroName;
  const childPacks = useMemo(
    () =>
      packs
        .filter((p) => !characterId || p.primaryCharacterId === characterId)
        .sort(
          (a, b) =>
            (a.sequenceNumber ?? 0) - (b.sequenceNumber ?? 0) ||
            a.createdAt.localeCompare(b.createdAt),
        ),
    [packs, characterId],
  );

  /*
    A book being drawn is not on the shelf yet.

    It has no cover to open, no PDF to download and nothing to print, so it appears once — as
    the card at the top of the page, which is the only thing here that changes while a parent
    watches it — and joins the shelf as an ordinary book when the pipeline finishes.
  */
  const drawing = useMemo(() => childPacks.filter((p) => isPackGenerating(p.status)), [childPacks]);
  const shelfPacks = useMemo(
    () => childPacks.filter((p) => !isPackGenerating(p.status)),
    [childPacks],
  );
  const hasStories = childPacks.length > 0;

  // The shelf is paged rather than infinite: a family buying a book a month has a wall
  // of covers by the second year, and every one of them loads an illustration.
  const [libraryPage, setLibraryPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(shelfPacks.length / LIBRARY_PAGE_SIZE));
  const visiblePacks = useMemo(
    () => shelfPacks.slice((libraryPage - 1) * LIBRARY_PAGE_SIZE, libraryPage * LIBRARY_PAGE_SIZE),
    [shelfPacks, libraryPage],
  );

  // Switching child leaves the old page number behind, which can land past the end.
  useEffect(() => {
    setLibraryPage(1);
  }, [shelfPacks.length]);

  const [childPage, setChildPage] = useState(1);
  const childPageCount = Math.max(1, Math.ceil(characters.length / SIDEBAR_PAGE_SIZE));
  const visibleChildren = useMemo(
    () => characters.slice((childPage - 1) * SIDEBAR_PAGE_SIZE, childPage * SIDEBAR_PAGE_SIZE),
    [characters, childPage],
  );

  /*
    One creation action, and it starts something new.

    A single href used to serve "create a new book", the empty state and the map's call to
    action, and it resolved to a continuation for any family that already had books — which
    lands on the preview stage and starts a billed generation on sight.
  */
  const newHref = useMemo(() => newBookHref(characterId), [characterId]);

  const hrefParts = useCallback((href: string) => {
    const [pathAndQuery, hash] = href.split("#");
    const [to, query = ""] = (pathAndQuery || "/create").split("?");
    return {
      to: to || "/create",
      search: Object.fromEntries(new URLSearchParams(query).entries()),
      // No `|| "preview"` fallback. That default is what put every hash-less href back on the
      // preview stage, which is the whole bug this is fixing.
      hash: hash || undefined,
    };
  }, []);

  const newParts = useMemo(() => hrefParts(newHref), [hrefParts, newHref]);

  const printByBook = useMemo(() => {
    const dict: Record<string, PrintOrderResponse> = {};
    for (const order of printOrders) dict[order.bookId] = order;
    return dict;
  }, [printOrders]);

  const storyCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const pack of packs) {
      const id = pack.primaryCharacterId;
      if (!id) continue;
      counts[id] = (counts[id] ?? 0) + 1;
    }
    return counts;
  }, [packs]);

  const beginEditPrintAddress = (order: PrintOrderResponse) => {
    setPrintBookId(null);
    setEditPrintOrderId(order.id);
    setPrintError(null);
    setShipping({
      recipientName: order.recipientName,
      recipientPhone: order.recipientPhone,
      city: order.city,
      addressLine1: order.addressLine1,
      addressLine2: order.addressLine2 ?? "",
      postalCode: order.postalCode ?? "",
      notes: order.notes ?? "",
      saveForLater: true,
    });
  };

  const submitPrintForm = async () => {
    if (!shipping.recipientName.trim() || !shipping.city.trim() || !shipping.addressLine1.trim()) {
      setPrintError("მიუთითე მიწოდების მისამართი.");
      return;
    }
    const phone = normalizeGeorgianPhone(shipping.recipientPhone) ?? shipping.recipientPhone.trim();
    if (!phone) {
      setPrintError("მიუთითე ტელეფონის ნომერი.");
      return;
    }

    setPrintBusy(true);
    setPrintError(null);
    try {
      if (editPrintOrderId) {
        await updatePrintOrderAddress(editPrintOrderId, {
          ...shipping,
          recipientPhone: phone,
        });
        setEditPrintOrderId(null);
        setShipping(emptyShipping());
        setPrintOrders(await listPrintOrders());
        return;
      }

      if (!printBookId) return;
      const checkout = await createPrintUpgradeOrder({
        bookId: printBookId,
        shippingAddress: { ...shipping, recipientPhone: phone },
        returnPath: "/dashboard",
      });
      if (checkout.isFree || !checkout.checkoutUrl) {
        setPrintBookId(null);
        setShipping(emptyShipping());
        setPrintOrders(await listPrintOrders());
        return;
      }
      window.location.assign(checkout.checkoutUrl);
    } catch (err) {
      setPrintError(
        err instanceof ApiError
          ? err.message
          : editPrintOrderId
            ? "მისამართის განახლება ვერ მოხერხდა."
            : "ბეჭდური შეკვეთა ვერ შეიქმნა.",
      );
    } finally {
      setPrintBusy(false);
    }
  };

  /*
    Signed out, the door and nothing else.

    What stood here was the dashboard filled with an invented family, blurred out of reach, under
    a panel explaining that this was somebody else's household and that signing in would replace
    it. Three things a parent had to read and one more button to press before they could do the
    only thing this screen offers them while signed out. The sign-in opens on arrival now; behind
    it is the shell, not a stranger's children.
  */
  if (!authLoading && !isAuthenticated) {
    return (
      <div className="screen dashboard-shell dashboard-shell-story-map">
        <div className="dashboard-sky" aria-hidden="true" />
        <div className="grain" aria-hidden="true" />
        <AppHeader backHref="/" worldMode />

        <PasswordlessAuthDialog
          open
          onOpenChange={(open) => {
            /* Dismissed, not completed: leave the screen rather than reveal an empty one. */
            if (!open && !signedInHere.current) void navigate({ to: "/" });
          }}
          onSuccess={() => {
            signedInHere.current = true;
          }}
        />
      </div>
    );
  }

  return (
    <div className="screen dashboard-shell">
      <div className="dashboard-sky" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/" />

      {/*
        The sidebar answers one question — whose shelf am I looking at — and holds the two ways
        to make something new, pinned to its foot so the column reads as two groups rather than
        stopping halfway down the screen.
      */}
      <aside className="dashboard-sidebar">
        <p className="sidebar-title">{t.dashboard.sidebar.parentLabel}</p>

        {visibleChildren.map((c) => (
          <button
            key={c.id}
            type="button"
            className={`child-switch-card ${c.id === characterId ? "selected" : ""}`}
            onClick={() => setCharacterId(c.id)}
          >
            <span className="child-avatar nia-avatar" aria-hidden="true">
              {c.name.slice(0, 1)}
            </span>
            <span>
              <strong>{c.name}</strong>
              <small>
                {storyCounts[c.id]
                  ? t.dashboard.sidebar.storyCount(storyCounts[c.id])
                  : t.dashboard.sidebar.noStoriesYet}
              </small>
            </span>
            <ArrowRight aria-hidden="true" />
          </button>
        ))}

        {childPageCount > 1 ? (
          <nav className="sidebar-paging" aria-label={t.dashboard.sidebar.pagingLabel}>
            <button
              type="button"
              onClick={() => setChildPage((p) => Math.max(1, p - 1))}
              disabled={childPage === 1}
            >
              {t.common.actions.previous}
            </button>
            <span>{t.dashboard.library.pageOf(childPage, childPageCount)}</span>
            <button
              type="button"
              onClick={() => setChildPage((p) => Math.min(childPageCount, p + 1))}
              disabled={childPage === childPageCount}
            >
              {t.common.actions.next}
            </button>
          </nav>
        ) : null}

        <div className="sidebar-foot">
          {/* A new book for the child already selected — the common case. */}
          {characterId ? (
            <Link
              className="sidebar-new-book"
              to={newParts.to}
              search={newParts.search}
              hash={newParts.hash}
            >
              <Sparkles aria-hidden="true" />
              {t.dashboard.sidebar.newBook}
            </Link>
          ) : null}

          {/* Adding a child is a different intention, and must begin genuinely blank. */}
          <Link
            className="add-child"
            to="/create"
            search={{ new: "1" }}
            hash="profile"
            aria-label={t.dashboard.sidebar.addChild}
          >
            <Plus aria-hidden="true" />
            <span>{t.dashboard.sidebar.addChild}</span>
          </Link>
        </div>
      </aside>

      {!hasStories ? (
        <section className="dashboard-main empty-dashboard">
          <div className="empty-copy">
            <p className="eyebrow">
              <Sparkles aria-hidden="true" /> {t.dashboard.empty.title(heroName)}
            </p>
            <h1>{t.dashboard.empty.lead}</h1>
            <Link
              className="button button-primary"
              to={newParts.to}
              search={newParts.search}
              hash={newParts.hash}
            >
              {t.dashboard.empty.cta}
              <ArrowRight aria-hidden="true" />
            </Link>
            {/* One line, not a paragraph and two footnotes: the first book says the rest. */}
            <p className="empty-note">
              <Check aria-hidden="true" /> {t.dashboard.empty.trust[0]}
            </p>
          </div>

          <div className="empty-book-preview" aria-hidden="true">
            <span style={{ backgroundImage: `url("${WORLD_COVER_ART.magic}")` }} />
          </div>
        </section>
      ) : (
        <section className="dashboard-main dashboard-shelf">
          {drawing.map((pack) => (
            <DrawingBookCard key={pack.id} pack={pack} heroName={heroName} />
          ))}

          <div className="dashboard-section-heading" id="dashboard-library">
            <h2>{t.dashboard.library.heading(heroName)}</h2>
            <span>{t.dashboard.library.bookCount(shelfPacks.length)}</span>
          </div>

          {visiblePacks.length === 0 && drawing.length === 0 ? (
            // Silence here read as a bug: books existed, just under another child's name.
            <p className="shelf-other-child">{t.dashboard.library.otherChild(heroName)}</p>
          ) : null}

          <div className="book-library">
            {visiblePacks.map((pack) => (
              <LibraryBookCard
                key={pack.id}
                pack={pack}
                heroName={heroName}
                isRead={readBooks.has(pack.id)}
                printOrder={printByBook[pack.id]}
                onOrderPrint={() => {
                  setEditPrintOrderId(null);
                  setPrintBookId(pack.id);
                  setShipping(emptyShipping());
                  setPrintError(null);
                }}
                onEditPrintAddress={(order) => beginEditPrintAddress(order)}
              />
            ))}
          </div>

          {pageCount > 1 ? (
            <nav className="library-paging" aria-label={t.dashboard.library.pagingLabel}>
              <button
                type="button"
                onClick={() => setLibraryPage((p) => Math.max(1, p - 1))}
                disabled={libraryPage === 1}
              >
                {t.common.actions.previous}
              </button>
              <span>{t.dashboard.library.pageOf(libraryPage, pageCount)}</span>
              <button
                type="button"
                onClick={() => setLibraryPage((p) => Math.min(pageCount, p + 1))}
                disabled={libraryPage === pageCount}
              >
                {t.common.actions.next}
              </button>
            </nav>
          ) : null}

          {printBookId || editPrintOrderId ? (
            <PrintUpgradePanel
              mode={editPrintOrderId ? "edit" : "upgrade"}
              busy={printBusy}
              error={printError}
              shipping={shipping}
              onChange={(patch) => setShipping((prev) => ({ ...prev, ...patch }))}
              onCancel={() => {
                setPrintBookId(null);
                setEditPrintOrderId(null);
                setPrintError(null);
              }}
              onSubmit={() => void submitPrintForm()}
            />
          ) : null}

          {error ? <p className="dashboard-note is-error">{error}</p> : null}
          {loading ? <p className="dashboard-note">{t.common.states.loading}</p> : null}
        </section>
      )}
    </div>
  );
}

/**
 * A pack the pipeline is still drawing. "GeneratingPdf" is deliberately absent: that state
 * belongs to a finished book whose print file is being built on demand, and the card's own
 * PDF button already narrates it.
 */
function isPackGenerating(status: AdventurePackResponse["status"]): boolean {
  return status === "Pending" || status === "Generating" || status === "GeneratingStory";
}

/**
 * The book being drawn right now — the one thing on this page that changes while the parent
 * is looking at it, so it sits above everything that does not.
 */
function DrawingBookCard({ pack, heroName }: { pack: AdventurePackResponse; heroName: string }) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const cover = useIllustrationUrl(pack.coverImageUrl) ?? WORLD_COVER_ART[worldId];
  const title = pack.title?.trim() || world.bookTitle(heroName);
  const percent = typeof pack.progressPercent === "number" ? pack.progressPercent : null;

  return (
    <article className="dashboard-drawing" role="status" aria-live="polite">
      <span
        className="dashboard-drawing-cover"
        style={{ backgroundImage: `url("${cover}")` }}
        aria-hidden="true"
      />
      <div>
        <p>
          <i aria-hidden="true" />
          {title}
        </p>
        <small>{pack.progressMessage || t.dashboard.library.drawing}</small>
        <div className="dashboard-drawing-bar">
          <span style={{ width: `${percent ?? 0}%` }} />
        </div>
      </div>
      {percent !== null ? <strong>{percent}%</strong> : null}
    </article>
  );
}

function LibraryBookCard({
  pack,
  heroName,
  isRead,
  printOrder,
  onOrderPrint,
  onEditPrintAddress,
}: {
  pack: AdventurePackResponse;
  /** The child this shelf belongs to, so an untitled book still carries their name. */
  heroName: string;
  isRead: boolean;
  printOrder?: PrintOrderResponse;
  onOrderPrint: () => void;
  onEditPrintAddress: (order: PrintOrderResponse) => void;
}) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfBusy, setPdfBusy] = useState(false);
  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const cover = useIllustrationUrl(pack.coverImageUrl) ?? WORLD_COVER_ART[worldId];
  // An untitled book fell back to the generic "პატარა გმირი". The shelf is already
  // filtered to one child, so it can name them instead of describing them.
  const title = pack.title?.trim() || world.bookTitle(heroName);
  const hasPrint = pack.hasPrintEntitlement || !!printOrder;

  /*
    Read, download, print: the same three actions in the same order on every card. Once the
    story has been read the two free ones are spent and the printed copy is the only thing the
    card can still offer, so it takes the filled button and the first place in the row.
  */
  const promotePrint = isRead && !hasPrint;

  // This button could only download a PDF, never ask for one, so a finished book whose PDF was
  // never built showed a permanently disabled button and no reason why. Build it on demand.
  const handlePdf = async () => {
    setPdfError(null);
    setPdfBusy(true);
    try {
      if (!pack.pdfUrl) {
        // Queuing rejects a pack that is not StoryReady, so a build already under way is
        // joined rather than started again.
        if (pack.status !== "GeneratingPdf") {
          await generatePackPdf(pack.id);
        }
        await pollAdventurePack(pack.id, undefined, { untilPdfReady: true, maxAttempts: 90 });
      }
      await downloadAdventurePack(pack.id, `${title}.pdf`);
    } catch (err) {
      setPdfError(
        err instanceof ApiError ? err.message : (err as Error)?.message || "PDF ვერ მომზადდა.",
      );
    } finally {
      setPdfBusy(false);
    }
  };

  return (
    <article className="library-book">
      {/* The cover carries the title. Printing it again beside the cover was the one thing
          every card on this shelf said twice. */}
      <Link
        to="/reader/$bookId"
        params={{ bookId: pack.id }}
        className={`library-cover cover-${worldId === "space" ? "space" : "dino"}`}
        style={{ backgroundImage: `url("${cover}")`, backgroundSize: "cover" }}
        aria-label={t.dashboard.library.openBook(title)}
      >
        <strong>{title}</strong>
      </Link>
      <div>
        <small>
          {world.theme}
          {isRead ? ` · ${t.dashboard.library.readMark}` : ""}
        </small>

        <div className="library-actions">
          <Link
            className={`library-action ${promotePrint ? "" : "is-primary"}`}
            to="/reader/$bookId"
            params={{ bookId: pack.id }}
          >
            <BookOpen aria-hidden="true" />
            {isRead ? t.dashboard.library.readAgain : t.dashboard.library.read}
          </Link>

          <button
            className="library-action"
            type="button"
            onClick={() => void handlePdf()}
            disabled={pdfBusy}
          >
            <Download aria-hidden="true" />
            {pdfBusy ? t.dashboard.library.pdfBusy : "PDF"}
          </button>

          {!hasPrint ? (
            <button
              className={`library-action library-print ${promotePrint ? "is-primary is-promoted" : ""}`}
              type="button"
              onClick={onOrderPrint}
            >
              <Printer aria-hidden="true" />
              {t.dashboard.library.orderPrint(formatGel(PRICES.printUpgrade))}
            </button>
          ) : null}
        </div>

        {pdfError ? (
          <small className="library-error" role="alert">
            {pdfError}
          </small>
        ) : null}

        {hasPrint ? (
          <p className="library-print-status">
            <Package aria-hidden="true" />
            <strong>{printOrder?.statusLabel || t.dashboard.library.printOrdered}</strong>
            {printOrder?.canEditAddress ? (
              <>
                {" · "}
                <button type="button" onClick={() => onEditPrintAddress(printOrder)}>
                  {t.common.actions.change}
                </button>
              </>
            ) : null}
          </p>
        ) : null}
      </div>
    </article>
  );
}

function PrintUpgradePanel({
  mode,
  shipping,
  busy,
  error,
  onChange,
  onCancel,
  onSubmit,
}: {
  mode: "upgrade" | "edit";
  shipping: ShippingAddressRequest;
  busy: boolean;
  error: string | null;
  onChange: (patch: Partial<ShippingAddressRequest>) => void;
  onCancel: () => void;
  onSubmit: () => void;
}) {
  const t = useT();
  const heading =
    mode === "edit"
      ? "მიწოდების მისამართის განახლება"
      : `${t.dashboard.library.printEdition} · ${formatGel(PRICES.printUpgrade)}`;
  const submitLabel =
    mode === "edit"
      ? busy
        ? "…"
        : "მისამართის შენახვა"
      : busy
        ? "…"
        : `${t.journey.generated.orderPrint.trim()} · ${formatGel(PRICES.printUpgrade)}`;

  return (
    <section
      className="ux-package-panel"
      style={{ marginTop: 22, maxWidth: 560 }}
      aria-label={t.journey.checkout.shippingAddress}
    >
      <p className="eyebrow">
        <Package aria-hidden="true" /> {heading}
      </p>
      {/* What arrives and when, said once — instead of on every card down the shelf. */}
      <p className="print-panel-note">
        {t.dashboard.library.printDetail(DELIVERY_DAYS.tbilisi, DELIVERY_DAYS.regions)}
      </p>
      <fieldset className="choice-fieldset">
        <legend>{t.journey.checkout.shippingAddress}</legend>
        <div className="form-grid">
          <label className="field" htmlFor="dashboard-ship-recipient">
            <span>მიმღები</span>
            <input
              id="dashboard-ship-recipient"
              name="recipientName"
              autoComplete="name"
              value={shipping.recipientName}
              onChange={(e) => onChange({ recipientName: e.target.value })}
            />
          </label>
          <label className="field" htmlFor="dashboard-ship-phone">
            <span>{t.common.labels.phone}</span>
            <input
              id="dashboard-ship-phone"
              name="recipientPhone"
              type="tel"
              autoComplete="tel"
              value={shipping.recipientPhone}
              onChange={(e) => onChange({ recipientPhone: e.target.value })}
            />
          </label>
          <label className="field" htmlFor="dashboard-ship-city">
            <span>ქალაქი</span>
            <input
              id="dashboard-ship-city"
              name="city"
              autoComplete="address-level2"
              value={shipping.city}
              onChange={(e) => onChange({ city: e.target.value })}
            />
          </label>
          <label
            className="field"
            htmlFor="dashboard-ship-address"
            style={{ gridColumn: "1 / -1" }}
          >
            <span>მისამართი</span>
            <input
              id="dashboard-ship-address"
              name="addressLine1"
              autoComplete="street-address"
              value={shipping.addressLine1}
              onChange={(e) => onChange({ addressLine1: e.target.value })}
            />
          </label>
        </div>
      </fieldset>
      {error ? (
        <p className="eyebrow" style={{ color: "#f1c970", marginTop: 10 }}>
          {error}
        </p>
      ) : null}
      <div className="ux-generated-actions" style={{ marginTop: 14 }}>
        <button className="button button-quiet" type="button" onClick={onCancel} disabled={busy}>
          {t.common.actions.cancel}
        </button>
        <button
          className={mode === "edit" ? "button button-primary" : "button button-print-upgrade"}
          type="button"
          onClick={onSubmit}
          disabled={busy}
        >
          {submitLabel}
          {mode === "upgrade" ? <Plus aria-hidden="true" /> : null}
        </button>
      </div>
    </section>
  );
}
