import SharedLayoutDisclaimer from "@/components/SharedLayoutDisclaimer";

export default function ExamplesLayout({ children }) {
  return (
    <>
      <SharedLayoutDisclaimer />
      {children}
    </>
  );
}
