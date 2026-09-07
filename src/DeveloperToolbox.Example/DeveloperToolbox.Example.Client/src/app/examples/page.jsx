import Link from "next/link";
import { getExamples } from "@/utils/examples";

export const metadata = {
  title: "Examples",
  description: "Generic local examples for dynamic routes.",
};

export default function Examples() {
  const examples = getExamples();

  return (
    <>
      <h1>Examples</h1>
      <p>
        This self-contained catalog demonstrates a dynamic App Router route.
      </p>
      <ul className="card-list">
        {examples.map(({ id, name, summary }) => (
          <li className="card" key={id}>
            <h2>
              <Link href={`/examples/${id}`}>{name}</Link>
            </h2>
            <p>{summary}</p>
          </li>
        ))}
      </ul>
    </>
  );
}
