import { t } from "@/lib/i18n";

import { ArrowIcon, SparkleIcon } from "./icons";

export function Announcement() {
  return (
    <div className="landing-v3-announcement">
      <span>
        <SparkleIcon />
        {t.landing.announcement}
      </span>
      <a href="#books">
        {t.landing.announcementLink}
        <ArrowIcon />
      </a>
    </div>
  );
}
