import Link from "next/link";
import { notFound } from "next/navigation";
import { getExampleById, getExamples } from "@/utils/examples";

export function generateStaticParams() {
  return getExamples().map(({ id }) => ({ id }));
}

export async function generateMetadata({ params }) {
  const { id } = await params;
  const example = getExampleById(id);

  return example
    ? { title: example.name, description: example.summary }
    : { title: "Example not found" };
}

export default async function ExampleDetails({ params }) {
  const { id } = await params;
  const example = getExampleById(id);

  if (!example) {
    notFound();
  }

  return (
    <article>
      <h1>{example.name}</h1>
      <p>{example.summary}</p>
      <h2>What it demonstrates</h2>
      <ul>
        {example.topics.map((topic) => (
          <li key={topic}>{topic}</li>
        ))}
      </ul>
      <p>
        <Link href="/examples">Back to examples</Link>
      </p>
    </article>
  );
}
