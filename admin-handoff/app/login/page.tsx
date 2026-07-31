import Link from "next/link";

export default function LoginPage() {
  return (
    <main className="login-page">
      <section className="login-card">
        <div className="brand-row login-brand">
          <span className="brand-mark">A</span>
          <span><strong>ADVENTRYA</strong><small>Operations</small></span>
        </div>
        <p className="eyebrow">Secure workspace</p>
        <h1>ადმინისტრაციის პანელი</h1>
        <p>
          ეს არის clickable UX prototype. აირჩიეთ როლი შესაბამისი
          ოპერაციული ხედის შესამოწმებლად.
        </p>
        <div className="login-role-list">
          <Link href="/">
            <span className="login-role-icon">A</span>
            <span><strong>Adventrya Admin</strong><small>Orders, QA, Print, Delivery და Customers</small></span>
            <span>→</span>
          </Link>
          <Link href="/partner">
            <span className="login-role-icon partner">P</span>
            <span><strong>Print Partner</strong><small>მხოლოდ გაგზავნილი Print Job-ები</small></span>
            <span>→</span>
          </Link>
        </div>
        <small className="login-note">Mock access · რეალური authentication არ არის დაკავშირებული</small>
      </section>
      <aside className="login-aside">
        <span className="login-book theme-magic">
          <small>ADVENTRYA</small>
          <strong>ყოველი წიგნი გადის უსაფრთხო გზას</strong>
          <i>Generation → Review → Print → Delivery</i>
        </span>
        <h2>ერთი უწყვეტი ოპერაციული სისტემა</h2>
        <p>ყოველი სტატუსი ცალკეა, ყველა მოქმედება — თვალსაჩინო და კონტროლირებადი.</p>
      </aside>
    </main>
  );
}
