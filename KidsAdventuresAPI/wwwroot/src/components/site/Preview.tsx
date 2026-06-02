import story from "@/assets/preview-story.jpg";
import puzzle from "@/assets/preview-puzzle.jpg";
import cert from "@/assets/preview-certificate.jpg";

const pages = [
  { title: "Personalized story", desc: "Your child as the hero of every chapter.", img: story },
  { title: "Puzzles & activities", desc: "Mazes, word searches and quizzes.", img: puzzle },
  { title: "Achievement certificate", desc: "A proud finish to the adventure.", img: cert },
];

export function Preview() {
  return (
    <section id="preview" className="relative py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-6">
        <div className="max-w-2xl">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">
            Inside a pack
          </p>
          <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            A complete adventure, beautifully printable.
          </h2>
        </div>

        <div className="mt-14 grid md:grid-cols-3 gap-8">
          {pages.map((p, i) => (
            <div key={p.title} className="group">
              <div
                className="rounded-3xl border border-border bg-card overflow-hidden shadow-card hover:-translate-y-1 transition"
                style={{ transform: `rotate(${i === 1 ? 1 : i === 2 ? -1.5 : 1.5}deg)` }}
              >
                <img
                  src={p.img}
                  alt={p.title}
                  loading="lazy"
                  width={1024}
                  height={1280}
                  className="w-full h-auto"
                />
              </div>
              <div className="mt-6">
                <div className="text-xs font-semibold text-muted-foreground">PAGE {i + 1}</div>
                <h3 className="mt-1 font-display text-2xl font-semibold">{p.title}</h3>
                <p className="text-muted-foreground mt-1">{p.desc}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
