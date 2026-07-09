import { useMemo } from "react";
import type { AvatarConfig } from "@/lib/avatar/config";
import { createAdventurerDataUri } from "@/lib/avatar/createAdventurerSvg";

type PixarAvatarPreviewProps = {
  config: AvatarConfig;
  childName: string;
  size?: "hero" | "compact";
  className?: string;
};

function hex(value: string): string {
  return `#${value.replace("#", "")}`;
}

function shade(raw: string, amount: number): string {
  const n = raw.replace("#", "");
  if (n.length !== 6) return hex(n);
  const r = Math.min(255, Math.max(0, parseInt(n.slice(0, 2), 16) + amount));
  const g = Math.min(255, Math.max(0, parseInt(n.slice(2, 4), 16) + amount));
  const b = Math.min(255, Math.max(0, parseInt(n.slice(4, 6), 16) + amount));
  return `#${r.toString(16).padStart(2, "0")}${g.toString(16).padStart(2, "0")}${b.toString(16).padStart(2, "0")}`;
}

/**
 * Full-body hero: Adventurer face in a soft circular frame, seated on a
 * connected cartoon body (arms, hands, legs, shoes). Static — no bounce.
 */
export function PixarAvatarPreview({
  config,
  childName,
  size = "hero",
  className = "",
}: PixarAvatarPreviewProps) {
  const name = childName.trim() || "Hero";
  const isHero = size === "hero";
  const headUri = useMemo(
    () => createAdventurerDataUri(config, { transparentBackground: true, size: 512 }),
    [config],
  );

  const skin = hex(config.skinColor);
  const skinDeep = shade(skin, -28);
  const skinSoft = shade(skin, 24);
  const accent = hex(config.outfitColor || "f07167");
  const accentDeep = shade(accent, -40);
  const accentSoft = shade(accent, 44);

  const pants =
    config.outfit === "astronaut"
      ? "#8fa0b5"
      : config.outfit === "captain"
        ? "#3a281c"
        : config.outfit === "superhero"
          ? "#23263c"
          : config.outfit === "party"
            ? accentDeep
            : config.outfit === "hoodie"
              ? "#455563"
              : "#4a6b52";

  const shoes =
    config.outfit === "astronaut"
      ? "#6f8094"
      : config.outfit === "party"
        ? accentDeep
        : config.outfit === "hoodie"
          ? "#1a2024"
          : "#241810";

  const uid = useMemo(
    () =>
      `fb-${config.hair}-${config.skinColor}-${config.outfit}-${config.outfitColor}`.replace(
        /[^a-z0-9-]/gi,
        "",
      ),
    [config],
  );

  const dress = config.outfit === "party" && config.gender === "girl";
  const shirtFill =
    config.outfit === "astronaut"
      ? "#e6edf6"
      : config.outfit === "superhero"
        ? "#2a2e48"
        : config.outfit === "captain"
          ? accentDeep
          : `url(#${uid}-shirt)`;

  return (
    <div
      className={`avatar-stage relative ${isHero ? "avatar-stage-hero" : "avatar-stage-compact"} ${className}`}
      aria-label={`${name}'s full-body hero preview`}
    >
      <div className="avatar-stage-card">
        <div className="avatar-fullbody-wrap">
          <svg viewBox="0 0 240 300" className="avatar-fullbody-svg" aria-hidden>
            <defs>
              <linearGradient id={`${uid}-shirt`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={accentSoft} />
                <stop offset="50%" stopColor={accent} />
                <stop offset="100%" stopColor={accentDeep} />
              </linearGradient>
              <linearGradient id={`${uid}-skin`} x1="0.2" y1="0" x2="0.8" y2="1">
                <stop offset="0%" stopColor={skinSoft} />
                <stop offset="100%" stopColor={skin} />
              </linearGradient>
              <linearGradient id={`${uid}-pants`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={shade(pants, 20)} />
                <stop offset="100%" stopColor={pants} />
              </linearGradient>
              <linearGradient id={`${uid}-shoe`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={shade(shoes, 26)} />
                <stop offset="100%" stopColor={shoes} />
              </linearGradient>
              <radialGradient id={`${uid}-floor`} cx="50%" cy="50%" r="50%">
                <stop offset="0%" stopColor="#0f172a" stopOpacity="0.15" />
                <stop offset="100%" stopColor="#0f172a" stopOpacity="0" />
              </radialGradient>
            </defs>

            <ellipse cx="120" cy="288" rx="64" ry="9" fill={`url(#${uid}-floor)`} />

            {config.outfit === "superhero" && (
              <path
                d="M84 128 C54 158 46 210 64 260 C94 242 146 242 176 260 C194 210 186 158 156 128 Z"
                fill={accent}
                stroke="#1a1a1a"
                strokeWidth="2.5"
              />
            )}

            {/* Arms — inked like Adventurer */}
            <g>
              <path
                d="M90 132
                   C72 142 58 160 54 182
                   C52 196 58 208 70 214
                   C80 218 90 214 92 204
                   C86 196 82 182 84 166
                   C86 148 94 136 106 132 Z"
                fill={`url(#${uid}-skin)`}
                stroke="#1a1a1a"
                strokeWidth="2.5"
                strokeLinejoin="round"
              />
              <ellipse cx="64" cy="216" rx="13" ry="11" fill={skin} stroke="#1a1a1a" strokeWidth="2.2" />
              <ellipse cx="54" cy="212" rx="3.2" ry="4.8" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="56" cy="222" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="63" cy="224" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="70" cy="221" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
            </g>
            <g>
              <path
                d="M150 132
                   C168 142 182 160 186 182
                   C188 196 182 208 170 214
                   C160 218 150 214 148 204
                   C154 196 158 182 156 166
                   C154 148 146 136 134 132 Z"
                fill={`url(#${uid}-skin)`}
                stroke="#1a1a1a"
                strokeWidth="2.5"
                strokeLinejoin="round"
              />
              <ellipse cx="176" cy="216" rx="13" ry="11" fill={skin} stroke="#1a1a1a" strokeWidth="2.2" />
              <ellipse cx="186" cy="212" rx="3.2" ry="4.8" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="184" cy="222" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="177" cy="224" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
              <ellipse cx="170" cy="221" rx="2.8" ry="4.2" fill={skin} stroke="#1a1a1a" strokeWidth="1.2" />
            </g>

            {/* Torso — inked outlines to match Adventurer head */}
            {config.outfit === "astronaut" ? (
              <g>
                <path
                  d="M78 128 C78 118 162 118 162 128 L170 196 C170 214 152 226 120 226 C88 226 70 214 70 196 Z"
                  fill="#e6edf6"
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <circle cx="120" cy="160" r="20" fill={accent} stroke="#1a1a1a" strokeWidth="2" />
                <circle cx="120" cy="160" r="11" fill={accentSoft} />
                <rect x="94" y="190" width="52" height="8" rx="4" fill={accentDeep} opacity="0.4" />
              </g>
            ) : config.outfit === "captain" ? (
              <g>
                <path
                  d="M76 126 L120 136 L164 126 L172 198 C172 216 152 228 120 228 C88 228 68 216 68 198 Z"
                  fill={accentDeep}
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <path d="M98 132 L120 142 L142 132 L138 168 L102 168 Z" fill={accent} opacity="0.55" />
                <rect x="108" y="146" width="24" height="10" rx="3" fill="#f0c14e" stroke="#1a1a1a" strokeWidth="1.5" />
              </g>
            ) : dress ? (
              <g>
                <path
                  d="M90 126 L120 136 L150 126 L184 218 C164 232 142 238 120 238 C98 238 76 232 56 218 Z"
                  fill={`url(#${uid}-shirt)`}
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <ellipse cx="120" cy="130" rx="34" ry="12" fill={accentSoft} stroke="#1a1a1a" strokeWidth="1.5" />
              </g>
            ) : config.outfit === "hoodie" ? (
              <g>
                <path
                  d="M76 126 C76 114 164 114 164 126 L172 196 C172 214 152 226 120 226 C88 226 68 214 68 196 Z"
                  fill={`url(#${uid}-shirt)`}
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <ellipse cx="120" cy="126" rx="28" ry="11" fill={accentDeep} opacity="0.25" />
                <path
                  d="M94 128 C108 150 132 150 146 128"
                  fill="none"
                  stroke="#1a1a1a"
                  strokeWidth="2"
                  opacity="0.35"
                  strokeLinecap="round"
                />
                <path d="M102 166 L138 166 L134 188 L106 188 Z" fill={accentDeep} opacity="0.18" stroke="#1a1a1a" strokeWidth="1.2" />
              </g>
            ) : config.outfit === "superhero" ? (
              <g>
                <path
                  d="M78 126 C78 114 162 114 162 126 L168 194 C168 212 150 224 120 224 C90 224 72 212 72 194 Z"
                  fill="#2a2e48"
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <polygon
                  points="120,146 127,162 145,162 131,174 136,192 120,181 104,192 109,174 95,162 113,162"
                  fill="#f0c14e"
                  stroke="#1a1a1a"
                  strokeWidth="1.5"
                />
              </g>
            ) : (
              <g>
                <path
                  d="M78 126 C78 114 162 114 162 126 L168 194 C168 212 150 224 120 224 C90 224 72 212 72 194 Z"
                  fill="#c4a07a"
                  stroke="#1a1a1a"
                  strokeWidth="2.5"
                />
                <path
                  d="M88 138 C88 128 152 128 152 138 L156 184 C156 196 140 206 120 206 C100 206 84 196 84 184 Z"
                  fill={`url(#${uid}-shirt)`}
                  stroke="#1a1a1a"
                  strokeWidth="2"
                />
                <circle cx="100" cy="160" r="3.6" fill="#fff" stroke="#1a1a1a" strokeWidth="1" />
                <circle cx="140" cy="160" r="3.6" fill="#fff" stroke="#1a1a1a" strokeWidth="1" />
                <circle cx="120" cy="174" r="3.6" fill="#fff" stroke="#1a1a1a" strokeWidth="1" />
                <rect x="88" y="196" width="64" height="8" rx="3" fill="#6b4f2a" stroke="#1a1a1a" strokeWidth="1.5" />
                <rect x="112" y="194" width="16" height="12" rx="2" fill="#f0c14e" stroke="#1a1a1a" strokeWidth="1.5" />
              </g>
            )}

            {/* Neck + shoulders — head circle sits on top of these */}
            <ellipse cx="120" cy="132" rx="17" ry="14" fill={`url(#${uid}-skin)`} stroke="#1a1a1a" strokeWidth="2" />
            <ellipse cx="120" cy="142" rx="42" ry="14" fill={shirtFill} stroke="#1a1a1a" strokeWidth="2.2" />
            {/* Soft shoulder pads peeking under chin */}
            <ellipse cx="88" cy="140" rx="14" ry="10" fill={shirtFill} stroke="#1a1a1a" strokeWidth="1.8" />
            <ellipse cx="152" cy="140" rx="14" ry="10" fill={shirtFill} stroke="#1a1a1a" strokeWidth="1.8" />

            {/* Legs */}
            {dress ? (
              <g>
                <rect x="102" y="228" width="14" height="40" rx="7" fill={`url(#${uid}-skin)`} stroke="#1a1a1a" strokeWidth="2" />
                <rect x="124" y="228" width="14" height="40" rx="7" fill={`url(#${uid}-skin)`} stroke="#1a1a1a" strokeWidth="2" />
              </g>
            ) : (
              <g>
                <ellipse cx="120" cy="220" rx="36" ry="12" fill={`url(#${uid}-pants)`} stroke="#1a1a1a" strokeWidth="2" />
                <path
                  d="M90 216 C84 216 84 226 84 234 L84 262 C84 272 92 278 101 278 C110 278 118 272 118 262 L118 234 C118 226 116 216 110 216 Z"
                  fill={`url(#${uid}-pants)`}
                  stroke="#1a1a1a"
                  strokeWidth="2.2"
                />
                <path
                  d="M122 216 C116 216 122 226 122 234 L122 262 C122 272 130 278 139 278 C148 278 156 272 156 262 L156 234 C156 226 154 216 148 216 Z"
                  fill={`url(#${uid}-pants)`}
                  stroke="#1a1a1a"
                  strokeWidth="2.2"
                />
              </g>
            )}

            {/* Shoes */}
            <ellipse cx="101" cy="278" rx="18" ry="10" fill={`url(#${uid}-shoe)`} stroke="#1a1a1a" strokeWidth="2.2" />
            <ellipse cx="139" cy="278" rx="18" ry="10" fill={`url(#${uid}-shoe)`} stroke="#1a1a1a" strokeWidth="2.2" />
            <ellipse cx="108" cy="276" rx="7" ry="4" fill={shade(shoes, 36)} opacity="0.45" />
            <ellipse cx="146" cy="276" rx="7" ry="4" fill={shade(shoes, 36)} opacity="0.45" />
          </svg>

          {/* Circular Adventurer face — always shows eyes/mouth */}
          <div className="avatar-head-layer">
            <img src={headUri} alt="" draggable={false} className="avatar-head-img" />
          </div>
        </div>

        {isHero && (
          <div className="avatar-caption">
            <p className="font-display text-base font-semibold text-foreground">{name}</p>
            <p className="text-[11px] text-muted-foreground">Your story hero · full body preview</p>
          </div>
        )}
      </div>
    </div>
  );
}
