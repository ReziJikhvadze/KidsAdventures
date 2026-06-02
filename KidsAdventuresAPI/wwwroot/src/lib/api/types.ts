export type SubscriptionType = "Free" | "Premium";

export type ThemeType = "Airplanes" | "Dinosaurs" | "Space" | "Pirates" | "Animals";

export type AdventurePackStatus = "Pending" | "Generating" | "Completed" | "Failed";

export type AuthResponse = {
  token: string;
  expiresAt: string;
  email: string;
  subscriptionType: SubscriptionType;
};

export type ChildResponse = {
  id: string;
  userId: string;
  name: string;
  age: number;
  photoUrl: string | null;
  createdAt: string;
};

export type AdventurePackResponse = {
  id: string;
  userId: string;
  childId: string;
  theme: ThemeType;
  status: AdventurePackStatus;
  pdfUrl: string | null;
  progressMessage: string | null;
  storyLanguage: string | null;
  createdAt: string;
};

export type CheckoutSessionResponse = {
  sessionId: string;
  checkoutUrl: string;
};

export const THEME_ID_TO_API: Record<string, ThemeType> = {
  airplanes: "Airplanes",
  dinosaurs: "Dinosaurs",
  space: "Space",
  pirates: "Pirates",
  animals: "Animals",
};
