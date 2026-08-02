import { Sparkles } from "lucide-react";

import { PasswordlessAuthPanel } from "@/components/auth/PasswordlessAuthPanel";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { useT } from "@/lib/i18n";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess?: () => void;
  /** Where a magic link should land once followed. Defaults to the dashboard. */
  returnPath?: string;
};

/**
 * Sign-in for entry points outside the create journey (dashboard, blog nav).
 *
 * It hosts the same panel the journey uses, so a parent who signed up at checkout meets the
 * identical providers, language and wording when they come back later.
 */
export function PasswordlessAuthDialog({
  open,
  onOpenChange,
  onSuccess,
  returnPath = "/dashboard",
}: Props) {
  const t = useT();
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="ux-auth-dialog">
        {/* The visible heading is inside the panel; this names the dialog for screen readers. */}
        <DialogTitle className="sr-only">{t.common.nav.openDashboard}</DialogTitle>
        <PasswordlessAuthPanel
          returnPath={returnPath}
          onAuthenticated={() => {
            onSuccess?.();
            onOpenChange(false);
          }}
          header={
            <>
              <p className="eyebrow">
                <Sparkles aria-hidden="true" /> {t.journey.auth.eyebrow}
              </p>
              <h1>{t.common.nav.openDashboard}</h1>
              <p>{t.journey.auth.lead}</p>
            </>
          }
        />
      </DialogContent>
    </Dialog>
  );
}
