import { createContext } from "react";

/**
 * Which child the storybook's back-cover "make another book" invitation is for.
 *
 * A context rather than a prop, because the back cover is four component layers down inside
 * the volume and the screen that knows the child is the reader above it. Without it the
 * invitation was a hard link to a blank form: a full page load that forgot the world, the child
 * and the draft, on the one screen where the parent has just finished reading that child's book.
 */
export const NewBookCharacterContext = createContext<string | null>(null);
