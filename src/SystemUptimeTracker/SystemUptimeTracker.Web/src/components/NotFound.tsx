"use client";

import Link from "next/link";
import styled from "styled-components";

import { H2, Text } from "@/components/generic/ui/generic-ui";

const Container = styled.div`
  text-align: center;
  font-size: 1.125rem;
  padding: 1.5rem;
`;

const ErrorCode = styled(Text)`
  color: #57606a;
`;

const Disclaimer = styled.div`
  border: 1px dotted;
  max-inline-size: 37.5rem;
  margin-inline: auto;
  padding: 1.5rem;
  font-size: 1rem;
`;

export default function NotFoundContent({
  title = "Sorry, we could not find the page you were looking for.",
  content = (
    <>
      <p>Please check that the address is correct.</p>
      <p>
        <Link href="/">Return Home</Link>
      </p>
    </>
  ),
}) {
  return (
    <Container>
      <ErrorCode renderAs="h1">Error code: 404</ErrorCode>
      <H2>{title}</H2>
      {content}

      <Disclaimer>
        <p>
          You should update this &ldquo;re-engagment point&rdquo; with something
          appropriate for your application. For more information, please
          reference{" "}
          <a
            href="https://nextjs.org/docs/app/getting-started/error-handling"
            target="_blank"
            rel="noreferrer"
          >
            the Next.js documentation for error handling.
          </a>
        </p>
      </Disclaimer>
    </Container>
  );
}
