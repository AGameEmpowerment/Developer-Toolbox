import Link from "next/link";
import "./NotFound.css";

export default function NotFoundContent({
  title = "The page you requested was not found.",
  content = (
    <p>
      Check the address or <Link href="/">return home</Link>.
    </p>
  ),
}) {
  return (
    <section className="not-found" aria-labelledby="not-found-title">
      <p className="not-found__code">Error 404</p>
      <h1 id="not-found-title">{title}</h1>
      {content}
    </section>
  );
}
