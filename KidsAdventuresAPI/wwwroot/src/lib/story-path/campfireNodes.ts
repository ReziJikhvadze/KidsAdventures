/** Story Path campfires only after these node indices (0-based) — pages 2 and 6. */
export const CAMPFIRE_NODE_INDICES = new Set([1, 5]);

export function isCampfireNode(nodeIndex: number): boolean {
  return CAMPFIRE_NODE_INDICES.has(nodeIndex);
}
