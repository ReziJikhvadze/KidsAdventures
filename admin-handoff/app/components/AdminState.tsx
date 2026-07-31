"use client";

/* eslint-disable react-hooks/set-state-in-effect */

import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

export type PrintJobStatus =
  | "Ready for review"
  | "Sent"
  | "Accepted"
  | "Printing"
  | "Quality check"
  | "Packed"
  | "Ready for pickup";

export const printJobStatusLabels: Record<PrintJobStatus, string> = {
  "Ready for review": "PDF-ის დადასტურებას ელოდება",
  Sent: "პარტნიორთან გაგზავნილია",
  Accepted: "პარტნიორმა მიიღო",
  Printing: "იბეჭდება",
  "Quality check": "ხარისხი მოწმდება",
  Packed: "შეფუთულია",
  "Ready for pickup": "კურიერს ელოდება",
};

export type CourierStatus = "not_created" | "creating" | "created" | "failed";

export type PrintDocumentSnapshot = {
  fileId: string;
  fileName: string;
  version: string;
  sha256: string;
  generatedAt: string;
  size: string;
  preflightPassed: boolean;
};

export type PrintJob = {
  id: string;
  orderId: string;
  status: PrintJobStatus;
  version: string;
  dueAt: string;
  createdDate: string;
  sentAt?: string;
  acceptedAt?: string;
  lastUpdatedAt: string;
  owner: "Adventrya Admin" | "BookLab" | "Courier";
  sla: "On track" | "At risk" | "Breached";
  approvedFileId?: string;
  approvedFileName?: string;
  approvedVersion?: string;
  approvedHash?: string;
  approvedAt?: string;
  approvedBy?: string;
  courierStatus: CourierStatus;
  courierCreated: boolean;
  courierIdempotencyKey?: string;
  externalCourierOrderId?: string;
  trackingId?: string;
};

export type AuditCategory =
  | "ORDER"
  | "BOOK"
  | "PRINT"
  | "DELIVERY"
  | "PAYMENT"
  | "ACCESS";

export type AuditEvent = {
  id: string;
  orderId?: string;
  category: AuditCategory;
  action: string;
  detail: string;
  actor: string;
  actorRole: "System" | "Super Admin" | "Operations" | "Print Partner" | "Courier";
  timestamp: string;
  eventDate: string;
  severity: "info" | "success" | "warning" | "danger";
  immutable: true;
};

type ApprovalResult =
  | "approved"
  | "already-approved"
  | "blocked"
  | "revision-required";

export type CourierResult = "creating" | "existing" | "processing" | "blocked";

type NewAuditEvent = Omit<AuditEvent, "id" | "immutable">;

type AdminStateValue = {
  printJobs: PrintJob[];
  approvedOrderIds: string[];
  auditEvents: AuditEvent[];
  approveForPrint: (
    orderId: string,
    snapshot: PrintDocumentSnapshot,
  ) => ApprovalResult;
  updatePrintJob: (orderId: string, status: PrintJobStatus) => boolean;
  createCourierOrder: (orderId: string) => CourierResult;
  recordAudit: (event: NewAuditEvent) => void;
};

