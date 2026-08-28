import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { LegalDocument } from "@/components/legal/LegalDocument";
import { refundsIntro, refundsSections } from "@/content/legal/refunds";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/refunds")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `მიწოდება და დაბრუნება — ${BRAND_NAME}`,
      description:
        "როგორ მიდის შეკვეთა, რა ვადებში ბარდება ნაბეჭდი წიგნი, როგორ ხდება გაუქმება და თანხის დაბრუნება.",
      path: "/refunds",
    });
    return { meta, links };
  },
  component: RefundsPage,
});

function RefundsPage() {
  return (
    <LegalPageShell>
      <LegalDocument
        title="მიწოდება, გაუქმება და თანხის დაბრუნება"
        intro={refundsIntro}
        sections={refundsSections}
      />
    </LegalPageShell>
  );
}
