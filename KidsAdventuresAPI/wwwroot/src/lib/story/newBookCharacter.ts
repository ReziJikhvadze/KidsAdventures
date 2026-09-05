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

/**
 * Where the picker should send the reader back to, when that invitation is taken.
 *
 * The same four layers down, and the same reason: only the screen holding the book knows where
 * the book is being held. `/themes` reads this out of its own address — see `backHrefFromSearch`
 * — and without it every route in looked identical, so its arrow answered `/#worlds` to everyone.
 * On the home page that meant pressing "a new adventure" inside the hero's storybook and being
 * returned to a different part of the page than the one it was pressed on.
 *
 * Null keeps the old answer, which is right for the screens that are not a section of a page.
 */
export const NewBookReturnContext = createContext<string | null>(null);
