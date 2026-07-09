export type PathPoint = { x: number; y: number };

/**
 * Builds a smooth SVG path `d` string that passes exactly through every point
 * (Catmull-Rom -> cubic Bezier conversion). Keeping the drawn trail derived
 * from the same coordinates as the chapter nodes guarantees they can never
 * drift out of sync with each other.
 */
export function buildSmoothPathD(points: PathPoint[]): string {
  if (points.length === 0) return "";
  if (points.length === 1) return `M ${points[0].x} ${points[0].y}`;

  const pad = [points[0], ...points, points[points.length - 1]];
  const segments: string[] = [`M ${points[0].x} ${points[0].y}`];

  for (let i = 1; i < pad.length - 2; i++) {
    const p0 = pad[i - 1];
    const p1 = pad[i];
    const p2 = pad[i + 1];
    const p3 = pad[i + 2];

    const c1x = p1.x + (p2.x - p0.x) / 6;
    const c1y = p1.y + (p2.y - p0.y) / 6;
    const c2x = p2.x - (p3.x - p1.x) / 6;
    const c2y = p2.y - (p3.y - p1.y) / 6;

    segments.push(`C ${c1x.toFixed(2)} ${c1y.toFixed(2)}, ${c2x.toFixed(2)} ${c2y.toFixed(2)}, ${p2.x} ${p2.y}`);
  }

  return segments.join(" ");
}
