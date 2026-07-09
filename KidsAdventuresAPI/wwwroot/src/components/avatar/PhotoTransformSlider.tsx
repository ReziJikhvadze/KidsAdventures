import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import { createAdventurerDataUri } from "@/lib/avatar/createAdventurerSvg";
import { applyPreset, THEME_AVATAR_PRESETS } from "@/lib/avatar/presets";

/**
 * Interactive before/after: photo-like portrait → Pixar-style Adventurer hero.
 * Drag the handle or let the loop sell the transformation.
 */
export function PhotoTransformSlider({ className = "" }: { className?: string }) {
  const [pos, setPos] = useState(38);
  const [dragging, setDragging] = useState(false);
  const trackRef = useRef<HTMLDivElement>(null);
  const heroUri = useRef(
    createAdventurerDataUri(applyPreset(THEME_AVATAR_PRESETS.space[0]), {
      transparentBackground: false,
      size: 280,
    }),
  );

  useEffect(() => {
    if (dragging) return;
    let raf = 0;
    const start = performance.now();
    const tick = (now: number) => {
      const t = ((now - start) % 3200) / 3200;
      const wave =
        t < 0.15
          ? 28
          : t < 0.45
            ? 28 + ((t - 0.15) / 0.3) * 52
            : t < 0.7
              ? 80
              : 80 - ((t - 0.7) / 0.3) * 52;
      setPos(wave);
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [dragging]);

  const setFromClientX = (clientX: number) => {
    const el = trackRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const next = ((clientX - rect.left) / rect.width) * 100;
    setPos(Math.min(92, Math.max(8, next)));
  };

  const onPointerDown = (e: ReactPointerEvent) => {
    e.stopPropagation();
    e.preventDefault();
    setDragging(true);
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
    setFromClientX(e.clientX);
  };

  const onPointerMove = (e: ReactPointerEvent) => {
    if (!dragging) return;
    e.stopPropagation();
    setFromClientX(e.clientX);
  };

  const onPointerUp = (e: ReactPointerEvent) => {
    e.stopPropagation();
    setDragging(false);
  };

  return (
    <div
      className={`photo-transform-slider ${className}`}
      ref={trackRef}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      onClick={(e) => e.stopPropagation()}
      role="slider"
      aria-label="Drag to reveal Pixar-style hero from photo"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(pos)}
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === "ArrowLeft") setPos((p) => Math.max(8, p - 5));
        if (e.key === "ArrowRight") setPos((p) => Math.min(92, p + 5));
      }}
    >
      <div className="photo-transform-layer photo-transform-hero">
        <img src={heroUri.current} alt="" draggable={false} />
        <span className="photo-transform-tag photo-transform-tag-hero">Story hero</span>
      </div>

      <div
        className="photo-transform-layer photo-transform-photo"
        style={{ clipPath: `inset(0 ${100 - pos}% 0 0)` }}
      >
        <div className="photo-transform-photo-art">
          <div className="photo-face" />
          <div className="photo-hair" />
          <div className="photo-cheek photo-cheek-l" />
          <div className="photo-cheek photo-cheek-r" />
          <div className="photo-eye photo-eye-l" />
          <div className="photo-eye photo-eye-r" />
          <div className="photo-smile" />
        </div>
        <span className="photo-transform-tag">Photo</span>
      </div>

      <div className="photo-transform-handle" style={{ left: `${pos}%` }}>
        <span className="photo-transform-handle-line" />
        <span className="photo-transform-handle-knob" />
      </div>
    </div>
  );
}
