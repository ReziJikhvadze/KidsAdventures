import { useEffect, useRef, useState } from "react";
import { Link } from "@tanstack/react-router";
import { useAuth } from "@/lib/auth/AuthContext";
import { ApiError } from "@/lib/api/client";
import { notify } from "@/lib/ui/notify";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { createChild } from "@/lib/api/children";
import { getToken } from "@/lib/api/client";
import { createFamilyMember } from "@/lib/api/family-members";
import type { PreviewIllustrationStatus, ThemeType } from "@/lib/api/types";
import { THEME_ID_TO_API } from "@/lib/api/types";
import { STORY_THEMES, isStoryThemeId, type StoryThemeId } from "@/lib/themes";
import { StoryBookReader } from "@/components/story/StoryBookReader";
import { PhotoPickerActions } from "@/components/ui/PhotoPickerActions";
import { dataUrlToFile } from "@/lib/api/utils";
import { AuthDialog } from "@/components/auth/AuthDialog";
import {
  Sparkles,
  Loader2,
  X,
  Check,
  Plane,
  Bone,
  Rocket,
  Ship,
  PawPrint,
  Camera,
  User,
  Crown,
  MapPin,
  Compass,
  Star,
  Wand2,
  Telescope,
  Sword,
  Shield,
  Heart,
  Brain,
  Users,
  Gift,
  ChevronDown,
} from "lucide-react";

const THEME_ICONS = {
  airplanes: Plane,
  dinosaurs: Bone,
  space: Rocket,
  pirates: Ship,
  animals: PawPrint,
} as const;

type ThemeId = StoryThemeId;

type GeneratorProps = {
  initialTheme?: StoryThemeId | null;
};

const relationOptions = [
  { value: "mom", label: "Mom" },
  { value: "dad", label: "Dad" },
  { value: "grandma", label: "Grandma" },
  { value: "grandpa", label: "Grandpa" },
  { value: "brother", label: "Brother" },
  { value: "sister", label: "Sister" },
  { value: "sibling", label: "Sibling" },
  { value: "friend", label: "Friend" },
] as const;

type Relation = (typeof relationOptions)[number]["value"];

const parentRoles = [
  { value: "captain", label: "The Captain", icon: Crown },
  { value: "navigator", label: "Chief Navigator", icon: Compass },
  { value: "guardian", label: "The Guardian", icon: Shield },
  { value: "explorer", label: "Lead Explorer", icon: MapPin },
];
const grandRoles = [
  { value: "wise", label: "The Wise One", icon: Brain },
  { value: "storykeeper", label: "Story Keeper", icon: Star },
  { value: "healer", label: "The Healer", icon: Heart },
  { value: "navigator", label: "Chief Navigator", icon: Compass },
];
const siblingRoles = [
  { value: "sidekick", label: "Co-Adventurer", icon: Sword },
  { value: "scout", label: "The Scout", icon: Telescope },
  { value: "pilot", label: "The Pilot", icon: Plane },
  { value: "engineer", label: "The Engineer", icon: Wand2 },
];

const roleOptions: Record<Relation, { value: string; label: string; icon: typeof Crown }[]> = {
  mom: parentRoles,
  dad: parentRoles,
  grandma: grandRoles,
  grandpa: grandRoles,
  brother: siblingRoles,
  sister: siblingRoles,
  sibling: siblingRoles,
  friend: siblingRoles,
};

const relationLabelMap: Record<Relation, string> = {
  mom: "Mom",
  dad: "Dad",
  grandma: "Grandma",
  grandpa: "Grandpa",
  brother: "Brother",
  sister: "Sister",
  sibling: "Sibling",
  friend: "Friend",
};

type Member = {
  id: string;
  photo: string;
  relation: Relation | "";
  role: string;
};

const MAX_MEMBERS = 6;

/** Family cast sends extra reference photos to OpenAI per page — disabled to reduce cost. */
const ENABLE_STORY_CAST = false;

const storyLanguages = [
  { value: "en", label: "English" },
  { value: "ka", label: "Georgian (ქართული)" },
  { value: "es", label: "Spanish (Español)" },
] as const;

