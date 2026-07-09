import type { ThemeType } from "@/lib/api/types";
import { apiRequest } from "@/lib/api/client";

export type StoryPathNodeStatus = "Locked" | "Unlocked" | "Generating" | "ReadyToRead" | "Complete";

export type StoryPathNode = {
  chapterIndex: number;
  storyNodeId?: string | null;
  nodeKey?: string | null;
  status: StoryPathNodeStatus;
  adventurePackId?: string | null;
  title?: string | null;
  coverIllustrationUrl?: string | null;
  parentConfirmedAt?: string | null;
};

export type StoryPathWorld = {
  theme: ThemeType;
  hasReadablePack: boolean;
  isWorldComplete: boolean;
  pathMode?: "Linear" | "Graph";
  nodes: StoryPathNode[];
};

export type StoryPathAchievement = {
  theme: ThemeType;
  achievementKey: string;
  label: string;
  earnedAt: string;
};

export type StoryPathOverview = {
  childId: string;
  worlds: StoryPathWorld[];
  achievements: StoryPathAchievement[];
};

export type StoryPathWorldResponse = {
  childId: string;
  world: StoryPathWorld;
  achievements: StoryPathAchievement[];
};

export type ConfirmCampfireResponse = {
  world: StoryPathWorld;
};

export type GenerateChapterResponse = {
  world: StoryPathWorld;
};

export type CompleteChapterResponse = {
  world: StoryPathWorld;
  newAchievement?: StoryPathAchievement | null;
  nextTheme?: string | null;
  suggestNextWorld: boolean;
};

export function getStoryPathOverview(childId: string) {
  return apiRequest<StoryPathOverview>(`/api/story-path?childId=${encodeURIComponent(childId)}`);
}

export function getStoryPathWorld(childId: string, theme: ThemeType) {
  return apiRequest<StoryPathWorldResponse>(
    `/api/story-path/${encodeURIComponent(theme)}?childId=${encodeURIComponent(childId)}`,
  );
}

export function getStoryPathAchievements(childId: string) {
  return apiRequest<StoryPathAchievement[]>(
    `/api/story-path/achievements?childId=${encodeURIComponent(childId)}`,
  );
}

export function getCampfirePrompt(theme: ThemeType, nodeIndex: number) {
  return apiRequest<{ prompt: string }>(
    `/api/story-path/${encodeURIComponent(theme)}/campfire-prompt?nodeIndex=${nodeIndex}`,
  );
}

export function confirmCampfire(input: {
  childId: string;
  adventurePackId: string;
  nodeIndex: number;
}) {
  return apiRequest<ConfirmCampfireResponse>("/api/story-path/confirm-campfire", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function generateChapter(theme: ThemeType, chapterIndex: number, childId: string) {
  return apiRequest<GenerateChapterResponse>(
    `/api/story-path/${encodeURIComponent(theme)}/chapters/${chapterIndex}/generate`,
    {
      method: "POST",
      body: JSON.stringify({ childId }),
    },
  );
}

export function completeChapter(theme: ThemeType, chapterIndex: number, childId: string) {
  return apiRequest<CompleteChapterResponse>(
    `/api/story-path/${encodeURIComponent(theme)}/chapters/${chapterIndex}/complete`,
    {
      method: "POST",
      body: JSON.stringify({ childId }),
    },
  );
}
