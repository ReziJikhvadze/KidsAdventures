import { apiRequest } from "./client";
import type { ThemeType } from "./types";

export type StoryGraphNodeType =
  | "narrative"
  | "decision"
  | "problem_gate"
  | "campfire"
  | "parent_approval";

export type StoryNodeContent = {
  text?: string | null;
  artVariantIds?: string[];
};

export type ProblemDefinition = {
  interactionType: string;
  prompt?: string | null;
  configJson?: string | null;
};

export type StoryGraphPath = {
  id: string;
  title: string;
  theme: ThemeType;
  startNodeId: string | null;
  version: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
};

export type StoryGraphChoice = {
  id: string;
  storyPathId: string;
  fromNodeId: string;
  toNodeId: string;
  choiceKey: string;
  label: string;
  consequenceTag: string | null;
  sortOrder: number;
};

export type StoryGraphNode = {
  id: string;
  storyPathId: string;
  nodeKey: string;
  nodeType: StoryGraphNodeType;
  title: string;
  content: StoryNodeContent | null;
  problem: ProblemDefinition | null;
  requiresParentApproval: boolean;
  mapPositionX: number | null;
  mapPositionY: number | null;
  sortOrder: number;
  choices: StoryGraphChoice[];
};

export type StoryGraphDetail = {
  path: StoryGraphPath;
  nodes: StoryGraphNode[];
  choices: StoryGraphChoice[];
};

export type StoryGraphProgress = {
  childId: string;
  storyPathId: string;
  currentNodeId: string | null;
  visitedNodeIds: string[];
  updatedAt: string;
};

export type StoryGraphPlayResponse = {
  graph: StoryGraphDetail;
  progress: StoryGraphProgress | null;
  pathMode: "Graph";
};

export type CreateStoryGraphPathRequest = {
  title: string;
  theme: ThemeType;
};

export type UpsertStoryGraphNodeRequest = {
  nodeKey: string;
  nodeType: StoryGraphNodeType;
  title: string;
  content?: StoryNodeContent | null;
  problem?: ProblemDefinition | null;
  requiresParentApproval?: boolean;
  mapPositionX?: number | null;
  mapPositionY?: number | null;
  sortOrder?: number;
};

export type UpsertStoryGraphChoiceRequest = {
  fromNodeId: string;
  toNodeId: string;
  choiceKey: string;
  label: string;
  consequenceTag?: string | null;
  sortOrder?: number;
};

export async function listStoryGraphPaths(theme?: ThemeType): Promise<StoryGraphPath[]> {
  const query = theme ? `?theme=${encodeURIComponent(theme)}` : "";
  return apiRequest<StoryGraphPath[]>(`/api/story-path/graph${query}`);
}

export async function getActiveStoryGraph(
  theme: ThemeType,
  childId?: string,
): Promise<StoryGraphPlayResponse> {
  const params = new URLSearchParams({ theme });
  if (childId) params.set("childId", childId);
  return apiRequest<StoryGraphPlayResponse>(`/api/story-path/graph/active?${params}`);
}

export async function getStoryGraphDetail(pathId: string): Promise<StoryGraphDetail> {
  return apiRequest<StoryGraphDetail>(`/api/story-path/graph/${pathId}`);
}

export async function createStoryGraphPath(
  request: CreateStoryGraphPathRequest,
): Promise<StoryGraphPath> {
  return apiRequest<StoryGraphPath>("/api/story-path/graph", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
}

export async function publishStoryGraph(pathId: string): Promise<void> {
  await apiRequest<void>(`/api/story-path/graph/${pathId}/publish`, { method: "POST" });
}

export async function createStoryGraphNode(
  pathId: string,
  request: UpsertStoryGraphNodeRequest,
): Promise<StoryGraphNode> {
  return apiRequest<StoryGraphNode>(`/api/story-path/graph/${pathId}/nodes`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
}

export async function createStoryGraphChoice(
  pathId: string,
  request: UpsertStoryGraphChoiceRequest,
): Promise<StoryGraphChoice> {
  return apiRequest<StoryGraphChoice>(`/api/story-path/graph/${pathId}/choices`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
}

export async function seedLinearStoryGraph(theme: ThemeType): Promise<StoryGraphDetail> {
  return apiRequest<StoryGraphDetail>(`/api/story-path/graph/seed-linear/${theme}`, {
    method: "POST",
  });
}
