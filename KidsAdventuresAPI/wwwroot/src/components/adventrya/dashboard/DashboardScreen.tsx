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
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "@tanstack/react-router";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { StoryPathMap } from "@/components/adventrya/world/StoryPathMap";
import { ApiError } from "@/lib/api/client";
import {
  downloadAdventurePack,
  listAdventurePacks,
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
import { continueHrefFromMap, firstJourneyHref } from "@/lib/continue";
import { formatGel, normalizeGeorgianPhone, t } from "@/lib/i18n";
import { PRICES } from "@/lib/pricing";
import { WORLD_BY_ID, WORLD_COVER_ART, isWorldId, type WorldId } from "@/lib/worlds";

const emptyShipping = (): ShippingAddressRequest => ({
  recipientName: "",
  recipientPhone: "",
  city: "",
  region: "",
  addressLine1: "",
  addressLine2: "",
  postalCode: "",
  notes: "",
  saveForLater: true,
});

export function DashboardScreen() {
  const navigate = useNavigate();
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

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      void navigate({ to: "/create", hash: "auth" });
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
        const primary = list.find((c) => c.isPrimary) ?? list[0] ?? null;
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
  }, [authLoading, isAuthenticated, navigate]);

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
        .sort((a, b) => (a.sequenceNumber ?? 0) - (b.sequenceNumber ?? 0) || a.createdAt.localeCompare(b.createdAt)),
    [packs, characterId],
  );
  const hasStories = childPacks.length > 0 || (map?.completedCount ?? 0) > 0;
  const activeNode = map?.worlds.find((w) => w.worldId === activeWorldId);
  const worldId = (activeWorldId && isWorldId(activeWorldId) ? activeWorldId : "dinosaurs") as WorldId;
  const world = WORLD_BY_ID[worldId];

  const actionHref = useMemo(() => {
    if (!characterId) return firstJourneyHref(worldId);
    if (!hasStories || map?.isFirstJourney) return firstJourneyHref(worldId, characterId);
    return continueHrefFromMap(map?.continuation, characterId, worldId);
  }, [characterId, hasStories, map, worldId]);

  const actionParts = useMemo(() => {
    const [pathAndQuery, hash] = actionHref.split("#");
    const [to, query = ""] = (pathAndQuery || "/create").split("?");
    return {
      to: to || "/create",
      search: Object.fromEntries(new URLSearchParams(query).entries()),
      hash: hash || "preview",
    };
  }, [actionHref]);

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
      region: order.region ?? "",
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

  if (!authLoading && !isAuthenticated) {
    return null;
  }

  return (
    <div className={`screen dashboard-shell ${hasStories ? "dashboard-shell-story-map" : ""}`}>
      <div className="dashboard-sky" aria-hidden="true" />
      <div className="grain" aria-hidden="true" />

      <AppHeader backHref="/" worldMode={hasStories} childName={heroName} />

      <aside className="dashboard-sidebar">
        <div className="parent-label">
          <Users aria-hidden="true" />
          <span>
            <small>Parent Dashboard</small>
            {user?.displayName?.trim() || t.common.nav.myFamily}
          </span>
        </div>

        <p className="sidebar-title">{t.dashboard.sidebar.parentLabel}</p>

        {characters.map((c) => (
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

        <Link className="add-child" to="/create" hash="profile">
          {t.dashboard.sidebar.addChild}
        </Link>

        <div className="sidebar-links">
          <Link to="/">
            <Home aria-hidden="true" />
            {t.dashboard.sidebar.links.home}
          </Link>
          <button type="button" className="active">
            <BookOpen aria-hidden="true" />
            {t.dashboard.sidebar.links.myBooks}
          </button>
          <Link to="/world">
            <Sparkles aria-hidden="true" />
            {t.common.nav.childWorld}
          </Link>
        </div>

        <p className="privacy-mini">{t.dashboard.sidebar.privacy}</p>
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
              to={actionParts.to}
              search={actionParts.search}
              hash={actionParts.hash}
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
              <span>
                <strong>{map?.completedCount ?? childPacks.length}</strong>
                {t.story.world.statBook}
              </span>
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
              <Link
                className="button button-primary"
                to={actionParts.to}
                search={actionParts.search}
                hash={actionParts.hash}
              >
                {activeNode?.state === "Completed"
                  ? t.story.world.continueFromMemory
                  : t.story.world.unlockNext}
                <ArrowRight aria-hidden="true" />
              </Link>
            </div>
          </div>

          <div className="dashboard-section-heading map-library-heading">
            <div>
              <h2>{t.dashboard.library.heading(heroName)}</h2>
              <p>{t.story.world.archiveNote}</p>
            </div>
            <Link to="/world">{t.common.actions.seeAll}</Link>
          </div>

          <div className="book-library">
            {childPacks.map((pack, index) => (
              <LibraryBookCard
                key={pack.id}
                pack={pack}
                index={index + 1}
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
  printOrder,
  onOrderPrint,
  onEditPrintAddress,
}: {
  pack: AdventurePackResponse;
  index: number;
  printOrder?: PrintOrderResponse;
  onOrderPrint: () => void;
  onEditPrintAddress: (order: PrintOrderResponse) => void;
}) {
  const worldId = pack.worldId && isWorldId(pack.worldId) ? pack.worldId : "dinosaurs";
  const world = WORLD_BY_ID[worldId];
  const cover = useIllustrationUrl(pack.coverImageUrl) ?? WORLD_COVER_ART[worldId];
  const title = pack.title?.trim() || world.bookTitle(t.common.fallbackHeroName);
  const hasPrint = pack.hasPrintEntitlement || !!printOrder;
  const format = hasPrint
    ? t.dashboard.library.formatBoth
    : t.dashboard.library.formatDigital;

  return (
    <article className="library-book">
      <div
        className={`library-cover cover-${worldId === "space" ? "space" : "dino"}`}
        style={{ backgroundImage: `url("${cover}")`, backgroundSize: "cover" }}
      >
        <span>{t.dashboard.library.bookIndex(index)}</span>
        <strong>{title}</strong>
      </div>
      <div>
        <small>
          {world.theme} · {format}
        </small>
        <h3>{title}</h3>
        <div>
          <Link to="/reader/$bookId" params={{ bookId: pack.id }}>
            Online Reader
          </Link>
          <button
            type="button"
            onClick={() => void downloadAdventurePack(pack.id, `${title}.pdf`).catch(() => undefined)}
            disabled={!pack.pdfUrl}
          >
            <Download aria-hidden="true" /> PDF
          </button>
        </div>
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
          <label className="field">
            <span>მიმღები</span>
            <input
              value={shipping.recipientName}
              onChange={(e) => onChange({ recipientName: e.target.value })}
            />
          </label>
          <label className="field">
            <span>{t.common.labels.phone}</span>
            <input
              value={shipping.recipientPhone}
              onChange={(e) => onChange({ recipientPhone: e.target.value })}
            />
          </label>
          <label className="field">
            <span>ქალაქი</span>
            <input value={shipping.city} onChange={(e) => onChange({ city: e.target.value })} />
          </label>
          <label className="field">
            <span>რეგიონი</span>
            <input
              value={shipping.region ?? ""}
              onChange={(e) => onChange({ region: e.target.value })}
            />
          </label>
          <label className="field" style={{ gridColumn: "1 / -1" }}>
            <span>მისამართი</span>
            <input
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
