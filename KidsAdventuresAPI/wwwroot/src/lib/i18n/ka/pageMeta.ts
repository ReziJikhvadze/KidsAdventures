/**
 * Visible browser-tab titles, keyed by route path.
 *
 * Route `head:` functions run outside React and cannot read the locale, so they
 * still emit the Georgian canonical meta for crawlers. These entries drive the
 * client-side title swap once the interface language is known — see
 * `LocalizedDocumentTitle`.
 */
export const pageMeta: Record<string, string> = {
  "/": "პერსონალიზებული საბავშვო წიგნები",
  "/create": "შექმენი წიგნი",
  "/world": "ბავშვის სამყარო",
  "/themes": "აირჩიე სამყარო",
  "/about": "ჩვენ შესახებ",
  "/refunds": "მიწოდება და დაბრუნება",
  "/contact": "კონტაქტი",
  "/dashboard": "მშობლის სივრცე",
  "/privacy": "კონფიდენციალურობა",
  "/terms": "წესები და პირობები",
  "/reader": "Online Reader",
  "/book": "თავგადასავალი აქ არ მთავრდება",
  "/my-packs": "ჩემი წიგნები",
  "/auth/magic": "შესვლა",
  "/confirm-email": "ელფოსტის დადასტურება",
  "/admin": "Admin · Overview",
  "/admin/orders": "Admin · შეკვეთები",
};