const initialJobs: PrintJob[] = [
  {
    id: "PJ-2087",
    orderId: "ADV-1046",
    status: "Ready for review",
    version: "v1 · SHA-8f2a",
    dueAt: "31 ივლისი",
    createdDate: "2026-07-28",
    lastUpdatedAt: "დღეს, 12:57",
    owner: "Adventrya Admin",
    sla: "On track",
    courierStatus: "not_created",
    courierCreated: false,
  },
  {
    id: "PJ-2086",
    orderId: "ADV-1045",
    status: "Printing",
    version: "v1 · SHA-4ca1",
    dueAt: "30 ივლისი",
    createdDate: "2026-07-27",
    sentAt: "გუშინ, 18:12",
    acceptedAt: "გუშინ, 18:36",
    lastUpdatedAt: "დღეს, 10:24",
    owner: "BookLab",
    sla: "On track",
    approvedFileId: "PDF-1045-V1",
    approvedFileName: "Adventrya_ADV-1045_print_v1.pdf",
    approvedVersion: "v1",
    approvedHash: "SHA-256 · 4ca1e27b…d914",
    approvedAt: "გუშინ, 18:12",
    approvedBy: "ომიკო · Super Admin",
    courierStatus: "not_created",
    courierCreated: false,
  },
  {
    id: "PJ-2085",
    orderId: "ADV-1044",
    status: "Packed",
    version: "v2 · SHA-1bd7",
    dueAt: "დღეს",
    createdDate: "2026-07-27",
    sentAt: "გუშინ, 16:44",
    acceptedAt: "გუშინ, 17:05",
    lastUpdatedAt: "დღეს, 13:08",
    owner: "Adventrya Admin",
    sla: "At risk",
    approvedFileId: "PDF-1044-V2",
    approvedFileName: "Adventrya_ADV-1044_print_v2.pdf",
    approvedVersion: "v2",
    approvedHash: "SHA-256 · 1bd7a91c…8f20",
    approvedAt: "გუშინ, 16:44",
    approvedBy: "თაკო · Operations",
    courierStatus: "not_created",
    courierCreated: false,
  },
];

const initialAuditEvents: AuditEvent[] = [
  {
    id: "AUD-0098",
    orderId: "ADV-1044",
    category: "PRINT",
    action: "წიგნი შეფუთულად მოინიშნა",
    detail: "PJ-2085 · პარტნიორმა დაასრულა ხარისხის შემოწმება",
    actor: "ნიკა · BookLab",
    actorRole: "Print Partner",
    timestamp: "დღეს, 13:08",
    eventDate: "2026-07-28",
    severity: "success",
    immutable: true,
  },
  {
    id: "AUD-0097",
    orderId: "ADV-1043",
    category: "BOOK",
    action: "გენერაცია შეჩერდა",
    detail: "გვერდი 4 · ავტომატური retry უშედეგოა",
    actor: "Book Engine",
    actorRole: "System",
    timestamp: "დღეს, 12:46",
    eventDate: "2026-07-28",
    severity: "danger",
    immutable: true,
  },
  {
    id: "AUD-0096",
    orderId: "ADV-1045",
    category: "PRINT",
    action: "წარმოება დაიწყო",
    detail: "PJ-2086 · დამტკიცებული PDF v1 · SHA-256 4ca1e27b…d914",
    actor: "ნიკა · BookLab",
    actorRole: "Print Partner",
    timestamp: "დღეს, 10:24",
    eventDate: "2026-07-28",
    severity: "info",
    immutable: true,
  },
  {
    id: "AUD-0095",
    orderId: "ADV-1042",
    category: "DELIVERY",
    action: "მიწოდების SLA დაირღვა",
    detail: "კურიერის ბოლო განახლება · 26 ივლისი",
    actor: "Courier Webhook",
    actorRole: "System",
    timestamp: "დღეს, 09:52",
    eventDate: "2026-07-28",
    severity: "danger",
    immutable: true,
  },
  {
    id: "AUD-0094",
    orderId: "ADV-1045",
    category: "PRINT",
    action: "საბეჭდი PDF დადასტურდა",
    detail: "PDF-1045-V1 · v1 · SHA-256 4ca1e27b…d914",
    actor: "ომიკო",
    actorRole: "Super Admin",
    timestamp: "გუშინ, 18:12",
    eventDate: "2026-07-27",
    severity: "success",
    immutable: true,
  },
];

const AdminStateContext = createContext<AdminStateValue | null>(null);

