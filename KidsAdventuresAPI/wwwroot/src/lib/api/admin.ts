import { apiRequest } from "./client";

export type AdminOverview = {
  ordersInWindow: number;
  paidOrdersInWindow: number;
  revenueMinorInWindow: number;
  newCustomersInWindow: number;
  booksGeneratedInWindow: number;
  booksFailed: number;
  booksInFlight: number;
  paidButUnfulfilled: number;
  printOrdersAwaiting: number;
};

export type AdminOrderRow = {
  id: string;
  userId: string;
  customerEmail: string | null;
  customerPhone: string | null;
  bookId: string | null;
  bookTitle: string | null;
  type: string;
  package: string;
  status: string;
  currency: string;
  subtotalMinor: number;
  discountMinor: number;
  totalMinor: number;
  failureReason: string | null;
  createdAt: string;
  paidAt: string | null;
  fulfilledAt: string | null;
};

export type AdminCustomerRow = {
  id: string;
  email: string | null;
  phoneNumber: string | null;
  displayName: string | null;
  bookCount: number;
  orderCount: number;
  spendMinor: number;
  createdAt: string;
};

export type AdminProductionRow = {
  id: string;
  title: string | null;
  status: string;
  worldId: string | null;
  sequenceNumber: number;
  heroName: string | null;
  customerEmail: string | null;
  progressMessage: string | null;
  errorMessage: string | null;
  createdAt: string;
};

export type StoryRuleCell = {
  id: string;
  ageBand: string;
  /** null means the row applies to every world in the band. */
  theme: string | null;
  maxWordsPerPage: number | null;
  maxSentenceWords: number | null;
  vocabularyLevel: string | null;
  scarinessLimit: number | null;
  extraGuidance: string | null;
  isActive: boolean;
  updatedAt: string;
};

export type StoryRuleMatrix = {
  ageBands: string[];
  themes: string[];
  cells: StoryRuleCell[];
};

type Paged<T> = { total: number; page: number; pageSize: number; items: T[] };

function query(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const q = search.toString();
  return q ? `?${q}` : "";
}

export function getOverview(days = 30): Promise<AdminOverview> {
  return apiRequest<AdminOverview>(`/api/admin/overview${query({ days })}`);
}

export function listOrders(params: {
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<Paged<AdminOrderRow>> {
  return apiRequest<Paged<AdminOrderRow>>(`/api/admin/orders${query(params)}`);
}

export function listCustomers(params: {
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<Paged<AdminCustomerRow>> {
  return apiRequest<Paged<AdminCustomerRow>>(`/api/admin/customers${query(params)}`);
}

export function listProduction(params: {
  includeCompleted?: boolean;
  page?: number;
  pageSize?: number;
}): Promise<Paged<AdminProductionRow>> {
  return apiRequest<Paged<AdminProductionRow>>(`/api/admin/production${query(params)}`);
}

export function getStoryRules(): Promise<StoryRuleMatrix> {
  return apiRequest<StoryRuleMatrix>("/api/admin/story-rules");
}

export function updateStoryRule(
  id: string,
  body: {
    maxWordsPerPage: number | null;
    maxSentenceWords: number | null;
    vocabularyLevel: string | null;
    scarinessLimit: number | null;
    extraGuidance: string | null;
    isActive: boolean;
  },
): Promise<StoryRuleCell> {
  return apiRequest<StoryRuleCell>(`/api/admin/story-rules/${id}`, {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

/** Minor units to a readable GEL amount. */
export function gel(minor: number): string {
  return `${(minor / 100).toLocaleString("ka-GE", { maximumFractionDigits: 2 })} ₾`;
}
