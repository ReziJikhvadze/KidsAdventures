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
 */
export function MerchantDetails() {
  const rows = merchantContactRows();
  if (rows.length === 0) return null;

  return (
    <section className="mx-auto max-w-3xl px-4 sm:px-6 pb-12">
      <div className="rounded-3xl border border-border bg-card shadow-card p-5 md:p-6">
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
    </section>
  );
}