function makeAuditEvent(event: NewAuditEvent): AuditEvent {
  return {
    ...event,
    id: `AUD-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
    immutable: true,
  };
}

export function AdminStateProvider({ children }: { children: ReactNode }) {
  const [printJobs, setPrintJobs] = useState<PrintJob[]>(initialJobs);
  const [approvedOrderIds, setApprovedOrderIds] = useState<string[]>([
    "ADV-1045",
    "ADV-1044",
  ]);
  const [auditEvents, setAuditEvents] =
    useState<AuditEvent[]>(initialAuditEvents);
  const [hydrated, setHydrated] = useState(false);
  const courierLocks = useRef(new Set<string>());

  useEffect(() => {
    try {
      const saved = window.localStorage.getItem("adventrya-admin-ux-v4");
      if (saved) {
        const parsed = JSON.parse(saved) as {
          printJobs?: PrintJob[];
          approvedOrderIds?: string[];
          auditEvents?: AuditEvent[];
        };
        if (parsed.printJobs) {
          setPrintJobs(
            parsed.printJobs.map((job) => ({
              ...job,
              courierStatus:
                job.courierCreated || job.courierStatus === "created"
                  ? "created"
                  : "not_created",
            })),
          );
        }
        if (parsed.approvedOrderIds) setApprovedOrderIds(parsed.approvedOrderIds);
        if (parsed.auditEvents) setAuditEvents(parsed.auditEvents);
      }
    } catch {
      // The prototype remains usable if browser storage is unavailable.
    }
    setHydrated(true);
  }, []);

  useEffect(() => {
    if (!hydrated) return;
    window.localStorage.setItem(
      "adventrya-admin-ux-v4",
      JSON.stringify({ printJobs, approvedOrderIds, auditEvents }),
    );
  }, [approvedOrderIds, auditEvents, hydrated, printJobs]);

  const value = useMemo<AdminStateValue>(
    () => ({
      printJobs,
      approvedOrderIds,
      auditEvents,
      recordAudit(event) {
        setAuditEvents((current) => [makeAuditEvent(event), ...current]);
      },
      approveForPrint(orderId, snapshot) {
        if (!snapshot.preflightPassed) return "blocked";

        const existing = printJobs.find((job) => job.orderId === orderId);
        if (existing?.approvedFileId === snapshot.fileId) return "already-approved";
        if (existing?.approvedFileId) return "revision-required";

        setApprovedOrderIds((current) =>
          current.includes(orderId) ? current : [...current, orderId],
        );
        setPrintJobs((current) => {
          const currentJob = current.find((job) => job.orderId === orderId);
          const approvedFields = {
            status: "Sent" as const,
            version: `${snapshot.version} · ${snapshot.sha256.slice(0, 16)}`,
            sentAt: "ახლახან",
            lastUpdatedAt: "ახლახან",
            owner: "BookLab" as const,
            sla: "On track" as const,
            approvedFileId: snapshot.fileId,
            approvedFileName: snapshot.fileName,
            approvedVersion: snapshot.version,
            approvedHash: snapshot.sha256,
            approvedAt: "ახლახან",
            approvedBy: "ომიკო · Super Admin",
          };

          if (currentJob) {
            return current.map((job) =>
              job.orderId === orderId ? { ...job, ...approvedFields } : job,
            );
          }

          return [
            {
              id: `PJ-${2090 + current.length}`,
              orderId,
              dueAt: "1 აგვისტო",
              createdDate: "2026-07-28",
              courierStatus: "not_created",
              courierCreated: false,
              ...approvedFields,
            },
            ...current,
          ];
        });
        setAuditEvents((current) => [
          makeAuditEvent({
            orderId,
            category: "PRINT",
            action: "საბეჭდი PDF დადასტურდა და გაიგზავნა",
            detail: `${snapshot.fileId} · ${snapshot.version} · ${snapshot.sha256}`,
            actor: "ომიკო",
            actorRole: "Super Admin",
            timestamp: "ახლახან",
            eventDate: "2026-07-28",
            severity: "success",
          }),
          ...current,
        ]);
        return "approved";
      },
      updatePrintJob(orderId, status) {
        const currentJob = printJobs.find((job) => job.orderId === orderId);
        if (!currentJob) return false;

        const allowedNext: Partial<Record<PrintJobStatus, PrintJobStatus>> = {
          Sent: "Accepted",
          Accepted: "Printing",
          Printing: "Quality check",
          "Quality check": "Packed",
        };
        if (allowedNext[currentJob.status] !== status) return false;

        setPrintJobs((current) =>
          current.map((job) =>
            job.orderId === orderId
              ? {
                  ...job,
                  status,
                  acceptedAt:
                    status === "Accepted" ? "ახლახან" : job.acceptedAt,
                  lastUpdatedAt: "ახლახან",
                  owner: status === "Packed" ? "Adventrya Admin" : "BookLab",
                }
              : job,
          ),
        );
        setAuditEvents((current) => [
          makeAuditEvent({
            orderId,
            category: "PRINT",
            action: printJobStatusLabels[status],
            detail: `${currentJob.id} · ${currentJob.approvedFileId ?? "დადასტურებული PDF"}`,
            actor: "ნიკა · BookLab",
            actorRole: "Print Partner",
            timestamp: "ახლახან",
            eventDate: "2026-07-28",
            severity: status === "Packed" ? "success" : "info",
          }),
          ...current,
        ]);
        return true;
      },
      createCourierOrder(orderId) {
        const job = printJobs.find((item) => item.orderId === orderId);
        if (!job || job.status !== "Packed") return "blocked";
        if (job.courierStatus === "created" || job.courierCreated) return "existing";
        if (job.courierStatus === "creating" || courierLocks.current.has(orderId)) {
          return "processing";
        }

        courierLocks.current.add(orderId);
        const idempotencyKey = `courier:${job.id}`;
        setPrintJobs((current) =>
          current.map((item) =>
            item.orderId === orderId
              ? {
                  ...item,
                  courierStatus: "creating",
                  courierIdempotencyKey: idempotencyKey,
                  lastUpdatedAt: "ახლახან",
                }
              : item,
          ),
        );
        setAuditEvents((current) => [
          makeAuditEvent({
            orderId,
            category: "DELIVERY",
            action: "საკურიერო შეკვეთის შექმნა დაიწყო",
            detail: `${job.id} · idempotency key ${idempotencyKey}`,
            actor: "ომიკო",
            actorRole: "Super Admin",
            timestamp: "ახლახან",
            eventDate: "2026-07-28",
            severity: "info",
          }),
          ...current,
        ]);

        window.setTimeout(() => {
          const suffix = job.id.replace(/\D/g, "").slice(-4);
          setPrintJobs((current) =>
            current.map((item) =>
              item.orderId === orderId && item.courierStatus !== "created"
                ? {
                    ...item,
                    courierStatus: "created",
                    courierCreated: true,
                    externalCourierOrderId: `CO-${suffix}`,
                    trackingId: `DLV-${suffix}219`,
                    status: "Ready for pickup",
                    lastUpdatedAt: "ახლახან",
                    owner: "Courier",
                    sla: "On track",
                  }
                : item,
            ),
          );
          setAuditEvents((current) => [
            makeAuditEvent({
              orderId,
              category: "DELIVERY",
              action: "საკურიერო შეკვეთა შეიქმნა",
              detail: `CO-${suffix} · DLV-${suffix}219 · ${idempotencyKey}`,
              actor: "Courier Adapter",
              actorRole: "System",
              timestamp: "ახლახან",
              eventDate: "2026-07-28",
              severity: "success",
            }),
            ...current,
          ]);
          courierLocks.current.delete(orderId);
        }, 650);

        return "creating";
      },
    }),
    [approvedOrderIds, auditEvents, printJobs],
  );

  return (
    <AdminStateContext.Provider value={value}>
      {children}
    </AdminStateContext.Provider>
  );
}

export function useAdminState() {
  const value = useContext(AdminStateContext);
  if (!value) throw new Error("useAdminState must be used within AdminStateProvider");
  return value;
}
