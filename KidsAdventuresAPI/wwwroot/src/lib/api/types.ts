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
  /** How many digits the code has, so the panel draws exactly that many boxes. */
  otpLength: number;
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

/** Where a book is in being written. Polled while the loader is on screen. */
export type MasterStoryRunStatus = {
  runId: string;
  worldId?: string | null;
  status: "Pending" | "Writing" | "Illustrating" | "Ready" | "Failed";
  progressMessage?: string | null;
  errorMessage?: string | null;
  title?: string | null;
  childName?: string | null;
  coverImageUrl?: string | null;
  firstPageTitle?: string | null;
  firstPageText?: string | null;
  /** Sixteen, for a book of eight spreads. */
  pageCount?: number;
  /**
   * The inputs the run was written from, for a journey resumed in a new tab. Present only once
   * the story is Ready; the child's photograph itself never travels — `hasPortrait` says the
   * server holds one that a new character can be given at order time.
   */
  birthDate?: string | null;
  gender?: string | null;
  eyeColor?: string | null;
  hasPortrait?: boolean;
};

export type StoryPageContent = {
  title: string;
  /** Short evocative phrase (3-8 words) shown overlaid on the illustration. */
  caption?: string | null;
  content: string;
  illustrationUrl?: string | null;
  isIllustrated?: boolean;
  /**
   * True for the prose half of a spread. The reader draws art and copy on the same page, so
   * without this a page whose only text is a caption prints that caption across the picture.
   */
  isTextOnlyPage?: boolean;
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
  /** 0-100 while a job is running, null otherwise. */
  progressPercent?: number | null;
  errorMessage: string | null;
  isFailed?: boolean;
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
  /** When the book was last opened in the reader, on any device. Null until it has been read. */
  lastReadAt?: string | null;

  /**
   * Which pipeline drew this book: "beki" or "legacy". Durable on the row rather than inferred,
   * because the two disagree about what "ready" means and guessing produced a shelf that offered
   * a finished book minutes before one existed.
   */
  generationPipeline?: string | null;

  /**
   * A Beki book that has not reached Completed. StoryReady means the words are written on this
   * pipeline, not that the book exists — the illustrations, the composition and the release check
   * all come after it. The shelf treats this as "still being made", which is what it is.
   */
  generationPending?: boolean;

  /**
   * Why a finished book has no file to download: "review" while a person is being waited on,
   * "gates" while something measurable is failing, null when nothing is holding it.
   *
   * Present only when it is true of this book. It exists so the download button can say something
   * honest instead of asking for a PDF that will be refused in English.
   */
  downloadHeld?: "review" | "gates" | null;
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
  /**
   * Eight spreads rather than pages that each carry art and text. Book-level on purpose: an
   * older book's page also reports isTextOnlyPage false, so the page alone cannot say.
   */
  isSpreadBook?: boolean;
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
  /** The child as their books draw them. Null until a first book has been illustrated. */
  heroPortraitUrl?: string | null;
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
  /**
   * A preview run whose parked portrait becomes this character's photo. Used when a journey is
   * resumed in a new tab and the photograph the parent chose only exists on the server.
   */
  portraitRunId?: string;
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
  bookFailed?: boolean;
  parentMessage?: string | null;
  progressMessage?: string | null;
  failureReason?: string | null;
  /** 0–100 while the book is being made, when the job reports one. */
  progressPercent?: number | null;
  /** The pack's own status (Pending, GeneratingStory, StoryReady, GeneratingPdf, Completed, Failed). */
  packStatus?: string | null;
  /** When the job last wrote to the row; a stale one is a job that stopped. */
  heartbeatUtc?: string | null;
  /**
   * What the book is, for a screen that only has the order id — a parent coming back from the
   * bank on a hard page load has no draft left to describe the book they just paid for.
   */
  title?: string | null;
  worldId?: string | null;
  childName?: string | null;
  coverImageUrl?: string | null;
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
