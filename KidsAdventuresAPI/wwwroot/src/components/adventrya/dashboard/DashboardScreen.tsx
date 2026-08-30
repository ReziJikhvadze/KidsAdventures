import {
  ArrowRight,
  BookOpen,
  Check,
  ChevronDown,
  Download,
  Loader2,
  Lock,
  Package,
  Plus,
  Printer,
} from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";
import { AppHeader } from "@/components/adventrya/AppHeader";
import { WorldSelectorStage } from "@/components/adventrya/journey/WorldSelectorStage";
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
  WorldNodeResponse,
} from "@/lib/api/types";
import { getAdventureMap, listAdventureMaps } from "@/lib/api/worlds";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { FLIGHT_ROUTES, ISLAND_SPOTS, SELECTOR_WORLDS } from "@/lib/journey/worldSelector";
import { useAuth } from "@/lib/auth/AuthContext";
import { useJourneyDraft } from "@/lib/journey/draft";
import { useIllustrationUrl } from "@/lib/hooks/useIllustrationUrl";
import { continueViaPickerHref, newBookHref } from "@/lib/continue";
import { formatGel, formatGelAmount, normalizeGeorgianPhone, useT } from "@/lib/i18n";
import { DELIVERY_DAYS, PRICES } from "@/lib/pricing";
import { SESSION_KEYS } from "@/lib/storage/session";
import { useWorldById, WORLD_COVER_ART, WORLD_IDS, isWorldId, type WorldId } from "@/lib/worlds";

/** Books per shelf page. Six fills the grid without a wall of illustrations to load. */
const LIBRARY_PAGE_SIZE = 6;

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

function isPackGenerating(status: AdventurePackResponse["status"]): boolean {
  return status === "Pending" || status === "Generating" || status === "GeneratingStory";
}

/**
 * A preview the parent walked away from.
 *
 * Leaving the creation screen keeps the run id on the device — that is what lets a return pick up
 * the same book rather than paying for a second one — but nothing outside `/create` used to look
 * at it, so walking away was a one-way door.
 */
function readPendingRunId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(SESSION_KEYS.pendingBookRunId);
  } catch {
    return null;
  }
}

/**
 * The parent's space: the shelf and the path, on one page.
 *
 * They were two screens. `/dashboard` held the books and `/world` held the map of six worlds, and
 * neither was complete on its own — the shelf could not say where a child was going, and the map
 * could not open, download or print the books it was counting. A family with one child and three
 * books had to hop between them to see one thing.
 *
 * The shape is `BEKI_Dashboard_Standalone_Prototype.html`: a cream page, the books as cards, and
 * one dark card at the foot carrying the world art with the six worlds along its bottom edge as a
 * track — the milestone a parent actually comes back for. `/world` redirects here.
 */
