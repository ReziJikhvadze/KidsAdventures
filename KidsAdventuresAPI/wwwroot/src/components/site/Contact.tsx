import { useState } from "react";
import { Loader2, Mail, Send } from "lucide-react";

import { submitContactForm } from "@/lib/api/contact";
import { notify } from "@/lib/ui/notify";
import { BRAND_NAME } from "@/lib/brand";
import { SocialLinks } from "@/components/brand/SocialLinks";
import { useT } from "@/lib/i18n";

export function Contact() {
  const c = useT().common.contactForm;
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [company, setCompany] = useState("");
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);

  const canSubmit =
    name.trim().length > 0 && email.trim().length > 0 && message.trim().length > 0 && !sending;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;

    setSending(true);
    try {
      const result = await submitContactForm({
        name: name.trim(),
        email: email.trim(),
        message: message.trim(),
        company: company.trim() || undefined,
      });
      setSent(true);
      setName("");
      setEmail("");
      setMessage("");
      notify.success(result.message);
    } catch (err) {
      notify.fromError(err, c.failed);
    } finally {
      setSending(false);
    }
  };

  return (
    /*
      One screen, send included.

      The page was built on a long-form rhythm — a tall header block, a card with generous
      padding, and section padding on top of both — which pushed the send button below the fold
      on a laptop. Everything here is one short form; a visitor should be able to see the button
      they are being asked to press.
    */
    <section className="py-6 md:py-8">
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <div className="text-center max-w-xl mx-auto">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">{c.eyebrow}</p>
          <h1 className="mt-2 font-display text-2xl md:text-3xl font-bold text-balance">
            {c.title}
          </h1>
          {/*
            The lead sentence went. It asked whether the visitor had a question about stories,
            printing or delivery — on a page that is a contact form, where the only reason to be
            is a question, and the form's own fields say the rest.
          */}
          <SocialLinks className="mt-4 justify-center" />
        </div>

        <div className="mt-5 rounded-3xl border border-border bg-card shadow-card p-5 md:p-6">
          {sent ? (
            <div className="text-center py-8">
              <div className="mx-auto h-14 w-14 rounded-2xl bg-primary/10 grid place-items-center">
                <Mail className="h-7 w-7 text-primary" />
              </div>
              <p className="mt-5 font-display text-xl font-semibold">{c.sentTitle}</p>
              <p className="mt-2 text-sm text-muted-foreground max-w-sm mx-auto">
                {c.sentBody(BRAND_NAME)}
              </p>
              <button
                type="button"
                onClick={() => setSent(false)}
                className="mt-6 text-sm font-semibold text-primary hover:underline"
              >
                {c.sendAnother}
              </button>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label htmlFor="contact-name" className="text-sm font-semibold">
                    {c.nameLabel}
                  </label>
                  <input
                    id="contact-name"
                    value={name}
                    maxLength={100}
                    onChange={(e) => setName(e.target.value)}
                    placeholder={c.namePlaceholder}
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                    required
                  />
                </div>
                <div>
                  <label htmlFor="contact-email" className="text-sm font-semibold">
                    {c.emailLabel}
                  </label>
                  <input
                    id="contact-email"
                    type="email"
                    value={email}
                    maxLength={200}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder={c.emailPlaceholder}
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                    required
                  />
                </div>
              </div>

              <div>
                <label htmlFor="contact-message" className="text-sm font-semibold">
                  {c.messageLabel}
                </label>
                <textarea
                  id="contact-message"
                  value={message}
                  maxLength={2000}
                  rows={6}
                  onChange={(e) => setMessage(e.target.value)}
                  placeholder={c.messagePlaceholder}
                  className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 text-sm outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition resize-none"
                  required
                />
              </div>

              <div className="hidden" aria-hidden="true">
                <label htmlFor="contact-company">Company</label>
                <input
                  id="contact-company"
                  tabIndex={-1}
                  autoComplete="off"
                  value={company}
                  onChange={(e) => setCompany(e.target.value)}
                />
              </div>

              <button
                type="submit"
                disabled={!canSubmit}
                className="w-full inline-flex items-center justify-center gap-2 rounded-full bg-primary text-primary-foreground py-4 font-semibold disabled:opacity-40 disabled:cursor-not-allowed hover:opacity-90 transition"
              >
                {sending ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    {c.sending}
                  </>
                ) : (
                  <>
                    <Send className="h-4 w-4" />
                    {c.send}
                  </>
                )}
              </button>
            </form>
          )}
        </div>
      </div>
    </section>
  );
}
