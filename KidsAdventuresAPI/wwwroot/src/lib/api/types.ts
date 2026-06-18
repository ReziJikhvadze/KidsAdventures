export type SubscriptionType = "Free" | "Premium";

export type BookPackPlan = "Books3" | "Books5" | "Books15";

export type ThemeType = "Airplanes" | "Dinosaurs" | "Space" | "Pirates" | "Animals";

export type AdventurePackStatus =
  | "Pending"
  | "Generating"
  | "GeneratingStory"
  | "StoryReady"
  | "GeneratingPdf"
  | "Completed"
  | "Failed";

export type AuthResponse = {
  token: string;
  expiresAt: string;
  email: string;
  subscriptionType: SubscriptionType;
  bookCredits: number;
  storiesUsedThisMonth: number;
  storiesAllowedThisMonth: number;
  storiesRemainingThisMonth: number;
  welcomeStoryRemaining?: number;
};

export type SessionInfoResponse = {
  email: string;
  bookCredits: number;
  storiesUsedThisMonth: number;
  storiesAllowedThisMonth: number;
  storiesRemainingThisMonth: number;
  welcomeStoryRemaining?: number;
  subscriptionType: SubscriptionType;
  hasUnlimitedPdf: boolean;
};

export type AccountBalanceResponse = {
  bookCredits: number;
  storiesUsedThisMonth: number;
  storiesAllowedThisMonth: number;
  storiesRemainingThisMonth: number;
  welcomeStoryRemaining?: number;
  subscriptionType: SubscriptionType;
  hasUnlimitedPdf: boolean;
};

export type RegisterResponse = {
  message: string;
  email: string;
};

export type AuthConfigResponse = {
  googleEnabled: boolean;
  googleClientId: string | null;
};

export type ChildResponse = {
  id: string;
  userId: string;
  name: string;
  age: number;
  photoUrl: string | null;
  createdAt: string;
};

export type PreviewIllustrationStatus = "None" | "Generating" | "Ready" | "Failed";

export type StoryPageContent = {
  title: string;
  content: string;
  illustrationUrl?: string | null;
  isIllustrated?: boolean;
};

export type AdventurePackResponse = {
  id: string;
  userId: string;
  childId: string;
  theme: ThemeType;
  status: AdventurePackStatus;
  pdfUrl: string | null;
  progressMessage: string | null;
  errorMessage: string | null;
  storyLanguage: string | null;
  previewIllustrationStatus?: PreviewIllustrationStatus;
  storyPageCount?: number;
  isWelcomeGiftStory?: boolean;
  createdAt: string;
};

export type AdventurePackDetailResponse = AdventurePackResponse & {
  title?: string | null;
  childName?: string | null;
  previewIllustrationStatus?: PreviewIllustrationStatus;
  storyPages?: StoryPageContent[];
};

export type CheckoutSessionResponse = {
  sessionId: string;
  checkoutUrl: string;
};

export type PaymentProvider = "stripe" | "dodo";

export const THEME_ID_TO_API: Record<string, ThemeType> = {
  airplanes: "Airplanes",
  dinosaurs: "Dinosaurs",
  space: "Space",
  pirates: "Pirates",
  animals: "Animals",
};
