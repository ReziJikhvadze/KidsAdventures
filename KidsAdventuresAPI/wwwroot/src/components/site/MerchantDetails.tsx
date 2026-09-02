import { merchantContactRows } from "@/lib/merchant";

/**
 * The merchant's identity, address, phone and email, in plain text.
 *
 * A contact form is not contact details: a customer about to enter a card number, and the
 * acquiring bank reviewing the site, both need to see who they would be paying and how to
 * reach them without submitting anything first.
 *
 * Rows with nothing to show are dropped by `merchantContactRows`, so an unpublished phone
 * number is absent rather than an empty line — and the whole block disappears if none of it
 * has been filled in yet.
 *
 * A card, not a section: it stands beside the contact form now rather than under it, so the
 * width, the centring and the page's own spacing belong to the column it is placed in.
 */
export function MerchantDetails() {
  const rows = merchantContactRows();
  if (rows.length === 0) return null;

  return (
    <div className="min-w-0 rounded-3xl border border-border bg-card shadow-card p-5 md:p-6">
      <h2 className="font-display text-lg font-semibold">რეკვიზიტები</h2>
      <dl className="mt-4 grid gap-3 sm:grid-cols-[max-content_1fr] sm:gap-x-6">
        {rows.map((row) => (
          <div key={row.label} className="contents">
            <dt className="text-sm text-muted-foreground">{row.label}</dt>
            <dd className="text-sm font-medium">
              {row.href ? (
                <a href={row.href} className="hover:text-primary transition">
                  {row.value}
                </a>
              ) : (
                row.value
              )}
            </dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
