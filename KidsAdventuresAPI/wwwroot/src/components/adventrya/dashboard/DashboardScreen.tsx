import {
  ArrowRight,
  BookOpen,
  Check,
  Download,
  Home,
  Package,
  Plus,
  Sparkles,
  Users,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "@tanstack/react-router";
import { AppHeader } from "@/components/adventrya/AppHeader";
import { DashboardDemo } from "@/components/adventrya/dashboard/DashboardDemo";
import { StoryPathMap } from "@/components/adventrya/world/StoryPathMap";
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
  AdventureMapResponse,
  AdventurePackResponse,
  CharacterResponse,
  PrintOrderResponse,
  ShippingAddressRequest,
} from "@/lib/api/types";
import { getAdventureMap } from "@/lib/api/worlds";
import { useAuth } from "@/lib/auth/AuthContext";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { continueHrefFromMap, newBookHref } from "@/lib/continue";
import { formatGel, normalizeGeorgianPhone, useT } from "@/lib/i18n";
import { PRICES } from "@/lib/pricing";
import { useWorldById, WORLD_COVER_ART, isWorldId, type WorldId } from "@/lib/worlds";

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
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const { isAuthenticated, isLoading: authLoading, user } = useAuth();
  const [characters, setCharacters] = useState<CharacterResponse[]>([]);
  const [characterId, setCharacterId] = useState<string | null>(null);
  const [map, setMap] = useState<AdventureMapResponse | null>(null);
  const [packs, setPacks] = useState<AdventurePackResponse[]>([]);
  const [printOrders, setPrintOrders] = useState<PrintOrderResponse[]>([]);
  const [activeWorldId, setActiveWorldId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [printBookId, setPrintBookId] = useState<string | null>(null);
  const [editPrintOrderId, setEditPrintOrderId] = useState<string | null>(null);
  const [shipping, setShipping] = useState<ShippingAddressRequest>(emptyShipping);
  const [printBusy, setPrintBusy] = useState(false);
  const [printError, setPrintError] = useState<string | null>(null);
  const [authOpen, setAuthOpen] = useState(false);

  useEffect(() => {
    if (authLoading) return;
    // Signed out there is nothing to load and nothing to open: the preview below renders from
    // constants, and the auth dialog is now something a parent asks for rather than something
    // that lands on them.
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
          setError(err instanceof ApiError ? err.message : "Dashboard ვერ ჩაიტვირთა.");
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  useEffect(() => {
    if (!characterId || !isAuthenticated) {
      setMap(null);
      return;
    }
    let cancelled = false;
    void getAdventureMap(characterId)
      .then((response) => {
        if (cancelled) return;
        setMap(response);
        const next =
          response.nextWorldId ||
          response.worlds.find((w) => w.state === "Next")?.worldId ||
          response.worlds.find((w) => w.state === "Completed")?.worldId ||
          response.worlds[0]?.worldId ||
          null;
        setActiveWorldId(next);
      })
      .catch(() => {
        if (!cancelled) setMap(null);
      });
    return () => {
      cancelled = true;
    };
  }, [characterId, isAuthenticated]);

  const character = characters.find((c) => c.id === characterId) ?? null;
  const heroName = map?.characterName || character?.name || t.common.fallbackHeroName;
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
  const hasStories = childPacks.length > 0 || (map?.completedCount ?? 0) > 0;

  // The shelf is paged rather than infinite: a family buying a book a month has a wall
  // of covers by the second year, and every one of them loads an illustration.
  const [libraryPage, setLibraryPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(childPacks.length / LIBRARY_PAGE_SIZE));
  const visiblePacks = useMemo(
    () => childPacks.slice((libraryPage - 1) * LIBRARY_PAGE_SIZE, libraryPage * LIBRARY_PAGE_SIZE),
    [childPacks, libraryPage],
  );

  // Switching child leaves the old page number behind, which can land past the end.
  useEffect(() => {
    setLibraryPage(1);
  }, [childPacks.length]);

  const [childPage, setChildPage] = useState(1);
  const childPageCount = Math.max(1, Math.ceil(characters.length / SIDEBAR_PAGE_SIZE));
  const visibleChildren = useMemo(
    () => characters.slice((childPage - 1) * SIDEBAR_PAGE_SIZE, childPage * SIDEBAR_PAGE_SIZE),
    [characters, childPage],
  );
  const activeNode = map?.worlds.find((w) => w.worldId === activeWorldId);
  const worldId = (
    activeWorldId && isWorldId(activeWorldId) ? activeWorldId : "dinosaurs"
  ) as WorldId;
  const world = WORLD_BY_ID[worldId];

  /*
    Two actions, not one.

    A single href used to serve "create a new book", the empty state and the map's call to
    action, and it resolved to a continuation for any family that already had books — which
    lands on the preview stage and starts a billed generation on sight. Starting a book and
    carrying one on are different things and now say so: `newHref` opens the world picker,
    `continueHref` picks up where the last book left off.
  */
  const newHref = useMemo(() => newBookHref(characterId), [characterId]);

  const continueHref = useMemo(
    () => (characterId ? continueHrefFromMap(map?.continuation, characterId, worldId) : null),
    [characterId, map, worldId],
  );

  // A continuation only exists once there is a finished book to continue from.
  const canContinue = Boolean(continueHref && hasStories && !map?.isFirstJourney);

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
  const continueParts = useMemo(
    () => (continueHref ? hrefParts(continueHref) : null),
    [hrefParts, continueHref],
  );

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
    Signed out, the dashboard shows itself rather than a door.

    This was a bare panel — a heading, one line of Georgian and a sign-in button — with an
    effect that threw the auth dialog open the moment the screen mounted. A parent who had
    just signed out landed on a modal over an empty page, with nothing behind it to say what
    they had left or what signing back in would give them. The real layout is rendered here
    instead, filled with a sample family, blurred and genuinely out of reach; the gate on top
    says what it is. The dialog opens on the gate's button or on a click anywhere across the
    preview, so a parent who reaches for a control they cannot use is answered rather than
    ignored.
  */
  if (!authLoading && !isAuthenticated) {
    return (
      <div className="screen dashboard-shell dashboard-shell-story-map">
        <div className="dashboard-sky" aria-hidden="true" />
        <div className="grain" aria-hidden="true" />
        <AppHeader backHref="/" worldMode />

        {/* `inert` is the part that matters: it takes the whole subtree out of hit-testing, the
            tab order and the accessibility tree at once. A blur is only paint — without this
            every control under it stays tabbable and every heading stays readable aloud. */}
        <div className="dashboard-preview-layer" inert aria-hidden="true">
          <DashboardDemo />
        </div>

        {/* The catcher sits below the header's z-index, so the way back out of the screen and
            the language switcher stay live while everything under them asks for a sign-in. */}
        <div
          className="dashboard-preview-catch"
          aria-hidden="true"
          onClick={() => setAuthOpen(true)}
        />

        <div className="dashboard-preview-gate">
          <p className="eyebrow">
            <Sparkles aria-hidden="true" /> {t.dashboard.preview.label}
          </p>
          <h1>{t.dashboard.preview.title}</h1>
          <p>{t.dashboard.preview.body}</p>
          <button className="button button-primary" type="button" onClick={() => setAuthOpen(true)}>
            {t.dashboard.preview.cta}
            <ArrowRight aria-hidden="true" />
          </button>
        </div>

        <PasswordlessAuthDialog
          open={authOpen}
          onOpenChange={setAuthOpen}
          onSuccess={() => setAuthOpen(false)}
        />
      </div>
    );
  }

  return (
    <div className={`screen dashboard-shell ${hasStories ? "dashboard-shell-story-map" : ""}`}>
      <div className="dashboard-sky" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/" worldMode={hasStories} />

      <aside className="dashboard-sidebar">
        <div className="parent-label">
          <Users aria-hidden="true" />
          <span>
            <small>Parent Dashboard</small>
            {user?.displayName?.trim() || t.common.nav.myFamily}
          </span>
        </div>

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

        {/* A new book for the child already selected — the common case, and previously
            only reachable from the main panel. */}
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
        <Link className="add-child" to="/create" search={{ new: "1" }} hash="profile">
          <Plus aria-hidden="true" />
          {t.dashboard.sidebar.addChild}
        </Link>

        {/*
          The sidebar's nav and its privacy footnote are gone.

          One of the three links was a button that did not navigate and was permanently "active",
          one repeated the header's home link, and the third repeated the child's world. The
          privacy line was pinned to the bottom of a column that does not scroll, so a family
          with several children pushed it under their own names.
        */}
      </aside>

      {!hasStories ? (
        <section className="dashboard-main empty-dashboard">
          <div className="empty-world-book">
            <div className="empty-book-art" />
            <span className="empty-path" />
            <span className="empty-star star-one" />
            <span className="empty-star star-two" />
          </div>
          <div className="empty-copy">
            <p className="eyebrow">
              <Sparkles aria-hidden="true" /> {t.dashboard.empty.title(heroName)}
            </p>
            <h1>{t.dashboard.empty.lead}</h1>
            <p>{t.dashboard.empty.body(heroName)}</p>
            <Link
              className="button button-primary"
              to={newParts.to}
              search={newParts.search}
              hash={newParts.hash}
            >
              {t.dashboard.empty.cta}
              <ArrowRight aria-hidden="true" />
            </Link>
            <div className="empty-trust">
              {t.dashboard.empty.trust.map((line) => (
                <span key={line}>
                  <Check aria-hidden="true" /> {line}
                </span>
              ))}
            </div>
          </div>
        </section>
      ) : (
        <section className="dashboard-main map-first-dashboard">
          <div className="map-dashboard-heading">
            <div>
              <p className="eyebrow">
                <Sparkles aria-hidden="true" /> {t.story.world.welcomeBack.trim()}
              </p>
              <h1>
                {heroName}
                {t.story.world.titleSuffix}
              </h1>
              <p>{t.story.map.lead}</p>
            </div>
            <div className="map-dashboard-summary">
              {/* A button, because the number answered "how many" while the question a parent
                  actually arrives with is "where" — the shelf sits below the map, and nothing
                  above the fold said so. */}
              <button
                type="button"
                className="map-summary-books"
                onClick={() =>
                  document
                    .getElementById("dashboard-library")
                    ?.scrollIntoView({ behavior: "smooth", block: "start" })
                }
              >
                <strong>{map?.completedCount ?? childPacks.length}</strong>
                {t.story.world.statBook}
              </button>
              <span>
                <strong>{map?.completedCount ?? 0}</strong>
                {t.story.world.statMemory}
              </span>
              <span>
                <strong>{map?.totalWorlds ?? 6}</strong>
                {t.story.world.statWorld}
              </span>
            </div>
          </div>

          <div className="dashboard-map-frame">
            {map ? (
              <StoryPathMap
                map={map}
                activeWorldId={activeWorldId}
                onSelect={setActiveWorldId}
                compact
              />
            ) : null}

            <div className="dashboard-map-action">
              <div>
                <small>{world.chapter}</small>
                <strong>{activeNode?.bookTitle || world.mapTitle}</strong>
                <p>
                  {activeNode?.state === "Completed"
                    ? t.story.world.explanation
                    : activeNode?.state === "Next" || activeNode?.state === "Unlocked"
                      ? t.story.world.readyNote
                      : t.story.world.lockedNote}
                </p>
              </div>
              {/*
                The map's own action is a continuation — the next chapter of the adventure this
                map is showing. It falls back to the world picker only when there is nothing yet
                to continue from, which is also the only case where the label reads as a start.
              */}
              <Link
                className="button button-primary"
                to={canContinue && continueParts ? continueParts.to : newParts.to}
                search={canContinue && continueParts ? continueParts.search : newParts.search}
                hash={canContinue && continueParts ? continueParts.hash : newParts.hash}
              >
                {activeNode?.state === "Completed"
                  ? t.story.world.continueFromMemory
                  : t.story.world.unlockNext}
                <ArrowRight aria-hidden="true" />
              </Link>
            </div>
          </div>

          <div className="dashboard-section-heading map-library-heading" id="dashboard-library">
            <div>
              <h2>{t.dashboard.library.heading(heroName)}</h2>
              <p>{t.story.world.archiveNote}</p>
            </div>
            <Link to="/world">{t.common.actions.seeAll}</Link>
          </div>

          {visiblePacks.length === 0 ? (
            // Silence here read as a bug: books existed, just under another child's name.
            // Say whose shelf this is and where the books actually are.
            <p className="map-library-empty">
              {heroName}-ს ჯერ წიგნი არ აქვს. სხვა ბავშვის წიგნები მარცხენა სიაში მისი
              პროფილის არჩევით გამოჩნდება.
            </p>
          ) : null}

          <div className="book-library">
            {visiblePacks.map((pack, index) => (
              <LibraryBookCard
                key={pack.id}
                pack={pack}
                // Continuous across pages: book 7 stays book 7 on page two.
                index={(libraryPage - 1) * LIBRARY_PAGE_SIZE + index + 1}
                heroName={heroName}
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

          {error ? (
            <p className="eyebrow" style={{ marginTop: 16, color: "#f1c970" }}>
              {error}
            </p>
          ) : null}
          {loading ? (
            <p className="eyebrow" style={{ marginTop: 16, color: "#f8f2e5a8" }}>
              იტვირთება…
            </p>
          ) : null}
        </section>
      )}
    </div>
  );
}

function LibraryBookCard({
  pack,
  index,
  heroName,
  printOrder,
  onOrderPrint,
  onEditPrintAddress,
}: {
  pack: AdventurePackResponse;
  index: number;
  /** The child this shelf belongs to, so an untitled book still carries their name. */
  heroName: string;
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
  const format = hasPrint ? t.dashboard.library.formatBoth : t.dashboard.library.formatDigital;

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
      <Link
        to="/reader/$bookId"
        params={{ bookId: pack.id }}
        className={`library-cover cover-${worldId === "space" ? "space" : "dino"}`}
        style={{ backgroundImage: `url("${cover}")`, backgroundSize: "cover" }}
        aria-label={t.dashboard.library.openBook(title)}
      >
        <span>{t.dashboard.library.bookIndex(index)}</span>
        <strong>{title}</strong>
      </Link>
      <div>
        <small>
          {world.theme} · {format}
        </small>
        <h3>{title}</h3>
        <div>
          <Link to="/reader/$bookId" params={{ bookId: pack.id }}>
            Online Reader
          </Link>
          <button type="button" onClick={() => void handlePdf()} disabled={pdfBusy}>
            <Download aria-hidden="true" /> {pdfBusy ? "მზადდება…" : "PDF"}
          </button>
        </div>
        {pdfError ? <small role="alert">{pdfError}</small> : null}
        {hasPrint ? (
          <div className="library-print-status">
            <Package aria-hidden="true" />
            {printOrder?.statusLabel || t.dashboard.library.printOrdered}
            {printOrder?.canEditAddress ? (
              <button
                type="button"
                className="button button-quiet"
                style={{ marginLeft: 8 }}
                onClick={() => onEditPrintAddress(printOrder)}
              >
                მისამართის შეცვლა
              </button>
            ) : null}
          </div>
        ) : (
          <button className="library-print-upgrade" type="button" onClick={onOrderPrint}>
            {t.dashboard.library.orderPrint}
            <ArrowRight aria-hidden="true" />
          </button>
        )}
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
      : `${t.dashboard.library.orderPrint} · ${formatGel(PRICES.printUpgrade)}`;
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
