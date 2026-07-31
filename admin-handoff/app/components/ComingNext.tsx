import Link from "next/link";
import { AdminShell } from "./AdminShell";

export function ComingNext({
  active,
  title,
  description,
}: {
  active: string;
  title: string;
  description: string;
}) {
  return (
    <AdminShell active={active} title={title} subtitle={description}>
      <section className="panel coming-next">
        <span className="coming-mark">UX</span>
        <h2>შემდეგი clickable workflow</h2>
        <p>
          გვერდის სტრუქტურა უკვე განსაზღვრულია და დაემატება მიმდინარე UX
          ეტაპის შემდეგ ნაწილში.
        </p>
        <Link className="button button-secondary" href="/">
          Overview-ზე დაბრუნება
        </Link>
      </section>
    </AdminShell>
  );
}
