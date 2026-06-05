import { useRef, useState } from "react";
import { Link } from "@tanstack/react-router";
import { toast } from "sonner";

import { useAuth } from "@/lib/auth/AuthContext";
import { ApiError } from "@/lib/api/client";
import * as adventurePacksApi from "@/lib/api/adventure-packs";
import { createChild } from "@/lib/api/children";
import { createFamilyMember } from "@/lib/api/family-members";
import { THEME_ID_TO_API } from "@/lib/api/types";
import { dataUrlToFile } from "@/lib/api/utils";
import { AuthDialog } from "@/components/auth/AuthDialog";
import {
  Upload,
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
  Plus,
  Users,
  Cake,
  Gift,
  Map,
  Bird,
  PencilLine,
  MessageCircle,
} from "lucide-react";

const themes = [
  { id: "airplanes", name: "Airplanes", icon: Plane, tint: "var(--sky-soft)" },
  { id: "dinosaurs", name: "Dinosaurs", icon: Bone, tint: "var(--mint)" },
  { id: "space", name: "Space", icon: Rocket, tint: "var(--accent)" },
  { id: "pirates", name: "Pirates", icon: Ship, tint: "var(--sun)" },
  { id: "animals", name: "Animals", icon: PawPrint, tint: "var(--sun)" },
] as const;

type ThemeId = (typeof themes)[number]["id"];

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

const storyLanguages = [
  { value: "en", label: "English" },
  { value: "ka", label: "Georgian (ქართული)" },
  { value: "es", label: "Spanish (Español)" },
] as const;

