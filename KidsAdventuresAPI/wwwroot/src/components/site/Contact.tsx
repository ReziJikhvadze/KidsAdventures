import { useState } from "react";
import { Loader2, Mail, Send } from "lucide-react";

import { submitContactForm } from "@/lib/api/contact";
import { notify } from "@/lib/ui/notify";
import { BRAND_NAME } from "@/lib/brand";
import { SocialLinks } from "@/components/brand/SocialLinks";

export function Contact() {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [company, setCompany] = useState("");
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);

  const canSubmit =
    name.trim().length > 0 &&
    email.trim().length > 0 &&
    message.trim().length > 0 &&
    !sending;

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
      notify.fromError(err, "Could not send your message.");
    } finally {
      setSending(false);
    }
  };

  return (
    <section className="py-16 md:py-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <div className="text-center max-w-xl mx-auto">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">კონტაქტი</p>
          <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            მოხარული ვიქნებით შენი წერილის
          </h1>
          <p className="mt-4 text-muted-foreground">
            კითხვები ამბების, ბეჭდვის ან მიწოდების შესახებ? მოგვწერე — პასუხს ელფოსტაზე მიიღებ.
          </p>
          <SocialLinks className="mt-6 justify-center" />
        </div>

        <div className="mt-10 rounded-3xl border border-border bg-card shadow-card p-6 md:p-10">
          {sent ? (
            <div className="text-center py-8">
              <div className="mx-auto h-14 w-14 rounded-2xl bg-primary/10 grid place-items-center">
                <Mail className="h-7 w-7 text-primary" />
              </div>
              <p className="mt-5 font-display text-xl font-semibold">Message sent</p>
              <p className="mt-2 text-sm text-muted-foreground max-w-sm mx-auto">
                Thanks for reaching out. The {BRAND_NAME} team will get back to you at the email you provided.
              </p>
              <button
                type="button"
                onClick={() => setSent(false)}
                className="mt-6 text-sm font-semibold text-primary hover:underline"
              >
                Send another message
              </button>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="space-y-5">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label htmlFor="contact-name" className="text-sm font-semibold">
                    Your name
                  </label>
                  <input
                    id="contact-name"
                    value={name}
                    maxLength={100}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Ana"
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                    required
                  />
                </div>
                <div>
                  <label htmlFor="contact-email" className="text-sm font-semibold">
                    Email
                  </label>
                  <input
                    id="contact-email"
                    type="email"
                    value={email}
                    maxLength={200}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="you@example.com"
                    className="mt-2 w-full rounded-xl border border-border bg-background px-4 py-3 outline-none focus:border-primary focus:ring-4 focus:ring-primary/10 transition"
                    required
                  />
                </div>
              </div>

              <div>
                <label htmlFor="contact-message" className="text-sm font-semibold">
                  Message
                </label>
                <textarea
                  id="contact-message"
                  value={message}
                  maxLength={2000}
                  rows={6}
                  onChange={(e) => setMessage(e.target.value)}
                  placeholder="How can we help?"
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
                    Sending…
                  </>
                ) : (
                  <>
                    <Send className="h-4 w-4" />
                    Send message
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