export function Generator({ initialTheme = null }: GeneratorProps) {
  const [name, setName] = useState("");
  const [age, setAge] = useState<number | "">("");
  const [childPhoto, setChildPhoto] = useState<string | null>(null);
  const [optionalNotes, setOptionalNotes] = useState("");
  const [storyLanguage, setStoryLanguage] = useState<(typeof storyLanguages)[number]["value"]>("en");
  const [theme, setTheme] = useState<ThemeId | null>(initialTheme);

  useEffect(() => {
    if (initialTheme && isStoryThemeId(initialTheme)) {
      setTheme(initialTheme);
    }
  }, [initialTheme]);
  const [members, setMembers] = useState<Member[]>([]);
  const [showOptional, setShowOptional] = useState(false);
  const [status, setStatus] = useState<
    "idle" | "generatingStory" | "storyReady" | "generatingPdf" | "done" | "error"
  >("idle");
  const [progress, setProgress] = useState(0);
  const [progressMessage, setProgressMessage] = useState<string | null>(null);
  const [completedPackId, setCompletedPackId] = useState<string | null>(null);
  const [storyTitle, setStoryTitle] = useState<string | null>(null);
  const [storyPages, setStoryPages] = useState<
    { title: string; content: string; illustrationUrl?: string | null; isIllustrated?: boolean }[]
  >([]);
  const [packTheme, setPackTheme] = useState<ThemeType | null>(null);
  const [previewIllustrationStatus, setPreviewIllustrationStatus] =
    useState<PreviewIllustrationStatus>("None");
  const [downloading, setDownloading] = useState(false);
  const [startingPdf, setStartingPdf] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isWelcomeGiftStory, setIsWelcomeGiftStory] = useState(false);
  const [authOpen, setAuthOpen] = useState(false);
  const { isAuthenticated, isLoading, canCreatePdf, refreshAccountBalance, setBookCredits, user } =
    useAuth();
  const previewRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (status === "generatingStory") {
      previewRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }, [status]);

  const ageValid = typeof age === "number" && !Number.isNaN(age) && age >= 3 && age <= 12;

  const missingRequirements: string[] = [];
  if (!name.trim()) missingRequirements.push("child's name");
  if (age === "" || (typeof age === "number" && Number.isNaN(age))) {
    missingRequirements.push("age (3–12)");
  } else if (!ageValid) {
    missingRequirements.push("age between 3 and 12");
  }
  if (!theme) missingRequirements.push("a theme");

  const valid = missingRequirements.length === 0;

  const addMemberFromFile = (file: File | undefined) => {
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) return;
    if (members.length >= MAX_MEMBERS) return;
    const reader = new FileReader();
    reader.onload = () => {
      setMembers((prev) => [
        ...prev,
        {
          id: `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
          photo: reader.result as string,
          relation: "",
          role: "",
        },
      ]);
    };
    reader.readAsDataURL(file);
  };

  const updateMember = (id: string, patch: Partial<Member>) => {
    setMembers((prev) => prev.map((m) => (m.id === id ? { ...m, ...patch } : m)));
  };

  const removeMember = (id: string) => {
    setMembers((prev) => prev.filter((m) => m.id !== id));
  };

  const addHeroPhoto = (file: File | undefined) => {
    if (!file || file.size > 5 * 1024 * 1024) return;
    const reader = new FileReader();
    reader.onload = () => setChildPhoto(reader.result as string);
    reader.readAsDataURL(file);
  };

  const completeCast = members
    .map((m) => {
      if (!m.relation || !m.role) return null;
      const role = roleOptions[m.relation].find((r) => r.value === m.role);
      if (!role) return null;
      return { ...m, relation: m.relation as Relation, roleObj: role };
    })
    .filter(Boolean) as Array<
    Member & { relation: Relation; roleObj: { value: string; label: string; icon: typeof Crown } }
  >;

  const runGeneration = async () => {
    if (!valid || !theme) return;

    setStatus("generatingStory");
    setProgress(5);
    setProgressMessage("Saving your hero…");
    setErrorMessage(null);
    setCompletedPackId(null);
    setStoryTitle(null);
    setStoryPages([]);
    setIsWelcomeGiftStory((user?.welcomeStoryRemaining ?? 0) > 0);

    try {
      const apiTheme = THEME_ID_TO_API[theme];
      if (!apiTheme) throw new Error("Invalid theme selected.");

      setProgress(12);
      const heroFile =
        childPhoto?.startsWith("data:") ?
          dataUrlToFile(childPhoto, "hero")
        : undefined;
      const child = await createChild(name.trim(), age as number, heroFile);

      setProgress(20);
      if (ENABLE_STORY_CAST) {
        for (const member of completeCast) {
          const memberName = `${relationLabelMap[member.relation]} (${member.roleObj.label})`;
          const photoFile = member.photo.startsWith("data:")
            ? dataUrlToFile(member.photo, member.id)
            : undefined;
          await createFamilyMember({
            childId: child.id,
            name: memberName,
            relationship: relationLabelMap[member.relation],
            photoFile,
          });
        }
      }

      setProgress(28);
      setProgressMessage("Creating your illustrated storybook…");
      const queued = await adventurePacksApi.generateAdventurePack(child.id, apiTheme, {
        optionalStoryNotes: optionalNotes.trim() || undefined,
        storyLanguage,
      });
      void refreshAccountBalance();

      const finished = await adventurePacksApi.pollAdventurePack(
        queued.id,
        (pack) => {
          if (pack.progressMessage) {
            setProgressMessage(pack.progressMessage);
          }
          setProgress(adventurePacksApi.computePackProgressPercent(pack));
        },
        { untilReadable: true, maxAttempts: 300 },
      );

      setCompletedPackId(finished.id);
      setStoryTitle(finished.title ?? null);
      setStoryPages(finished.storyPages ?? []);
      setPackTheme(finished.theme);
      setPreviewIllustrationStatus(finished.previewIllustrationStatus ?? "Ready");
      setProgress(100);
      setStatus("storyReady");
      await refreshAccountBalance();
      notify.success("Your illustrated storybook is ready!", {
        description: "Swipe through every page below, then export a free PDF when you like.",
      });
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : "Failed to generate pack.";
      setErrorMessage(message);
      setStatus("error");
      notify.fromError(err, "Could not create your story.");
    }
  };

  const generate = () => {
    if (!valid) return;
    if (isLoading) {
      notify.info("Checking your sign-in…");
      return;
    }
    if (!isAuthenticated || !getToken()) {
      setAuthOpen(true);
      notify.error("Sign in to create a story", {
        description: "New accounts get one free 2-page welcome preview. Full 6-page books use book credits.",
      });
      return;
    }
    void runGeneration();
  };

  const runPdfGeneration = async () => {
    if (!completedPackId) return;
    setStartingPdf(true);
    setStatus("generatingPdf");
    setProgress(10);
    const slideshowReady =
      storyPages.length > 0 && storyPages.every((p) => p.isIllustrated);
    if (!slideshowReady) {
      notify.info("PDF not ready yet", {
        description: "Wait until all pages are illustrated, then export your free PDF.",
      });
      setStatus("storyReady");
      setStartingPdf(false);
      return;
    }

    setProgressMessage("Building PDF from your slideshow… ~30 seconds");
    try {
      const queued = await adventurePacksApi.generatePackPdf(completedPackId);
      if (typeof queued.bookCredits === "number") {
        setBookCredits(queued.bookCredits);
      } else {
        await refreshAccountBalance();
      }
      const completed = await adventurePacksApi.pollAdventurePack(
        completedPackId,
        (pack) => {
          if (pack.progressMessage) {
            setProgressMessage(pack.progressMessage);
            const pct = adventurePacksApi.parseProgressPercent(pack.progressMessage);
            if (pct !== null) setProgress(pct);
          }
        },
        {
          untilStatus: "Completed",
          maxAttempts: 30,
        },
      );
      setStoryPages(completed.storyPages ?? storyPages);
      setPreviewIllustrationStatus(completed.previewIllustrationStatus ?? "Ready");
      setProgress(100);
      setStatus("done");
      await refreshAccountBalance();
      notify.success("Your storybook PDF is ready!", {
        description: "Download it below or find it anytime in My Books.",
      });
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : "PDF creation failed.";
      setErrorMessage(message);
      setStatus("storyReady");
      notify.fromError(err, "PDF creation failed.");
    } finally {
      setStartingPdf(false);
    }
  };

  const reset = () => {
    setStatus("idle");
    setProgress(0);
    setProgressMessage(null);
    setCompletedPackId(null);
    setStoryTitle(null);
    setStoryPages([]);
    setPackTheme(null);
    setPreviewIllustrationStatus("None");
    setErrorMessage(null);
    setIsWelcomeGiftStory(false);
  };

  const selectedTheme = STORY_THEMES.find((t) => t.id === theme);

  return (
    <>
      <AuthDialog
        open={authOpen}
        onOpenChange={setAuthOpen}
        onSuccess={() => void runGeneration()}
      />
      <section id="generator" className="relative py-16 md:py-24 lg:py-32 scroll-mt-20">
        <div className="mx-auto max-w-7xl px-4 sm:px-6">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">
              Create your book
            </p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              A personalized story in minutes.
            </h2>
            <p className="mt-4 text-muted-foreground">
              Name, age, and theme — we write the story first. Add an illustrated PDF when you are
              ready.
            </p>
          </div>

          <div className="mt-12 grid lg:grid-cols-[1.1fr_1fr] gap-6 lg:gap-8 items-start">
            {/* Form */}
            <div className="rounded-3xl bg-card border border-border shadow-card p-4 sm:p-6 md:p-10">
              {/* Name + Age */}
              <div className="grid grid-cols-1 sm:grid-cols-[2fr_1fr] gap-4">
                <div>
                  <label className="text-sm font-semibold">Child's name</label>
                  <input
                    value={name}
                    maxLength={40}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Leo"
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                  />
                </div>
                <div>
                  <label className="text-sm font-semibold">Age</label>
                  <input
                    type="number"
                    min={3}
                    max={12}
                    value={age}
                    onChange={(e) => setAge(e.target.value ? Number(e.target.value) : "")}
                    placeholder="3–12"
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                  />
                </div>
              </div>

              {/* Theme */}
              <div className="mt-6">
                <label className="text-sm font-semibold">Choose a theme</label>
                <p className="mt-1 text-xs text-muted-foreground">
                  Airplanes, dinosaurs, space, pirates, or animals — pick one world for the whole
                  book.
                </p>
                <div className="mt-3 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-2">
                  {STORY_THEMES.map((t) => {
                    const active = theme === t.id;
                    const Icon = THEME_ICONS[t.id];
                    return (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => setTheme(t.id)}
                        className={`relative rounded-2xl border p-2.5 sm:p-3 flex flex-col items-center gap-2 transition ${
                          active
                            ? "border-primary bg-primary/5 ring-4 ring-primary/10"
                            : "border-border bg-card hover:border-foreground/30"
                        }`}
                      >
                        <span
                          className="h-9 w-9 sm:h-10 sm:w-10 rounded-xl grid place-items-center"
                          style={{ background: `color-mix(in oklab, ${t.tint} 55%, white)` }}
                        >
                          <Icon className="h-5 w-5 text-foreground" />
                        </span>
                        <span className="text-xs font-semibold">{t.name}</span>
                        {active && (
                          <span className="absolute -top-1.5 -right-1.5 h-5 w-5 rounded-full bg-primary text-primary-foreground grid place-items-center">
                            <Check className="h-3 w-3" />
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>
              </div>

              {/* Hero photo */}
              <div className="mt-6 rounded-2xl border border-border bg-secondary/30 p-4">
                <div className="flex flex-col sm:flex-row items-center sm:items-start gap-3 text-center sm:text-left">
                  {childPhoto ? (
                    <img
                      src={childPhoto}
                      alt={name || "Your child"}
                      className="h-16 w-16 rounded-xl object-cover border border-border shrink-0"
                    />
                  ) : (
                    <div className="h-16 w-16 rounded-xl bg-background border border-dashed border-border grid place-items-center shrink-0">
                      <Camera className="h-6 w-6 text-muted-foreground" />
                    </div>
                  )}
                  <div className="flex-1 min-w-0 w-full">
                    <div className="text-sm font-semibold">Photo of your child (hero)</div>
                    <p className="text-xs sm:text-sm text-muted-foreground mt-0.5">
                      Strongly recommended — we turn this into a cartoon hero that matches the
                      face, hair, skin tone, and age in your photo. Use a clear front-facing JPG or PNG
                      (not a screenshot). Friends and family should recognize them instantly.
                    </p>
                    <div className="mt-2 flex flex-wrap items-center justify-center sm:justify-start gap-2">
                      <PhotoPickerActions
                        hasPhoto={!!childPhoto}
                        onFileSelected={addHeroPhoto}
                      />
                      {childPhoto && (
                        <button
                          type="button"
                          onClick={() => setChildPhoto(null)}
                          className="text-xs text-muted-foreground hover:text-foreground underline"
                        >
                          Remove
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              </div>

              <button
                type="button"
                onClick={() => setShowOptional((v) => !v)}
                className="mt-6 w-full flex items-center justify-between rounded-xl border border-border bg-secondary/30 px-4 py-3 text-sm font-semibold hover:bg-secondary/50 transition"
              >
                Customize (optional)
                <ChevronDown
                  className={`h-4 w-4 transition ${showOptional ? "rotate-180" : ""}`}
                />
              </button>

              {showOptional && (
              <div className="mt-4 space-y-6 animate-rise">
              {ENABLE_STORY_CAST && (
              <div className="rounded-2xl bg-secondary/40 border border-border p-5">
                <div className="flex items-center justify-between gap-3 mb-4">
                  <div className="flex items-center gap-3">
                    <div className="h-8 w-8 rounded-lg bg-primary/10 grid place-items-center">
                      <Users className="h-4 w-4 text-primary" />
                    </div>
                    <div>
                      <div className="font-display font-semibold">
                        Story cast{" "}
                        <span className="font-normal text-muted-foreground">(optional)</span>
                      </div>
                      <div className="text-xs text-muted-foreground">
                        Add up to {MAX_MEMBERS} family members with photos — each upload is sent to
                        OpenAI to create a matching cartoon character in the illustrations.
                      </div>
                    </div>
                  </div>
                  <span className="text-xs text-muted-foreground shrink-0">
                    {members.length}/{MAX_MEMBERS}
                  </span>
                </div>

                {/* Member list */}
                {members.length > 0 && (
                  <ul className="space-y-3 mb-3">
                    {members.map((m) => {
                      const roles = m.relation ? roleOptions[m.relation] : [];
                      return (
                        <li
                          key={m.id}
                          className="rounded-xl bg-background border border-border p-3 animate-rise"
                        >
                          <div className="flex items-start gap-3">
                            <img
                              src={m.photo}
                              alt="Family member"
                              className="h-14 w-14 rounded-xl object-cover border border-border shrink-0"
                            />
                            <div className="flex-1 min-w-0 space-y-2">
                              <div>
                                <label className="text-[11px] font-semibold text-foreground/60 uppercase tracking-wide">
                                  Who is this?
                                </label>
                                <div className="mt-1 flex flex-wrap gap-1">
                                  {relationOptions.map((rel) => {
                                    const active = m.relation === rel.value;
                                    return (
                                      <button
                                        key={rel.value}
                                        type="button"
                                        onClick={() =>
                                          updateMember(m.id, { relation: rel.value, role: "" })
                                        }
                                        className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-medium transition ${
                                          active
                                            ? "bg-primary text-primary-foreground"
                                            : "bg-card border border-border hover:border-foreground/30"
                                        }`}
                                      >
                                        <User className="h-3 w-3" />
                                        {rel.label}
                                      </button>
                                    );
                                  })}
                                </div>
                              </div>

                              {m.relation && (
                                <div className="animate-rise">
                                  <label className="text-[11px] font-semibold text-foreground/60 uppercase tracking-wide">
                                    Role in the story
                                  </label>
                                  <div className="mt-1 flex flex-wrap gap-1">
                                    {roles.map((role) => {
                                      const active = m.role === role.value;
                                      return (
                                        <button
                                          key={role.value}
                                          type="button"
                                          onClick={() => updateMember(m.id, { role: role.value })}
                                          className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-medium transition ${
                                            active
                                              ? "bg-foreground text-background"
                                              : "bg-card border border-border hover:border-foreground/30"
                                          }`}
                                        >
                                          <role.icon className="h-3 w-3" />
                                          {role.label}
                                        </button>
                                      );
                                    })}
                                  </div>
                                </div>
                              )}
                            </div>
                            <button
                              type="button"
                              onClick={() => removeMember(m.id)}
                              className="h-7 w-7 rounded-full grid place-items-center text-muted-foreground hover:text-foreground hover:bg-secondary transition shrink-0"
                              aria-label="Remove member"
                            >
                              <X className="h-4 w-4" />
                            </button>
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                )}

                {/* Add another */}
                {members.length < MAX_MEMBERS ? (
                  <PhotoPickerActions
                    size="prominent"
                    hasPhoto={members.length > 0}
                    onFileSelected={addMemberFromFile}
                  />
                ) : (
                  <p className="text-xs text-center text-muted-foreground py-2">
                    You've reached the maximum cast size.
                  </p>
                )}
              </div>
              )}

              {/* Language + optional wishes */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="text-sm font-semibold">Story language</label>
                  <select
                    value={storyLanguage}
                    onChange={(e) =>
                      setStoryLanguage(e.target.value as (typeof storyLanguages)[number]["value"])
                    }
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                  >
                    {storyLanguages.map((lang) => (
                      <option key={lang.value} value={lang.value}>
                        {lang.label}
                      </option>
                    ))}
                  </select>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Georgian and Spanish work — GPT writes the full story in that language.
                  </p>
                </div>
                <div className="sm:col-span-1">
                  <label className="text-sm font-semibold">
                    Extra wishes{" "}
                    <span className="font-normal text-muted-foreground">(optional)</span>
                  </label>
                  <textarea
                    value={optionalNotes}
                    maxLength={500}
                    rows={3}
                    onChange={(e) => setOptionalNotes(e.target.value)}
                    placeholder="e.g. loves unicorns, afraid of loud noises, include little brother Niko"
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 text-sm outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition resize-none"
                  />
                  <p className="mt-1 text-xs text-muted-foreground">
                    Be specific — these wishes are woven into the story on multiple pages (not just the title).
                  </p>
                </div>
              </div>
              </div>
              )}

              {/* CTA */}
              <button
                onClick={generate}
                disabled={
                  !valid ||
                  status === "generatingStory" ||
                  status === "generatingPdf"
                }
                className="mt-8 w-full inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground py-4 font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:opacity-90 transition"
              >
                {status === "generatingStory" ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Writing your story…
                  </>
                ) : (
                  <>
                    <Sparkles className="h-4 w-4" />
                    Create story
                  </>
                )}
              </button>
              {!valid && status !== "generatingStory" && status !== "generatingPdf" && (
                <p className="mt-3 text-xs text-amber-700 dark:text-amber-400 text-center">
                  Still needed: {missingRequirements.join(" · ")}
                </p>
              )}
              <p className="mt-3 text-xs text-muted-foreground text-center">
                {user?.welcomeStoryRemaining
                  ? "Your first story is a free 2-page welcome preview. Full 6-page books use book credits."
                  : "Full 6-page stories use book credits · PDF export free · Hero photo optional"}
              </p>
            </div>

            {/* Preview / Result */}
            <div ref={previewRef} className="lg:sticky lg:top-24">
              <div className="relative rounded-3xl border border-border bg-secondary/40 p-4 sm:p-6 md:p-8 min-h-[240px] sm:min-h-[320px] lg:min-h-[460px] overflow-hidden">
                <div className="absolute inset-0 bg-hero-glow opacity-60 pointer-events-none" />

                {status === "idle" && (
                  <div className="relative h-full flex flex-col items-center justify-center text-center py-10 animate-rise">
                    <div className="h-40 w-32 rounded-xl bg-card border border-border shadow-card grid place-items-center font-display text-muted-foreground rotate-[-4deg]">
                      Preview
                    </div>
                    <p className="mt-6 text-sm text-muted-foreground max-w-xs">
                      Fill in your child's details to see a live preview of their adventure pack.
                    </p>
                    {ENABLE_STORY_CAST && completeCast.length > 0 && (
                      <div className="mt-5 w-full max-w-sm rounded-xl bg-card border border-border p-3 text-left animate-rise">
                        <div className="text-xs font-semibold text-foreground/60 uppercase tracking-wide mb-2">
                          Story cast ({completeCast.length})
                        </div>
                        <ul className="space-y-2">
                          {completeCast.map((c) => (
                            <li key={c.id} className="flex items-center gap-3">
                              <img
                                src={c.photo}
                                alt={relationLabelMap[c.relation]}
                                className="h-9 w-9 rounded-full object-cover border-2 border-primary/20"
                              />
                              <div className="min-w-0">
                                <div className="text-sm font-medium truncate">
                                  {relationLabelMap[c.relation]} →{" "}
                                  <span className="text-primary">{c.roleObj.label}</span>
                                </div>
                              </div>
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}

                {(status === "generatingStory" || status === "generatingPdf") && (
                  <div className="relative h-full flex flex-col items-center justify-center text-center py-6 sm:py-10 px-2 max-w-full">
                    <div className="relative">
                      <div className="h-40 w-32 rounded-xl bg-card border border-border shadow-card grid place-items-center overflow-hidden">
                        {childPhoto ? (
                          <img
                            src={childPhoto}
                            alt=""
                            className="h-full w-full object-cover"
                          />
                        ) : (
                          selectedTheme && (() => {
                            const ThemeIcon = THEME_ICONS[selectedTheme.id];
                            return (
                              <ThemeIcon className="h-12 w-12 text-primary animate-float" />
                            );
                          })()
                        )}
                      </div>
                      <div className="absolute -top-3 -right-3 h-10 w-10 rounded-full bg-primary text-primary-foreground grid place-items-center">
                        <Loader2 className="h-5 w-5 animate-spin" />
                      </div>
                    </div>
                    <div className="mt-8 w-full max-w-full sm:max-w-sm">
                      <div className="h-2 rounded-full bg-border overflow-hidden">
                        <div
                          className="h-full bg-primary transition-all duration-500"
                          style={{ width: `${Math.max(5, progress)}%` }}
                        />
                      </div>
                      <p className="mt-2 text-xs font-semibold text-primary tabular-nums">
                        {Math.round(progress)}%
                      </p>
                      <p className="mt-2 text-sm text-foreground font-medium min-h-[2.5rem]">
                        {progressMessage ??
                          (status === "generatingPdf"
                            ? "Creating illustrated PDF…"
                            : "Writing your story…")}
                      </p>
                      <div className="mt-4 rounded-xl bg-card/80 border border-border p-3 text-left text-xs text-muted-foreground space-y-2">
                        <p>
                          <strong className="text-foreground">You can leave this page.</strong>{" "}
                          We save every book under{" "}
                          <Link to="/my-packs" className="text-primary font-semibold underline">
                            My Books
                          </Link>
                          . We will email you when your illustrated story or PDF is ready.
                        </p>
                      </div>
                    </div>
                  </div>
                )}

                {(status === "storyReady" || status === "done") && selectedTheme && packTheme && (
                  <div className="relative animate-rise">
                    <p className="mb-3 text-center text-sm font-semibold text-foreground">
                      Read your story (free)
                    </p>
                    <StoryBookReader
                      pages={storyPages}
                      theme={packTheme}
                      title={storyTitle ?? `${name}'s ${selectedTheme.name} story`}
                      childName={name}
                      previewIllustrationStatus={previewIllustrationStatus}
                      isCompleted={status === "done"}
                      storiesRemainingThisMonth={user?.storiesRemainingThisMonth}
                      bookCredits={user?.bookCredits}
                      isWelcomeGiftStory={isWelcomeGiftStory}
                    />

                    <div className="mt-4 flex flex-col gap-2">
                      {status === "storyReady" && isAuthenticated && (
                        <button
                          type="button"
                          disabled={startingPdf}
                          onClick={() => void runPdfGeneration()}
                          className="w-full inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground py-3 font-semibold hover:opacity-90 transition disabled:opacity-60"
                        >
                          {startingPdf ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Sparkles className="h-4 w-4" />
                          )}
                          Export PDF — free (~30 sec)
                        </button>
                      )}
                      {status === "done" && completedPackId && (
                        <button
                          type="button"
                          disabled={downloading}
                          onClick={() => {
                            if (!completedPackId || !selectedTheme) return;
                            setDownloading(true);
                            const fileName = `${name}-${selectedTheme.name}-storybook.pdf`
                              .replace(/\s+/g, "-")
                              .toLowerCase();
                            void adventurePacksApi
                              .downloadAdventurePack(completedPackId, fileName)
                              .catch(() =>
                                notify.error("PDF download failed", {
                                  description: "Open My Books and try downloading again.",
                                }),
                              )
                              .finally(() => setDownloading(false));
                          }}
                          className="w-full inline-flex items-center justify-center gap-2 rounded-full bg-foreground text-background py-3 font-semibold hover:opacity-90 transition disabled:opacity-60"
                        >
                          {downloading ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Gift className="h-4 w-4" />
                          )}
                          Download storybook PDF
                        </button>
                      )}
                      <button
                        onClick={reset}
                        className="rounded-full bg-card border border-border px-4 py-3 font-semibold hover:bg-secondary transition"
                      >
                        New story
                      </button>
                    </div>
                  </div>
                )}

                {status === "error" && (
                  <div className="relative h-full flex flex-col items-center justify-center text-center py-10 px-4">
                    <p className="text-sm text-destructive font-medium">{errorMessage}</p>
                    <button
                      type="button"
                      onClick={reset}
                      className="mt-4 rounded-full bg-primary text-primary-foreground px-5 py-2 text-sm font-semibold"
                    >
                      Try again
                    </button>
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
