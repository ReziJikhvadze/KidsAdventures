export type PurchaseType =
  | "DIGITAL"
  | "PRINTED_DIGITAL"
  | "PRINT_UPGRADE";

export type PrintJobStatus =
  | "READY_FOR_REVIEW"
  | "SENT"
  | "ACCEPTED"
  | "PRINTING"
  | "QUALITY_CHECK"
  | "PACKED"
  | "READY_FOR_PICKUP"
  | "COMPLETED"
  | "EXCEPTION";

export type CourierStatus =
  | "NOT_CREATED"
  | "CREATING"
  | "CREATED"
  | "READY_FOR_PICKUP"
  | "PICKED_UP"
  | "IN_TRANSIT"
  | "DELIVERED"
  | "FAILED"
  | "EXCEPTION"
  | "RETURNED"
  | "CANCELLED";

export type Money = {
  amountMinor: number;
  currency: "GEL";
};

export type ApprovedPrintAssetSnapshot = {
  id: string;
  version: string;
  fileName: string;
  sha256: string;
  sizeBytes: number;
  signedDownloadUrl: string;
};

export type PartnerPrintJobDto = {
  id: string;
  publicOrderNumber: string;
  bookTitle: string;
  quantity: number;
  technicalSpecification: Record<string, unknown>;
  dueAt: string;
  status: PrintJobStatus;
  approvedAsset: ApprovedPrintAssetSnapshot;
};

// Intentionally absent from PartnerPrintJobDto:
// parent email, phone, address, payment, child photo, interests, prompt.