export function DashboardScreen({ celebrationBookId }: { celebrationBookId?: string } = {}) {
  const t = useT();
  const { isAuthenticated, isLoading: authLoading } = useAuth();
  const [characters, setCharacters] = useState<CharacterResponse[]>([]);
  const [characterId, setCharacterId] = useState<string | null>(null);
  const [packs, setPacks] = useState<AdventurePackResponse[]>([]);
  const [printOrders, setPrintOrders] = useState<PrintOrderResponse[]>([]);
  const [map, setMap] = useState<AdventureMapResponse | null>(null);
  const [mapsByCharacter, setMapsByCharacter] = useState<Record<string, AdventureMapResponse>>({});
  const [activeWorldId, setActiveWorldId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [printBookId, setPrintBookId] = useState<string | null>(null);
  const [editPrintOrderId, setEditPrintOrderId] = useState<string | null>(null);
  const [shipping, setShipping] = useState<ShippingAddressRequest>(emptyShipping);
  const [printBusy, setPrintBusy] = useState(false);
  const [printError, setPrintError] = useState<string | null>(null);
  const [pendingRunId] = useState<string | null>(readPendingRunId);
  const navigate = useNavigate();
  // The picker below writes the chosen world into the draft, exactly as it does at /themes.
  const [draft, setDraft] = useJourneyDraft();
  const initialCelebrationBookId = useRef(celebrationBookId);
  const consumedCelebrationBookId = useRef<string | null>(null);
  const WORLD_BY_ID = useWorldById();
  /*
    Signing in closes the dialog too, and a close is otherwise read as "no thanks, take me back".
    Without this flag a parent who signed in with Google was returned to the home page by the
    same handler that exists to catch the ones who changed their mind.
  */
  const signedInHere = useRef(false);

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
        const [chars, allPacks, prints, maps] = await Promise.all([
          listCharacters(),
          listAdventurePacks(),
          listPrintOrders().catch(() => [] as PrintOrderResponse[]),
          listAdventureMaps().catch(() => null),
        ]);
        if (cancelled) return;
        const kids = chars.filter((c) => c.characterType === "child" || c.isPrimary);
        const list = kids.length ? kids : chars;
        setCharacters(list);
        setPacks(allPacks);
        setPrintOrders(prints);

        if (maps) {
          const byCharacter: Record<string, AdventureMapResponse> = {};
          for (const candidate of maps) byCharacter[candidate.characterId] = candidate;
          setMapsByCharacter(byCharacter);
        }

        // The child whose book just arrived, ahead of the nominal primary. A parent lands
        // here straight from a purchase, and a page that opens on a different child's
        // empty shelf reads as the book not existing — which is exactly how it was reported.
        const newestPack = [...allPacks].sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
        const byNewestBook = newestPack?.primaryCharacterId
          ? list.find((c) => c.id === newestPack.primaryCharacterId)
          : null;
        let selected =
          (byNewestBook ?? list.find((c) => c.isPrimary) ?? list[0] ?? null)?.id ?? null;

        // A book handed over by the reader belongs to whichever child earned it, which is not
        // always the one whose shelf would otherwise open.
        const handoffBookId = initialCelebrationBookId.current;
        if (handoffBookId && maps) {
          const eligible = new Set(list.map((c) => c.id));
          const owner = maps.find(
            (candidate) =>
              eligible.has(candidate.characterId) &&
              candidate.worlds.some(
                (node) => node.state === "Completed" && node.bookId === handoffBookId,
              ),
          );
          selected = owner?.characterId ?? selected;
        }

        setCharacterId(selected);
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

  // The selected child's own path. Kept apart from the list above so switching child costs one
  // request rather than reloading the shelf underneath it.
  useEffect(() => {
    if (!characterId || !isAuthenticated) return;
    let cancelled = false;

    void getAdventureMap(characterId)
      .then((response) => {
        if (cancelled) return;
        setMap(response);
        setMapsByCharacter((prev) => ({ ...prev, [response.characterId]: response }));
        /*
          Open on somewhere the child can actually go. `nextWorldId` is a suggestion the server
          makes without checking it against this child's own progress, and a locked suggestion
          left the one button on the card greyed out.
        */
        const startable = (id: string | null | undefined) =>
          !!id &&
          response.worlds.some((w) => w.worldId === id && (w.canStart || w.state !== "Locked"));
        setActiveWorldId(
          (startable(response.nextWorldId) ? response.nextWorldId : null) ||
            response.worlds.find((w) => w.state === "Next")?.worldId ||
            response.worlds.find((w) => w.state === "Unlocked")?.worldId ||
            response.worlds.find((w) => w.state === "Completed")?.worldId ||
            response.worlds[0]?.worldId ||
            null,
        );
      })
      .catch(() => {
        /* The shelf is still worth showing without the path behind it. */
        if (!cancelled) setMap(null);
      });

    return () => {
      cancelled = true;
    };
  }, [characterId, isAuthenticated]);

  useEffect(() => {
    if (!celebrationBookId || !map || consumedCelebrationBookId.current === celebrationBookId) {
      return;
    }
    const completedNode = map.worlds.find(
      (node) => node.state === "Completed" && node.bookId === celebrationBookId,
    );
    if (!completedNode) return;

    consumedCelebrationBookId.current = celebrationBookId;
    setActiveWorldId(completedNode.worldId);
    // Keep the handoff ephemeral: preserving it would replay the celebration after a refresh.
    void navigate({ to: "/dashboard", search: { bookId: undefined }, replace: true });
  }, [celebrationBookId, map, navigate]);

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
  const heroName = map?.characterName || character?.name || t.common.fallbackHeroName;

  const childPacks = useMemo(
    () =>
      packs
        .filter((p) => !characterId || p.primaryCharacterId === characterId)
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
    [packs, characterId],
  );

  /*
    A book being drawn is not on the shelf yet.

    It has no cover to open, no PDF to download and nothing to print, so it appears once — as the
    card above the shelf, the only thing here that changes while a parent watches it — and joins
    the shelf as an ordinary book when the pipeline finishes.
  */
  const drawing = useMemo(() => childPacks.filter((p) => isPackGenerating(p.status)), [childPacks]);
  const shelfPacks = useMemo(
    () => childPacks.filter((p) => !isPackGenerating(p.status)),
    [childPacks],
  );

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

  const hrefParts = useCallback((href: string) => {
    const [pathAndQuery, hash] = href.split("#");
    const [to, query = ""] = (pathAndQuery || "/create").split("?");
    return {
      to: to || "/create",
      search: Object.fromEntries(new URLSearchParams(query).entries()),
      // No `|| "preview"` fallback. That default is what put every hash-less href back on the
      // preview stage, which mounts a billed generation on sight.
      hash: hash || undefined,
    };
  }, []);

  const nodesById = useMemo(() => {
    const dict: Record<string, WorldNodeResponse> = {};
    for (const node of map?.worlds ?? []) dict[node.worldId] = node;
    return dict;
  }, [map]);

  const activeNode = activeWorldId ? nodesById[activeWorldId] : undefined;
  const worldId = (
    activeWorldId && isWorldId(activeWorldId) ? activeWorldId : "dinosaurs"
  ) as WorldId;
  const world = WORLD_BY_ID[worldId];
  const completedCount = map?.completedCount ?? 0;
  const totalWorlds = map?.totalWorlds || map?.worlds.length || WORLD_IDS.length;

  /*
    One creation action, and it starts something new.

    Both calls to action — the one at the top of the page and the one on the path — go to the
    world picker. A single href used to serve them and resolved to a continuation for any family
    that already had books, which lands on the preview stage and starts a billed generation on
    sight. When the child has a path behind them it carries the prior book along, so the picker
    can shut the worlds they have not opened.
  */
  /*
    The button at the top opens the full-screen picker; the one on the map below is the picker's
    own, and starts the world it is attached to. Two doors to the same room, and neither guesses.
  */
  const newHref = useMemo(() => {
    if (!characterId) return newBookHref(null, { from: "dashboard" });
    if (!map || map.isFirstJourney) {
      // No path yet, so there is nothing to carry and nothing to skip.
      return newBookHref(characterId, { from: "dashboard" });
    }

    const continuation = map.continuation;
    const priorBookId =
      continuation?.fromBookId ??
      (activeNode?.state === "Completed" ? (activeNode.bookId ?? null) : null);

    const base = continueViaPickerHref({
      characterId,
      continuesFromBookId: priorBookId,
      characterIds: continuation?.carryForwardCharacters.map((c) => c.id) ?? [characterId],
    });

    return base;
  }, [characterId, map, activeNode]);

  const newParts = useMemo(() => hrefParts(newHref), [hrefParts, newHref]);

  /*
    Adding a child is a different intention, and it starts at the world too. It used to jump
    straight to `/create#profile`, which asked for a name, a birth date and a photograph before
    the parent had seen what any of it was for. `new` keeps the form blank.
  */
  const addChildParts = useMemo(
    () => hrefParts(newBookHref(null, { from: "dashboard", fresh: true })),
    [hrefParts],
  );

  /*
    Worlds this child has an actual book in.

    Read from the shelf rather than from the map's `Completed` state, because the question is
    narrower than the map's: `accessLevel === "Full"` is a book that was paid for and generated.
    A preview is a draft nobody has bought, and ticking its island would tell a parent they own
    something they do not.
  */
  const finishedWorldIds = useMemo(
    () =>
      Array.from(
        new Set(
          childPacks
            .filter(
              (p) =>
                p.accessLevel === "Full" &&
                !isPackGenerating(p.status) &&
                p.status !== "Failed" &&
                p.worldId &&
                isWorldId(p.worldId),
            )
            .map((p) => p.worldId as WorldId),
        ),
      ),
    [childPacks],
  );

  /* The child and the adventure the picker should carry into the questions. */
  const startSearch = useMemo(() => {
    const carried: Record<string, string> = { from: "dashboard" };
    if (characterId) carried.characterId = characterId;
    const continuation = map?.continuation;
    if (continuation?.fromBookId) carried.continuesFromBookId = continuation.fromBookId;
    const ids = continuation?.carryForwardCharacters.map((c) => c.id);
    if (ids?.length) carried.characterIds = ids.join(",");
    return carried;
  }, [characterId, map]);

  const printByBook = useMemo(() => {
    const dict: Record<string, PrintOrderResponse> = {};
    for (const order of printOrders) dict[order.bookId] = order;
    return dict;
  }, [printOrders]);

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
    if (!shipping.recipientName.trim() || !shipping.addressLine1.trim()) {
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
          city: shipping.addressLine1.trim(),
        });
        setEditPrintOrderId(null);
        setShipping(emptyShipping());
        setPrintOrders(await listPrintOrders());
        return;
      }

      if (!printBookId) return;
      const checkout = await createPrintUpgradeOrder({
        bookId: printBookId,
        // City mirrors the one address line, as on the checkout: the server requires it and
        // reads it for the Tbilisi delivery window, which is a substring match either way.
        shippingAddress: { ...shipping, recipientPhone: phone, city: shipping.addressLine1.trim() },
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

    What stood here was the page filled with an invented family, blurred out of reach, under a
    panel explaining that this was somebody else's household. The sign-in opens on arrival now;
    behind it is the shell, not a stranger's children.
  */
  if (!authLoading && !isAuthenticated) {
    return (
      <div className="journey-screen">
        <AppHeader backHref="/" />
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

  if (authLoading || loading) {
    return (
      <div className="journey-screen">
        <AppHeader backHref="/" />
        <div className="journey-wrap">
          <p className="journey-empty" role="status" aria-live="polite">
            {t.common.states.loading}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="journey-screen">
      <AppHeader backHref="/" />

      <main className="journey-wrap">
        <section className="journey-welcome">
          <div>
            <span className="journey-eyebrow">
              <Sparkle /> {t.story.world.spaceOf(heroName)}
            </span>
            <h1>{t.dashboard.library.heading(heroName)}</h1>
            <p>{t.story.world.shelfLead}</p>
          </div>
          <div className="journey-welcome-actions">
            {/*
              One child at a time, and a menu for the rest.

              The family was a row of faceless pills in the shelf's heading — small, unnamed by
              anything but text, and sitting in a section whose scope is the books while it in
              fact switched the whole page. This says whose space is open, wearing the face their
              own books drew, and keeps the switch one press away however many children there are.
            */}
            {character ? (
              <DropdownMenu>
                <DropdownMenuTrigger className="journey-child-switch">
                  <ChildAvatar name={character.name} portraitUrl={character.heroPortraitUrl} />
                  <span>
                    <small>{t.story.world.worldCount(completedCount)}</small>
                    <strong>{character.name}</strong>
                  </span>
                  <ChevronDown aria-hidden="true" />
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" className="journey-child-menu">
                  {characters.map((c) => (
                    <DropdownMenuItem
                      key={c.id}
                      onSelect={() => setCharacterId(c.id)}
                      data-on={c.id === characterId ? "" : undefined}
                    >
                      <ChildAvatar name={c.name} portraitUrl={c.heroPortraitUrl} />
                      <span>
                        <strong>{c.name}</strong>
                        <small>
                          {t.story.map.progress(
                            mapsByCharacter[c.id]?.completedCount ?? 0,
                            mapsByCharacter[c.id]?.totalWorlds || WORLD_IDS.length,
                          )}
                        </small>
                      </span>
                      {c.id === characterId ? <Check aria-hidden="true" /> : null}
                    </DropdownMenuItem>
                  ))}
                  <DropdownMenuSeparator />
                  <DropdownMenuItem asChild>
                    <Link
                      to={addChildParts.to}
                      search={addChildParts.search}
                      hash={addChildParts.hash}
                    >
                      <span className="journey-child-add" aria-hidden="true">
                        <Plus />
                      </span>
                      <span>
                        <strong>{t.dashboard.sidebar.addChild}</strong>
                      </span>
                    </Link>
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            ) : null}

            <Link
              className="journey-button journey-primary-button"
              to={newParts.to}
              search={newParts.search}
              hash={newParts.hash}
            >
              <Plus aria-hidden="true" />
              {t.dashboard.sidebar.newBook}
            </Link>
          </div>
        </section>

        <section aria-labelledby="dashboard-books-title">
          <div className="journey-section-head">
            <div>
              <h2 id="dashboard-books-title">{t.common.nav.books}</h2>
              <p>{t.dashboard.library.bookCount(shelfPacks.length)}</p>
            </div>
          </div>

          {/* A book still being written, and the way back into it. */}
          {pendingRunId ? (
            <div className="journey-resume">
              <span className="journey-resume-spark" aria-hidden="true">
                <Loader2 />
              </span>
              <div className="journey-resume-copy">
                <strong>{t.story.world.resumeTitle}</strong>
                <small>{t.story.world.resumeBody}</small>
              </div>
              <Link
                className="journey-button journey-resume-button"
                to="/create"
                hash="preview"
                search={{ resume: "1" }}
              >
                {t.story.world.resumeAction}
                <ArrowRight aria-hidden="true" />
              </Link>
            </div>
          ) : null}

          {drawing.map((pack) => (
            <DrawingCard key={pack.id} pack={pack} heroName={heroName} />
          ))}

          {visiblePacks.length > 0 ? (
            <div className="journey-books">
              {visiblePacks.map((pack) => (
                <BookCard
                  key={pack.id}
                  pack={pack}
                  heroName={heroName}
                  printOrder={printByBook[pack.id]}
                  onOrderPrint={() => {
                    setEditPrintOrderId(null);
                    setPrintBookId(pack.id);
                    setShipping(emptyShipping());
                    setPrintError(null);
                  }}
                  onEditPrintAddress={beginEditPrintAddress}
                />
              ))}
            </div>
          ) : drawing.length === 0 ? (
            <p className="journey-empty">
              {characters.length > 0
                ? t.dashboard.library.otherChild(heroName)
                : t.dashboard.empty.lead}
            </p>
          ) : null}

          {pageCount > 1 ? (
            <nav className="journey-paging" aria-label={t.dashboard.library.pagingLabel}>
              <button
                className="journey-button journey-small-button journey-outline-button"
                type="button"
                onClick={() => setLibraryPage((p) => Math.max(1, p - 1))}
                disabled={libraryPage === 1}
              >
                {t.common.actions.previous}
              </button>
              <span>{t.dashboard.library.pageOf(libraryPage, pageCount)}</span>
              <button
                className="journey-button journey-small-button journey-outline-button"
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

          {error ? <p className="journey-note">{error}</p> : null}
        </section>

        {/*
          The path, on the same page as the shelf it explains.

          `/world` was a screen of its own and is now this section: a parent who wants to know
          which worlds are open no longer has to leave the books to find out, and the books no
          longer sit next to a number they cannot see the meaning of.
        */}
        <section className="journey-story" id="story-path" aria-labelledby="dashboard-path-title">
          <div className="journey-section-head journey-section-head-story">
            <div>
              <span className="journey-eyebrow journey-eyebrow-violet">
                <Sparkle /> STORY PATH
              </span>
              <h2 id="dashboard-path-title">
                {heroName}
                {t.story.world.journeySuffix}
              </h2>
              <p>{t.story.map.lead}</p>
            </div>
            <div
              className="journey-progress"
              aria-label={t.story.map.progress(completedCount, totalWorlds)}
            >
              <strong>{completedCount}</strong>
              {/* The handoff's separator. Without it the two numbers read as one. */}
              <span>/ {t.story.map.ofTotal(totalWorlds)}</span>
            </div>
          </div>

          {/*
            The picker itself, standing in the parent's space.

            Not a second map drawn to look like it: this is `/themes`' own component, so the
            island hotspots, the star that flies out of Beki's lantern when one is chosen, the
            light that lifts the chosen island out of the dimmer and the button that grows from
            its name are all the same behaviour, from the same file. A copy would have been two
            maps to keep in step, and the first thing to drift would have been where the islands
            are.

            Embedded, because the wordmark and the back arrow belong to a page, and this is a
            section of one. One thing is added and nothing else: a tick on the worlds this child
            has a finished book in.
          */}
          <div className="journey-selector">
            <WorldSelectorStage
              draft={draft}
              onChange={setDraft}
              embedded
              completedWorldIds={finishedWorldIds}
              startSearch={startSearch}
            />
          </div>
        </section>
      </main>
    </div>
  );
}

/** The handoff's own four-point star, drawn rather than typed: the eyebrows lead with it. */
function Sparkle() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" aria-hidden="true">
      <path d="M12 3v6M12 15v6M3 12h6M15 12h6" />
    </svg>
  );
}

/**
 * The book being drawn right now — the one thing on this page that changes while the parent is
 * looking at it, so it sits above the shelf rather than on it.
 */
function DrawingCard({ pack, heroName }: { pack: AdventurePackResponse; heroName: string }) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const title = pack.title?.trim() || world.bookTitle(heroName);
  const percent = typeof pack.progressPercent === "number" ? pack.progressPercent : null;

  return (
    <div className="journey-resume journey-resume-drawing" role="status" aria-live="polite">
      <span className="journey-resume-spark" aria-hidden="true">
        <Loader2 />
      </span>
      <div className="journey-resume-copy">
        <strong>{title}</strong>
        <small>{pack.progressMessage || t.dashboard.library.drawing}</small>
        <span className="journey-resume-bar" aria-hidden="true">
          <i style={{ width: `${percent ?? 0}%` }} />
        </span>
      </div>
      {percent !== null ? <strong className="journey-resume-percent">{percent}%</strong> : null}
    </div>
  );
}

/**
 * One book on the shelf: its cover, what format it is in, and the three things a parent does with
 * it — read it, take the PDF away, or turn it into a printed copy.
 */
function BookCard({
  pack,
  heroName,
  printOrder,
  onOrderPrint,
  onEditPrintAddress,
}: {
  pack: AdventurePackResponse;
  heroName: string;
  printOrder?: PrintOrderResponse;
  onOrderPrint: () => void;
  onEditPrintAddress: (order: PrintOrderResponse) => void;
}) {
  const WORLD_BY_ID = useWorldById();
  const t = useT();
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfBusy, setPdfBusy] = useState(false);
  /*
    What the build is doing, while it does it.

    Making a PDF of a sixteen-page book takes long enough that a button which only greys out
    reads as a button that did nothing. `pollAdventurePack` has always reported progress through
    its second argument; this card passed `undefined` and threw the answer away.
  */
  const [pdfProgress, setPdfProgress] = useState<{
    percent: number | null;
    message: string | null;
  }>({ percent: null, message: null });

  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const cover = useIllustrationUrl(pack.coverImageUrl) ?? WORLD_COVER_ART[worldId];
  const title = pack.title?.trim() || world.bookTitle(heroName);
  const hasPrint = pack.hasPrintEntitlement || !!printOrder;

  // Built on demand: a finished book whose PDF was never generated used to show a dead button.
  const handlePdf = useCallback(async () => {
    setPdfError(null);
    setPdfBusy(true);
    setPdfProgress({ percent: null, message: null });
    try {
      if (!pack.pdfUrl) {
        if (pack.status !== "GeneratingPdf") await generatePackPdf(pack.id);
        await pollAdventurePack(
          pack.id,
          (fresh) =>
            setPdfProgress({
              percent: typeof fresh.progressPercent === "number" ? fresh.progressPercent : null,
              message: fresh.progressMessage ?? null,
            }),
          { untilPdfReady: true, maxAttempts: 90 },
        );
      }
      await downloadAdventurePack(pack.id, `${title}.pdf`);
    } catch (err) {
      setPdfError(
        err instanceof ApiError ? err.message : (err as Error)?.message || "PDF ვერ მომზადდა.",
      );
    } finally {
      setPdfBusy(false);
      setPdfProgress({ percent: null, message: null });
    }
  }, [pack.id, pack.pdfUrl, pack.status, title]);

  /*
    Built from the month names this product already ships, not from `toLocaleDateString`. `ka-GE`
    came back "August 21, 2026" here: the locale is only as good as the browser's ICU data, and a
    Georgian page printing an English month is worse than no date.
  */
  const created = new Date(pack.createdAt);
  const dateLabel = Number.isNaN(created.getTime())
    ? null
    : `${created.getDate()} ${t.common.date.months[created.getMonth()]}, ${created.getFullYear()}`;

  return (
    <article className="journey-book">
      <Link
        className="journey-cover"
        to="/reader/$bookId"
        params={{ bookId: pack.id }}
        style={{ backgroundImage: `url("${cover}")` }}
        aria-label={t.dashboard.library.openBook(title)}
      >
        <span className="journey-cover-brand">BEKI</span>
        <span className="journey-cover-title">{title}</span>
      </Link>

      <div className="journey-book-body">
        <div className="journey-book-top">
          <span
            className={`journey-badge ${hasPrint ? "journey-badge-printed" : "journey-badge-digital"}`}
          >
            {hasPrint ? <Printer aria-hidden="true" /> : <BookOpen aria-hidden="true" />}
            {hasPrint ? t.dashboard.library.statusPrinted : t.dashboard.library.statusCreated}
          </span>
          {dateLabel ? <span className="journey-book-date">{dateLabel}</span> : null}
        </div>

        <div>
          <h3>{title}</h3>
          <p>{world.theme}</p>
        </div>

        {printOrder ? (
          <div className="journey-delivery">
            <Check aria-hidden="true" />
            {printOrder.statusLabel || t.dashboard.library.printOrdered}
            {printOrder.canEditAddress ? (
              <button
                type="button"
                className="journey-delivery-edit"
                onClick={() => onEditPrintAddress(printOrder)}
              >
                {t.common.actions.change}
              </button>
            ) : null}
          </div>
        ) : null}

        <div className="journey-book-actions">
          <Link
            className="journey-button journey-small-button journey-outline-button"
            to="/reader/$bookId"
            params={{ bookId: pack.id }}
          >
            <BookOpen aria-hidden="true" />
            {t.dashboard.library.read}
          </Link>
          <button
            className="journey-button journey-small-button journey-outline-button"
            type="button"
            onClick={() => void handlePdf()}
            disabled={pdfBusy}
          >
            <Download aria-hidden="true" />
            {pdfBusy ? t.dashboard.library.pdfBusy : "PDF"}
          </button>
          {hasPrint ? null : (
            <button
              className="journey-button journey-small-button journey-gold-button"
              type="button"
              onClick={onOrderPrint}
            >
              <Printer aria-hidden="true" />
              {t.dashboard.library.orderPrint(formatGel(PRICES.printUpgrade))}
            </button>
          )}
        </div>

        {pdfBusy ? (
          <div className="journey-pdf-progress" role="status" aria-live="polite">
            <small>{pdfProgress.message || t.dashboard.library.pdfBusy}</small>
            <span
              className="journey-resume-bar"
              /* Indeterminate until the server names a number: a bar pinned at zero for a minute
                 says less than one that is visibly moving. */
              data-indeterminate={pdfProgress.percent === null ? "" : undefined}
            >
              <i
                style={
                  pdfProgress.percent === null ? undefined : { width: `${pdfProgress.percent}%` }
                }
              />
            </span>
          </div>
        ) : null}

        {pdfError ? (
          <p className="journey-book-error" role="alert">
            {pdfError}
          </p>
        ) : null}
      </div>
    </article>
  );
}

/**
 * The child as their books draw them, falling back to the initial of their name.
 *
 * Recovered from the sidebar this page used to have. The fallback has to survive the image
 * failing, not just the URL being absent: a stored portrait whose blob has gone still arrives as
 * a non-null URL, and rendering that alone leaves an empty tile — worse than the letter it
 * replaced, on the one control whose job is telling several children apart at a glance.
 */
function ChildAvatar({ name, portraitUrl }: { name: string; portraitUrl?: string | null }) {
  /* Through the same hook every other picture here uses: a portrait is stored as an `/api/…`
     path, and that endpoint wants the session token. */
  const resolved = useIllustrationUrl(portraitUrl);
  const [broken, setBroken] = useState(false);
  const showPortrait = Boolean(resolved) && !broken;

  return (
    <span className="journey-child-avatar" aria-hidden="true">
      {showPortrait ? (
        <img src={resolved ?? ""} alt="" loading="lazy" onError={() => setBroken(true)} />
      ) : (
        name.slice(0, 1)
      )}
    </span>
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
        : t.journey.checkout.pay(formatGelAmount(PRICES.printUpgrade));

  return (
    <section className="journey-print-panel" aria-label={t.journey.checkout.shippingAddress}>
      <p className="journey-print-heading">
        <Package aria-hidden="true" /> {heading}
      </p>
      {/* What arrives and when, said once — instead of on every card down the shelf. */}
      <p className="journey-print-note">
        {t.dashboard.library.printDetail(DELIVERY_DAYS.tbilisi, DELIVERY_DAYS.regions)}
      </p>

      <div className="journey-print-fields">
        <label className="journey-field" htmlFor="dashboard-ship-recipient">
          <span>{t.journey.checkout.recipient}</span>
          <input
            id="dashboard-ship-recipient"
            name="recipientName"
            autoComplete="name"
            value={shipping.recipientName}
            onChange={(e) => onChange({ recipientName: e.target.value })}
          />
        </label>
        <label className="journey-field" htmlFor="dashboard-ship-phone">
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
        <label className="journey-field journey-field-wide" htmlFor="dashboard-ship-address">
          <span>{t.journey.checkout.shippingAddress}</span>
          <input
            id="dashboard-ship-address"
            name="addressLine1"
            autoComplete="street-address"
            placeholder={t.journey.checkout.addressPlaceholder}
            value={shipping.addressLine1}
            onChange={(e) => onChange({ addressLine1: e.target.value })}
          />
        </label>
      </div>

      {error ? (
        <p className="journey-note" role="alert">
          {error}
        </p>
      ) : null}

      <div className="journey-print-actions">
        <button
          className="journey-button journey-small-button journey-outline-button"
          type="button"
          onClick={onCancel}
          disabled={busy}
        >
          {t.common.actions.cancel}
        </button>
        <button
          className="journey-button journey-small-button journey-gold-button"
          type="button"
          onClick={onSubmit}
          disabled={busy}
        >
          {submitLabel}
        </button>
      </div>
    </section>
  );
}
