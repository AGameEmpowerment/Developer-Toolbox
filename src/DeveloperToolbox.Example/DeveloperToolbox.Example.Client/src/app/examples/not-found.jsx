import Link from "next/link";
import NotFoundContent from "@/components/NotFound";

export default function NotFound() {
  return (
    <NotFoundContent
      title="The requested example was not found."
      content={
        <p>
          Return to the <Link href="/examples">example catalog</Link>.
        </p>
      }
    />
  );
}
