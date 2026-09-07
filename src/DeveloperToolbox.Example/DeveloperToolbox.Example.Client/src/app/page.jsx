import Link from "next/link";
import { headers } from "next/headers";
import { getDictionary } from "@/dictionaries";
import { DEFAULT_LOCALE } from "@/utils/i18n";

export default async function Home() {
  const requestHeaders = await headers();
  const language = requestHeaders.get("x-lang") || DEFAULT_LOCALE;
  const dictionary = await getDictionary(language);

  return (
    <>
      <h1>{dictionary.welcome}</h1>
      <p>{dictionary.features}</p>
      <ul>
        <li>
          <a href="https://github.com/AGameEmpowerment/Developer-Toolbox">
            {dictionary.links.toolbox}
          </a>
        </li>
        <li>
          <a href="https://nextjs.org/docs/app">
            {dictionary.links.nextDocumentation}
          </a>
        </li>
      </ul>
      <p>
        <Link href="/examples">{dictionary.seeMoreExamples}</Link>
      </p>
    </>
  );
}
