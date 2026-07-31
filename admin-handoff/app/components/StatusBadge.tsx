import type { OperationalStatus } from "../data";

const labels: Record<OperationalStatus, string> = {
  Paid: "გადახდილია",
  Pending: "მოლოდინში",
  Failed: "შეცდომა",
  Ready: "მზადაა",
  Generating: "გენერირდება",
  Review: "შესამოწმებელია",
  "Not required": "არ სჭირდება",
  "Ready for print": "მზადაა დასაბეჭდად",
  "In production": "იბეჭდება",
  Packed: "შეფუთულია",
  Shipped: "გაგზავნილია",
  Delivered: "მიწოდებულია",
  "Not created": "არ შექმნილა",
  Delayed: "დაგვიანებულია",
  Cancelled: "გაუქმებულია",
};

const tones: Record<OperationalStatus, string> = {
  Paid: "success",
  Pending: "warning",
  Failed: "danger",
  Ready: "success",
  Generating: "info",
  Review: "warning",
  "Not required": "neutral",
  "Ready for print": "info",
  "In production": "purple",
  Packed: "purple",
  Shipped: "info",
  Delivered: "success",
  "Not created": "neutral",
  Delayed: "danger",
  Cancelled: "danger",
};

export function StatusBadge({ value }: { value: OperationalStatus }) {
  return <span className={`status-badge ${tones[value]}`}>{labels[value]}</span>;
}