export function Generator() {
  const [name, setName] = useState("");
  const [age, setAge] = useState<number | "">("");
  const [childPhoto, setChildPhoto] = useState<string | null>(null);
  const [optionalNotes, setOptionalNotes] = useState("");
  const [storyLanguage, setStoryLanguage] = useState<(typeof storyLanguages)[number]["value"]>("en");
  const [theme, setTheme] = useState<ThemeId | null>(null);
  const [members, setMembers] = useState<Member[]>([]);
  const [birthdayMode, setBirthdayMode] = useState(false);
  const [birthday, setBirthday] = useState("");
  const [status, setStatus] = useState<"idle" | "generating" | "done" | "error">("idle");
  const [progress, setProgress] = useState(0);
  const [progressMessage, setProgressMessage] = useState<string | null>(null);
  const [completedPackId, setCompletedPackId] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [authOpen, setAuthOpen] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const heroPhotoRef = useRef<HTMLInputElement>(null);
  const { isAuthenticated } = useAuth();

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

    setStatus("generating");
    setProgress(5);
    setProgressMessage("Saving your hero and cast…");
    setErrorMessage(null);
    setCompletedPackId(null);

    try {
      const apiTheme = THEME_ID_TO_API[theme];
      if (!apiTheme) throw new Error("Invalid theme selected.");

      setProgress(12);
      const heroFile =
        childPhoto?.startsWith("data:") ?
          dataUrlToFile(childPhoto, "hero.jpg")
        : undefined;
      const child = await createChild(name.trim(), age as number, heroFile);

      setProgress(20);
      for (const member of completeCast) {
        const memberName = `${relationLabelMap[member.relation]} (${member.roleObj.label})`;
        const photoFile = member.photo.startsWith("data:")
          ? dataUrlToFile(member.photo, `${member.id}.jpg`)
          : undefined;
        await createFamilyMember({
          childId: child.id,
          name: memberName,
          relationship: relationLabelMap[member.relation],
          photoFile,
        });
      }

      setProgress(28);
      setProgressMessage("Starting your adventure — this usually takes 3–8 minutes…");
      const queued = await adventurePacksApi.generateAdventurePack(child.id, apiTheme, {
        optionalStoryNotes: optionalNotes.trim() || undefined,
        storyLanguage,
      });

      const completed = await adventurePacksApi.pollAdventurePack(queued.id, (pack) => {
        if (pack.progressMessage) {
          setProgressMessage(pack.progressMessage);
          const pct = adventurePacksApi.parseProgressPercent(pack.progressMessage);
          if (pct !== null) setProgress(pct);
        }
        if (pack.status === "Generating") setProgress((p) => Math.min(95, p + 2));
        if (pack.status === "Pending") setProgress((p) => Math.min(25, p + 3));
      });

      setCompletedPackId(completed.id);
      setProgress(100);
      setStatus("done");
      toast.success("Your adventure pack is ready!");
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : "Failed to generate pack.";
      setErrorMessage(message);
      setStatus("error");
      toast.error(message);
    }
  };

  const generate = () => {
    if (!valid) return;
    if (!isAuthenticated) {
      setAuthOpen(true);
      return;
    }
    void runGeneration();
  };

  const reset = () => {
    setStatus("idle");
    setProgress(0);
    setProgressMessage(null);
    setCompletedPackId(null);
    setErrorMessage(null);
  };

  const selectedTheme = themes.find((t) => t.id === theme);

  return (
    <>
      <AuthDialog
        open={authOpen}
        onOpenChange={setAuthOpen}
        onSuccess={() => void runGeneration()}
      />
      <section id="generator" className="relative py-24 md:py-32 scroll-mt-20">
        <div className="mx-auto max-w-7xl px-6">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">
              Create your pack
            </p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              Build an adventure in 60 seconds.
            </h2>
            <p className="mt-4 text-muted-foreground">
              Fill in a few details and we'll personalize a printable adventure pack for your child.
            </p>
          </div>

          <div className="mt-12 grid lg:grid-cols-[1.1fr_1fr] gap-8 items-start">
            {/* Form */}
            <div className="rounded-3xl bg-card border border-border shadow-card p-6 md:p-10">
              {/* Family cast — multi-member */}
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
                        Add up to {MAX_MEMBERS} family members — skip this if you only want your
                        child in the story
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
                  <button
                    type="button"
                    onClick={() => fileRef.current?.click()}
                    className="w-full rounded-xl border-2 border-dashed border-border bg-background hover:border-primary hover:bg-primary/5 transition px-4 py-4 flex items-center justify-center gap-2 text-sm font-medium text-muted-foreground hover:text-foreground"
                  >
                    {members.length === 0 ? (
                      <>
                        <Upload className="h-4 w-4" />
                        Upload a photo
                        <span className="text-xs text-muted-foreground font-normal">
                          (optional)
                        </span>
                      </>
                    ) : (
                      <>
                        <Plus className="h-4 w-4" />
                        Add another family member
                      </>
                    )}
                  </button>
                ) : (
                  <p className="text-xs text-center text-muted-foreground py-2">
                    You've reached the maximum cast size.
                  </p>
                )}

                <input
                  ref={fileRef}
                  type="file"
                  accept="image/*"
                  className="hidden"
                  onChange={(e) => {
                    addMemberFromFile(e.target.files?.[0]);
                    if (fileRef.current) fileRef.current.value = "";
                  }}
                />
              </div>

              <div className="my-8 h-px bg-border" />

              {/* Name + Age */}
              <div className="grid sm:grid-cols-[2fr_1fr] gap-4">
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

              {/* Hero photo */}
              <div className="mt-6 rounded-2xl border border-border bg-secondary/30 p-4">
                <div className="flex items-start gap-3">
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
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-semibold">Photo of your child (hero)</div>
                    <p className="text-xs text-muted-foreground mt-0.5">
                      Strongly recommended — we send this photo directly to the AI on every page so
                      your child looks like themselves. Use a clear front-facing photo, good light,
                      face fills most of the frame, no sunglasses or heavy filters.
                    </p>
                    <p className="text-xs text-muted-foreground/80 mt-1">
                      Result is a polished animated illustration inspired by your child&apos;s real
                      face — closer to a movie character than a generic cartoon, but not a pasted
                      photo.
                    </p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      <button
                        type="button"
                        onClick={() => heroPhotoRef.current?.click()}
                        className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 text-primary px-3 py-1.5 text-xs font-semibold hover:bg-primary/15 transition"
                      >
                        <Upload className="h-3.5 w-3.5" />
                        {childPhoto ? "Change photo" : "Upload photo"}
                      </button>
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
                <input
                  ref={heroPhotoRef}
                  type="file"
                  accept="image/*"
                  className="hidden"
                  onChange={(e) => {
                    addHeroPhoto(e.target.files?.[0]);
                    if (heroPhotoRef.current) heroPhotoRef.current.value = "";
                  }}
                />
              </div>

              {/* Theme */}
              <div className="mt-6">
                <label className="text-sm font-semibold">Choose a theme</label>
                <p className="mt-1 text-xs text-muted-foreground">
                  Airplanes, dinosaurs, space, pirates, or animals — pick one world for the whole
                  pack.
                </p>
                <div className="mt-3 grid grid-cols-2 sm:grid-cols-5 gap-2">
                  {themes.map((t) => {
                    const active = theme === t.id;
                    return (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => setTheme(t.id)}
                        className={`relative rounded-2xl border p-3 flex flex-col items-center gap-2 transition ${
                          active
                            ? "border-primary bg-primary/5 ring-4 ring-primary/10"
                            : "border-border bg-card hover:border-foreground/30"
                        }`}
                      >
                        <span
                          className="h-10 w-10 rounded-xl grid place-items-center"
                          style={{ background: `color-mix(in oklab, ${t.tint} 55%, white)` }}
                        >
                          <t.icon className="h-5 w-5 text-foreground" />
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

              {/* Language + optional wishes */}
              <div className="mt-6 grid sm:grid-cols-2 gap-4">
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
                </div>
              </div>

              {/* Birthday Mode */}
              <div className="mt-6 rounded-2xl border border-border bg-secondary/40 p-4">
                <label className="flex items-start gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={birthdayMode}
                    onChange={(e) => setBirthdayMode(e.target.checked)}
                    className="mt-1 h-5 w-5 rounded border-border text-primary focus:ring-primary/30"
                  />
                  <div className="flex-1">
                    <div className="flex items-center gap-2 font-semibold text-sm">
                      <Cake className="h-4 w-4 text-primary" />
                      Birthday Mode
                      <span className="rounded-full bg-primary/10 text-primary px-2 py-0.5 text-[10px] font-bold uppercase">
                        Optional
                      </span>
                    </div>
                    <p className="text-xs text-muted-foreground mt-0.5">
                      Add a birthday certificate, party scavenger hunt and themed activities. Leave
                      off for a regular adventure pack.
                    </p>
                  </div>
                </label>
                {birthdayMode && (
                  <div className="mt-4 animate-rise">
                    <label className="text-xs font-semibold text-foreground/70 uppercase tracking-wide">
                      Birthday date <span className="normal-case font-normal">(optional)</span>
                    </label>
                    <input
                      type="date"
                      value={birthday}
                      onChange={(e) => setBirthday(e.target.value)}
                      className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                    />
                  </div>
                )}
              </div>

              {/* CTA */}
              <button
                onClick={generate}
                disabled={!valid || status === "generating"}
                className="mt-8 w-full inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground py-4 font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:opacity-90 transition"
              >
                {status === "generating" ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Generating your pack…
                  </>
                ) : (
                  <>
                    <Sparkles className="h-4 w-4" />
                    Create Adventure Pack
                  </>
                )}
              </button>
              {!valid && status !== "generating" && (
                <p className="mt-3 text-xs text-amber-700 dark:text-amber-400 text-center">
                  Still needed: {missingRequirements.join(" · ")}
                </p>
              )}
              <p className="mt-3 text-xs text-muted-foreground text-center">
                Free plan · No credit card required · Hero photo is optional
              </p>
            </div>

            {/* Preview / Result */}
            <div className="lg:sticky lg:top-24">
              <div className="relative rounded-3xl border border-border bg-secondary/40 p-8 min-h-[460px] overflow-hidden">
                <div className="absolute inset-0 bg-hero-glow opacity-60 pointer-events-none" />

                {status === "idle" && (
                  <div className="relative h-full flex flex-col items-center justify-center text-center py-10 animate-rise">
                    <div className="h-40 w-32 rounded-xl bg-card border border-border shadow-card grid place-items-center font-display text-muted-foreground rotate-[-4deg]">
                      Preview
                    </div>
                    <p className="mt-6 text-sm text-muted-foreground max-w-xs">
                      Fill in your child's details to see a live preview of their adventure pack.
                    </p>
                    {completeCast.length > 0 && (
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

                {status === "generating" && (
                  <div className="relative h-full flex flex-col items-center justify-center text-center py-10 px-2">
                    <div className="relative">
                      <div className="h-40 w-32 rounded-xl bg-card border border-border shadow-card grid place-items-center overflow-hidden">
                        {childPhoto ? (
                          <img
                            src={childPhoto}
                            alt=""
                            className="h-full w-full object-cover"
                          />
                        ) : (
                          selectedTheme && (
                            <selectedTheme.icon className="h-12 w-12 text-primary animate-float" />
                          )
                        )}
                      </div>
                      <div className="absolute -top-3 -right-3 h-10 w-10 rounded-full bg-primary text-primary-foreground grid place-items-center">
                        <Loader2 className="h-5 w-5 animate-spin" />
                      </div>
                    </div>
                    <div className="mt-8 w-full max-w-sm">
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
                        {progressMessage ?? "Creating your adventure…"}
                      </p>
                      <div className="mt-4 rounded-xl bg-card/80 border border-border p-3 text-left text-xs text-muted-foreground space-y-2">
                        <p>
                          <strong className="text-foreground">You can leave this page.</strong>{" "}
                          We save every pack under{" "}
                          <Link to="/my-packs" className="text-primary font-semibold underline">
                            My Packs
                          </Link>
                          . It often needs a few more minutes — refresh there when status is Ready.
                        </p>
                        <p>
                          Email when ready is coming soon; for now check My Packs or stay on this
                          screen.
                        </p>
                      </div>
                    </div>
                  </div>
                )}

                {status === "done" && selectedTheme && (
                  <div className="relative animate-rise">
                    <div className="rounded-2xl bg-card border border-border shadow-card overflow-hidden">
                      <div
                        className="p-6 flex items-center gap-4"
                        style={{
                          background: `color-mix(in oklab, ${selectedTheme.tint} 45%, white)`,
                        }}
                      >
                        {childPhoto ? (
                          <img
                            src={childPhoto}
                            alt={name}
                            className="h-16 w-16 rounded-full object-cover border-4 border-card shadow-soft"
                          />
                        ) : completeCast[0] ? (
                          <img
                            src={completeCast[0].photo}
                            alt={name}
                            className="h-16 w-16 rounded-full object-cover border-4 border-card shadow-soft"
                          />
                        ) : (
                          <div className="h-16 w-16 rounded-full bg-card grid place-items-center border-4 border-card shadow-soft">
                            <selectedTheme.icon className="h-7 w-7 text-foreground" />
                          </div>
                        )}
                        <div>
                          <div className="text-xs font-semibold uppercase tracking-wide text-foreground/60">
                            Adventure Pack
                          </div>
                          <div className="font-display text-2xl font-bold leading-tight">
                            {name}'s {selectedTheme.name} Quest
                          </div>
                        </div>
                      </div>

                      {/* Story cast */}
                      {completeCast.length > 0 && (
                        <div className="px-6 pt-4">
                          <div className="rounded-xl bg-secondary/50 border border-border p-3">
                            <div className="text-[11px] font-semibold text-foreground/60 uppercase tracking-wide mb-2">
                              Featuring
                            </div>
                            <ul className="space-y-2">
                              {completeCast.map((c) => (
                                <li key={c.id} className="flex items-center gap-3">
                                  <img
                                    src={c.photo}
                                    alt={relationLabelMap[c.relation]}
                                    className="h-9 w-9 rounded-full object-cover border border-primary/20"
                                  />
                                  <div className="text-sm min-w-0 truncate">
                                    {relationLabelMap[c.relation]} as{" "}
                                    <span className="text-primary font-semibold">
                                      {c.roleObj.label}
                                    </span>
                                  </div>
                                </li>
                              ))}
                            </ul>
                          </div>
                        </div>
                      )}

                      <ul className="px-6 pt-6 space-y-3 text-sm">
                        {[
                          `A personalized ${selectedTheme.name.toLowerCase()} story starring ${name}`,
                          `${typeof age === "number" && age <= 6 ? "Easy" : "Age-perfect"} puzzles & quizzes`,
                          "Drawing & coloring challenges",
                          `${name}'s achievement certificate`,
                          ...(birthdayMode && birthday
                            ? [
                                `🎂 Birthday certificate for ${new Date(birthday).toLocaleDateString(undefined, { month: "long", day: "numeric" })}`,
                                "🎉 Party scavenger hunt printable",
                              ]
                            : birthdayMode
                              ? ["🎂 Birthday-themed extras (add a date above for a dated certificate)"]
                              : []),
                          ...(completeCast.length > 0
                            ? [
                                `${completeCast.length} family ${
                                  completeCast.length === 1 ? "member appears" : "members appear"
                                } as characters in the story`,
                              ]
                            : []),
                        ].map((f) => (
                          <li key={f} className="flex items-start gap-2">
                            <Check className="h-4 w-4 text-primary mt-0.5 shrink-0" />
                            <span>{f}</span>
                          </li>
                        ))}
                      </ul>

                      {/* Family Quest */}
                      <div className="mx-6 mt-5 mb-6 rounded-2xl border border-primary/20 bg-primary/5 p-4">
                        <div className="flex items-center gap-2">
                          <span className="grid place-items-center h-7 w-7 rounded-lg bg-primary text-primary-foreground">
                            <Map className="h-4 w-4" />
                          </span>
                          <div>
                            <div className="text-[11px] font-bold uppercase tracking-wide text-primary">
                              Family Quest #1
                            </div>
                            <div className="font-display text-sm font-semibold leading-tight">
                              Things to do together — not just pages to print
                            </div>
                          </div>
                        </div>
                        <ul className="mt-3 space-y-2 text-sm">
                          {[
                            {
                              icon: PencilLine,
                              text: `Build a paper ${selectedTheme.name.toLowerCase() === "airplanes" ? "airplane" : "model"} together`,
                            },
                            {
                              icon: Bird,
                              text: `Find 5 ${selectedTheme.name.toLowerCase() === "animals" ? "different animals" : "birds"} outside`,
                            },
                            {
                              icon: PencilLine,
                              text: `Draw your dream ${selectedTheme.name.toLowerCase().replace(/s$/, "")}`,
                            },
                            ...(completeCast.some(
                              (c) => c.relation === "grandpa" || c.relation === "grandma",
                            )
                              ? [
                                  {
                                    icon: MessageCircle,
                                    text: `Ask Grand${completeCast.some((c) => c.relation === "grandpa") ? "pa" : "ma"} about their first trip`,
                                  },
                                ]
                              : [
                                  {
                                    icon: MessageCircle,
                                    text: "Ask a parent about their favorite childhood adventure",
                                  },
                                ]),
                          ].map((q) => (
                            <li key={q.text} className="flex items-start gap-2">
                              <q.icon className="h-4 w-4 text-primary mt-0.5 shrink-0" />
                              <span>{q.text}</span>
                            </li>
                          ))}
                        </ul>
                      </div>
                    </div>

                    <div className="mt-4 flex gap-2">
                      {completedPackId ? (
                        <button
                          type="button"
                          disabled={downloading}
                          onClick={() => {
                            if (!completedPackId || !selectedTheme) return;
                            setDownloading(true);
                            const fileName = `${name}-${selectedTheme.name}-adventure.pdf`
                              .replace(/\s+/g, "-")
                              .toLowerCase();
                            void adventurePacksApi
                              .downloadAdventurePack(completedPackId, fileName)
                              .catch(() =>
                                toast.error(
                                  "Could not download PDF. Open My Packs or generate again.",
                                ),
                              )
                              .finally(() => setDownloading(false));
                          }}
                          className="flex-1 inline-flex items-center justify-center gap-2 rounded-full bg-foreground text-background py-3 font-semibold hover:opacity-90 transition disabled:opacity-60"
                        >
                          {downloading ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Gift className="h-4 w-4" />
                          )}
                          Download PDF
                        </button>
                      ) : (
                        <button
                          type="button"
                          disabled
                          className="flex-1 inline-flex items-center justify-center gap-2 rounded-full bg-foreground/50 text-background py-3 font-semibold cursor-not-allowed"
                        >
                          <Gift className="h-4 w-4" />
                          Preparing download…
                        </button>
                      )}
                      <button
                        onClick={reset}
                        className="rounded-full bg-card border border-border px-4 py-3 font-semibold hover:bg-secondary transition"
                      >
                        New pack
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
