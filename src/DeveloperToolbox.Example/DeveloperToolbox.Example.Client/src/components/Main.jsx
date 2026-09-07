import "./Main.css";

export default function Main({ children }) {
  return (
    <main id="main-content" className="main">
      {children}
    </main>
  );
}
