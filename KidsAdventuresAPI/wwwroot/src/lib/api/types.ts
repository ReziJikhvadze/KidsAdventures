export type SubscriptionType = "Free" | "Premium";

export type BookPackPlan = "Book1";

export type ThemeType = "Airplanes" | "Dinosaurs" | "Space" | "Pirates" | "Animals" | "Magic";

export type AdventurePackStatus =
  | "Pending"
  | "Generating"
  | "GeneratingStory"
  | "StoryReady"
  | "GeneratingPdf"
  | "Completed"
  | "Failed";

export type BookAccessLevel = "Preview" | "Full";

export type AuthResponse = {
  token: string;
  expiresAt: string;
  /** Empty for accounts that signed up with a phone number only. */
  email: string;
  phoneNumber?: string | null;
  displayName?: string | null;
  preferredLanguage?: string;
  isAdmin?: boolean;
  welcomeStoryRemaining?: number;
  /** @deprecated Retained for older clients reading localStorage; always 0. */
  subscriptionType?: SubscriptionType;
  bookCredits?: number;
  storiesUsedThisMonth?: number;
  storiesAllowedThisMonth?: number;
  storiesRemainingThisMonth?: number;
};

export type SessionInfoResponse = {
  email: string;
  phoneNumber?: string | null;
  displayName?: string | null;
  preferredLanguage?: string;
  isAdmin?: boolean;
  welcomeStoryRemaining?: number;
  /** @deprecated Always empty on the Georgian product. */
  bookCredits?: number;
  storiesUsedThisMonth?: number;
  storiesAllowedThisMonth?: number;
  storiesRemainingThisMonth?: number;
  subscriptionType?: SubscriptionType;
  hasUnlimitedPdf?: boolean;
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

export type EmailStatusResponse = {
  exists: boolean;
  isGoogleAccount: boolean;
  /** The account can only be entered with a magic link or a phone code. */
  isPasswordless?: boolean;
};

export type PasswordlessConfig = {
  magicLinkEnabled: boolean;
  phoneEnabled: boolean;
  /** False when the server is only logging SMS, so the UI can surface the dev code. */
  smsDeliveryLive: boolean;
  magicLinkDeliveryLive: boolean;
  otpLength: number;
  resendCooldownSeconds: number;
};

export type AuthConfigResponse = {
  googleEnabled: boolean;
  googleClientId: string | null;
  recaptchaEnabled?: boolean;
  recaptchaSiteKey?: string | null;
  passwordless?: PasswordlessConfig;
};

/** Server acknowledgement that a sign-in link or code went out. */
export type AuthChallengeResponse = {
  /** Masked email or phone number, safe to echo back to the parent. */
  destination: string;
  expiresInSeconds: number;
  resendAfterSeconds: number;
  deliveryLive: boolean;
  /** Present only in development, when nothing was actually delivered. */
  devSecret?: string | null;
  /** Full magic-link URL in development when email delivery is not live. */
  devUrl?: string | null;
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
  /** Short evocative phrase (3-8 words) shown overlaid on the illustration. */
  caption?: string | null;
  content: string;
  illustrationUrl?: string | null;
  isIllustrated?: boolean;
  /** Past the free preview allowance: text is readable, artwork is withheld and blurred. */
  isLocked?: boolean;
};

export type AdventurePackResponse = {
  id: string;
  userId: string;
  childId?: string | null;
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
  seriesId?: string | null;
  sequenceNumber?: number;
  continuesFromBookId?: string | null;
  accessLevel?: BookAccessLevel;
  worldId?: string | null;
  primaryCharacterId?: string | null;
  title?: string | null;
  coverImageUrl?: string | null;
  hasPrintEntitlement?: boolean;
};

export type AdventurePackDetailResponse = AdventurePackResponse & {
  title?: string | null;
  childName?: string | null;
  previewIllustrationStatus?: PreviewIllustrationStatus;
  storyPages?: StoryPageContent[];
  /** Pages beyond the returned `storyPages` when AccessLevel is Preview. */
  lockedPageCount?: number;
  /** True when AccessLevel is Full. */
  isUnlocked?: boolean;
};

export type CheckoutSessionResponse = {
  sessionId: string;
  checkoutUrl: string;
};

export type PaymentProvider = "stripe";

export const THEME_ID_TO_API: Record<string, ThemeType> = {
  airplanes: "Airplanes",
  dinosaurs: "Dinosaurs",
  space: "Space",
  pirates: "Pirates",
  animals: "Animals",
  magic: "Magic",
};

export type CharacterType = "child" | "adult" | "animal" | "fantasy";
export type CharacterGender = "girl" | "boy";
export type EyeColor = "brown" | "blue" | "green" | "grey";

export type CharacterResponse = {
  id: string;
  name: string;
  birthDate?: string | null;
  age?: number | null;
  gender?: string | null;
  eyeColor?: string | null;
  characterType: string;
  relationship?: string | null;
  isPrimary: boolean;
  photoUrl?: string | null;
  hasAppearanceProfile: boolean;
  canDelete: boolean;
  createdAt: string;
  updatedAt: string;
};

export type SaveCharacterInput = {
  name: string;
  birthDate?: string;
  gender?: CharacterGender | string;
  eyeColor?: EyeColor | string;
  characterType: CharacterType | string;
  relationship?: string;
  isPrimary: boolean;
  removePhoto?: boolean;
  photo?: File;
};

export type WorldResponse = {
  id: string;
  name: string;
  sortOrder: number;
};

export type WorldNodeState = "Locked" | "Unlocked" | "Completed" | "Next";

export type WorldNodeResponse = {
  worldId: string;
  name: string;
  sortOrder: number;
  state: WorldNodeState;
  canStart: boolean;
  bookId?: string | null;
  bookTitle?: string | null;
  coverImageUrl?: string | null;
  sequenceNumber?: number | null;
  completedAt?: string | null;
};

export type ContinuationCharacter = {
  id: string;
  name: string;
  characterType: string;
  relationship?: string | null;
  isPrimary: boolean;
};

export type ContinuationResponse = {
  fromBookId: string;
  fromBookTitle?: string | null;
  fromWorldId: string;
  fromSequenceNumber: number;
  nextSequenceNumber: number;
  suggestedWorldId: string;
  carryForwardCharacters: ContinuationCharacter[];
};

export type AdventureMapResponse = {
  characterId: string;
  characterName: string;
  isFirstJourney: boolean;
  completedCount: number;
  totalWorlds: number;
  nextWorldId?: string | null;
  worlds: WorldNodeResponse[];
  continuation?: ContinuationResponse | null;
};

export type OrderPackage = "Digital" | "Print";
export type OrderType = "NewBook" | "PrintUpgrade";
export type OrderStatus = "Pending" | "Paid" | "Fulfilled" | "Failed" | "Cancelled" | "Refunded";

export type ShippingAddressRequest = {
  recipientName: string;
  recipientPhone: string;
  city: string;
  region?: string;
  addressLine1: string;
  addressLine2?: string;
  postalCode?: string;
  notes?: string;
  saveForLater?: boolean;
};

export type BookDraftRequest = {
  primaryCharacterId: string;
  supportingCharacterIds?: string[];
  worldId: string;
  bookLanguage?: string;
  storyNotes?: string;
  continuesFromBookId?: string;
  previewBookId?: string;
  /** The story the parent actually read in the preview, kept verbatim for the paid book. */
  previewStoryJson?: string;
  /** That preview's cover, reused as page one instead of being redrawn. */
  previewCoverImage?: string;
};

export type CreateOrderRequest = {
  package: OrderPackage;
  promoCode?: string;
  draft?: BookDraftRequest;
  shippingAddress?: ShippingAddressRequest;
  returnPath?: string;
};

export type CreatePrintUpgradeOrderRequest = {
  bookId: string;
  promoCode?: string;
  shippingAddress?: ShippingAddressRequest;
  returnPath?: string;
};

export type QuoteRequest = {
  type?: OrderType | string;
  package: OrderPackage | string;
  promoCode?: string;
};

export type PromoQuote = {
  code: string;
  description?: string | null;
  isValid: boolean;
  percentOff?: number | null;
  isFullDiscount: boolean;
  discountMinor: number;
  message?: string | null;
};

export type QuoteResponse = {
  currency: string;
  subtotalMinor: number;
  discountMinor: number;
  totalMinor: number;
  isFree: boolean;
  promo?: PromoQuote | null;
};

export type CheckoutResponse = {
  orderId: string;
  totalMinor: number;
  currency: string;
  isFree: boolean;
  checkoutUrl?: string | null;
  providerSessionId?: string | null;
  bookId?: string | null;
};

export type OrderResponse = {
  id: string;
  bookId?: string | null;
  type: OrderType;
  package: OrderPackage;
  currency: string;
  subtotalMinor: number;
  discountMinor: number;
  totalMinor: number;
  status: OrderStatus;
  promoCode?: string | null;
  failureReason?: string | null;
  createdAt: string;
  paidAt?: string | null;
  fulfilledAt?: string | null;
};

export type OrderStatusResponse = {
  orderId: string;
  status: OrderStatus;
  bookId?: string | null;
  bookReady: boolean;
  progressMessage?: string | null;
  failureReason?: string | null;
};

export type PrintOrderStatus = "AwaitingPrint" | "Printing" | "Shipped" | "Delivered" | "Cancelled";

export type PrintOrderResponse = {
  id: string;
  orderId: string;
  bookId: string;
  bookTitle?: string | null;
  status: PrintOrderStatus;
  statusLabel: string;
  recipientName: string;
  recipientPhone: string;
  city: string;
  region?: string | null;
  addressLine1: string;
  addressLine2?: string | null;
  postalCode?: string | null;
  notes?: string | null;
  trackingCode?: string | null;
  deliveryEstimate: string;
  canEditAddress: boolean;
  createdAt: string;
  shippedAt?: string | null;
  deliveredAt?: string | null;
};

export type AddressResponse = {
  id: string;
  recipientName: string;
  recipientPhone: string;
  city: string;
  region?: string | null;
  addressLine1: string;
  addressLine2?: string | null;
  postalCode?: string | null;
  isDefault: boolean;
  deliveryEstimate: string;
};

export type SaveAddressRequest = {
  id?: string;
  recipientName: string;
  recipientPhone: string;
  city: string;
  region?: string;
  addressLine1: string;
  addressLine2?: string;
  postalCode?: string;
  isDefault?: boolean;
};
