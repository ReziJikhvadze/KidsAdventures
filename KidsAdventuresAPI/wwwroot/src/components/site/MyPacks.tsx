import { useCallback, useEffect, useMemo, useState } from "react";
import { format } from "date-fns";
import { Download, Loader2, Package, RefreshCw } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { AuthDialog } from "@/components/auth/AuthDialog";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { listChildren } from "@/lib/api/children";
import type { AdventurePackResponse, AdventurePackStatus } from "@/lib/api/types";

const statusStyles: Record<
  AdventurePackStatus,
  { label: string; className: string }
> = {
  Pending: { label: "Queued", className: "bg-amber-100 text-amber-900" },
  Generating: { label: "Creating…", className: "bg-sky-100 text-sky-900" },
  Completed: { label: "Ready", className: "bg-emerald-100 text-emerald-900" },
  Failed: { label: "Failed", className: "bg-red-100 text-red-900" },
};

export function MyPacks() {
  const { isAuthenticated } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [packs, setPacks] = useState<AdventurePackResponse[]>([]);
  const [childNames, setChildNames] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!isAuthenticated) return;
    setLoading(true);
    setError(null);
    try {
      const [packRows, children] = await Promise.all([
        adventurePacksApi.listAdventurePacks(),
        listChildren(),
      ]);
      setPacks(
        [...packRows].sort(
          (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
        ),
      );
      setChildNames(Object.fromEntries(children.map((c) => [c.id, c.name])));
    } catch {
      setError("Could not load your packs. Try again.");
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    void load();
  }, [load]);

  const hasInProgress = useMemo(
    () => packs.some((p) => p.status === "Pending" || p.status === "Generating"),
    [packs],
  );

  useEffect(() => {
    if (!isAuthenticated || !hasInProgress) return;
    const timer = window.setInterval(() => void load(), 4000);
    return () => window.clearInterval(timer);
  }, [isAuthenticated, hasInProgress, load]);

  const openDownload = (pack: AdventurePackResponse) => {
    if (pack.status !== "Completed") return;
    const url = pack.pdfUrl ?? adventurePacksApi.getDownloadUrl(pack.id);
    window.open(url, "_blank", "noopener,noreferrer");
  };

  return (
    <section id="my-packs" className="py-20 bg-secondary/30 border-y border-border/60">
      <div className="mx-auto max-w-5xl px-6">
        <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4 mb-8">
          <div>
            <p className="text-sm font-semibold text-primary uppercase tracking-wide">Your library</p>
            <h2 className="font-display text-3xl font-bold tracking-tight mt-1">My adventure packs</h2>
            <p className="text-muted-foreground mt-2 max-w-xl">
              Every pack you generate appears here. Download PDFs anytime while signed in.
            </p>
          </div>
          {isAuthenticated && (
            <button
              type="button"
              onClick={() => void load()}
              disabled={loading}
              className="inline-flex items-center justify-center gap-2 rounded-full border border-border bg-card px-4 py-2 text-sm font-semibold hover:bg-background transition disabled:opacity-50"
            >
              <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
              Refresh
            </button>
          )}
        </div>

        {!isAuthenticated ? (
          <div className="rounded-3xl border border-border bg-card p-10 text-center">
            <Package className="h-10 w-10 mx-auto text-muted-foreground mb-4" />
            <p className="text-muted-foreground mb-4">Sign in to see packs you have created.</p>
            <button
              type="button"
              onClick={() => setAuthOpen(true)}
              className="inline-flex items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
            >
              Sign in
            </button>
          </div>
        ) : loading && packs.length === 0 ? (
          <div className="flex items-center justify-center gap-2 py-16 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            Loading your packs…
          </div>
        ) : error ? (
          <div className="rounded-3xl border border-destructive/30 bg-destructive/5 p-6 text-center text-destructive">
            {error}
          </div>
        ) : packs.length === 0 ? (
          <div className="rounded-3xl border border-dashed border-border bg-card p-10 text-center">
            <p className="text-muted-foreground">No packs yet. Create your first adventure below.</p>
            <a
              href="#generator"
              className="inline-flex mt-4 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
            >
              Create a pack
            </a>
          </div>
        ) : (
          <ul className="grid gap-4">
            {packs.map((pack) => {
              const status = statusStyles[pack.status];
              const childName = childNames[pack.childId] ?? "Child";
              return (
                <li
                  key={pack.id}
                  className="rounded-2xl border border-border bg-card p-5 flex flex-col sm:flex-row sm:items-center gap-4"
                >
                  <div className="flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2 mb-1">
                      <span className={`text-xs font-bold px-2.5 py-0.5 rounded-full ${status.className}`}>
                        {status.label}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {format(new Date(pack.createdAt), "MMM d, yyyy · h:mm a")}
                      </span>
                    </div>
                    <p className="font-display text-lg font-semibold truncate">
                      {childName}&apos;s {pack.theme} adventure
                    </p>
                    {(pack.status === "Pending" || pack.status === "Generating") &&
                      pack.progressMessage && (
                        <p className="text-xs text-muted-foreground mt-1 line-clamp-2">
                          {pack.progressMessage}
                        </p>
                      )}
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    {pack.status === "Completed" ? (
                      <button
                        type="button"
                        onClick={() => openDownload(pack)}
                        className="inline-flex items-center gap-2 rounded-full bg-foreground text-background px-4 py-2.5 text-sm font-semibold hover:opacity-90 transition"
                      >
                        <Download className="h-4 w-4" />
                        Download PDF
                      </button>
                    ) : pack.status === "Failed" ? (
                      <a
                        href="#generator"
                        className="inline-flex items-center rounded-full border border-border px-4 py-2.5 text-sm font-semibold hover:bg-secondary transition"
                      >
                        Try again
                      </a>
                    ) : (
                      <span className="inline-flex items-center gap-2 text-sm text-muted-foreground px-2">
                        <Loader2 className="h-4 w-4 animate-spin" />
                        Working…
                      </span>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}

        {hasInProgress && (
          <p className="text-xs text-muted-foreground text-center mt-4">
            Packs in progress refresh automatically every few seconds.
          </p>
        )}
      </div>
      <AuthDialog open={authOpen} onOpenChange={setAuthOpen} />
    </section>
  );
}
