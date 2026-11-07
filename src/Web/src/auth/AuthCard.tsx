import type { ReactNode } from "react";
import Logo from "../layout/Logo";
import ThemeToggle from "../layout/ThemeToggle";

// The frame of the pages you see before signing in: sign in, forgot password, and reset
// password. A card on its own, without the app shell.
export default function AuthCard({
  title,
  children,
}: {
  title: string;
  children: ReactNode;
}) {
  return (
    <main className="sign-in">
      <div className="sign-in-card">
        <div className="sign-in-top">
          <p className="sign-in-brand">
            <Logo />
            Dashboard Starter
          </p>
          <ThemeToggle />
        </div>
        <h1>{title}</h1>
        {children}
      </div>
    </main>
  );
}
