import { useCallback, useRef, useState } from "react";
import { cn } from "@/lib/utils";

const HOLD_MS = 1500;

type HoldToConfirmButtonProps = {
  onConfirm: () => void;
  disabled?: boolean;
  className?: string;
};

export function HoldToConfirmButton({ onConfirm, disabled, className }: HoldToConfirmButtonProps) {
  const [progress, setProgress] = useState(0);
  const [holding, setHolding] = useState(false);
  const timerRef = useRef<number | null>(null);
  const startRef = useRef<number | null>(null);
  const frameRef = useRef<number | null>(null);

  const clearTimers = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    if (frameRef.current !== null) {
      cancelAnimationFrame(frameRef.current);
      frameRef.current = null;
    }
    startRef.current = null;
    setHolding(false);
    setProgress(0);
  }, []);

  const tick = useCallback(() => {
    if (startRef.current === null) return;
    const elapsed = Date.now() - startRef.current;
    const next = Math.min(elapsed / HOLD_MS, 1);
    setProgress(next);
    if (next >= 1) {
      clearTimers();
      onConfirm();
      return;
    }
    frameRef.current = requestAnimationFrame(tick);
  }, [clearTimers, onConfirm]);

  const startHold = useCallback(() => {
    if (disabled) return;
    setHolding(true);
    startRef.current = Date.now();
    frameRef.current = requestAnimationFrame(tick);
    timerRef.current = window.setTimeout(() => {
      clearTimers();
      onConfirm();
    }, HOLD_MS);
  }, [clearTimers, disabled, onConfirm, tick]);

  const endHold = useCallback(() => {
    clearTimers();
  }, [clearTimers]);

  return (
    <button
      type="button"
      disabled={disabled}
      onMouseDown={startHold}
      onMouseUp={endHold}
      onMouseLeave={endHold}
      onTouchStart={startHold}
      onTouchEnd={endHold}
      onTouchCancel={endHold}
      className={cn(
        "relative min-h-11 w-full max-w-xs overflow-hidden rounded-full border-2 border-primary bg-card px-6 py-3 text-sm font-semibold text-foreground shadow-soft transition",
        holding && "border-primary/80",
        disabled && "cursor-not-allowed opacity-50",
        className,
      )}
      aria-label="Hold to confirm — grown-ups only"
    >
      <span
        className="absolute inset-y-0 left-0 bg-primary/20 motion-safe:transition-[width] motion-reduce:transition-none"
        style={{ width: `${progress * 100}%` }}
        aria-hidden
      />
      <span className="relative z-10">
        {holding ? "Keep holding…" : "Hold to confirm — grown-ups"}
      </span>
    </button>
  );
}
