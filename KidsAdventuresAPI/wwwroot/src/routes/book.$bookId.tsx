import { createFileRoute } from "@tanstack/react-router";

import { SharedBookScreen } from "@/components/adventrya/reader/SharedBookScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

/**
 * QR target printed on the last page of every book. It is public on purpose:
 * whoever holds the physical book can open the story and start the next chapter.
 */
export const Route = createFileRoute("/book/$bookId")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `თავგადასავალი აქ არ მთავრდება — ${BRAND_NAME}`,
      description: "დაასკანერე და გააგრძელე ბავშვის სამყარო შემდეგი თავგადასავლით.",
      path: "/book",
      noindex: true,
    });
    return { meta, links };
  },
  component: SharedBookScreen,
});
