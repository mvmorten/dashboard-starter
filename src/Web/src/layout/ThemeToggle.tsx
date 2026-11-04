import { useState } from "react";
import { applyTheme, nextTheme, readTheme, type Theme } from "../theme";
import { MoonIcon, SunIcon, SystemIcon } from "./icons";

const labels: Record<Theme, string> = {
  system: "system",
  light: "light",
  dark: "dark",
};

interface ThemeToggleProps {
  // In the user menu the button shows its label next to the icon; on the sign-in page,
  // where space is short, it's the icon alone.
  withLabel?: boolean;
}

// One button that cycles system, light, dark. The label says what is on now and what a
// click does, since the icon alone doesn't.
export default function ThemeToggle({ withLabel = false }: ThemeToggleProps) {
  const [theme, setTheme] = useState<Theme>(readTheme);
  const next = nextTheme(theme);

  function cycle() {
    applyTheme(next);
    setTheme(next);
  }

  const icon = (
    <>
      {theme === "system" && <SystemIcon />}
      {theme === "light" && <SunIcon />}
      {theme === "dark" && <MoonIcon />}
    </>
  );

  if (withLabel) {
    return (
      <button
        type="button"
        className="menu-option"
        aria-label={`Theme: ${labels[theme]}. Switch to ${labels[next]}.`}
        onClick={cycle}
      >
        {icon}
        Theme: {labels[theme]}
      </button>
    );
  }

  return (
    <button
      type="button"
      className="icon-button"
      aria-label={`Theme: ${labels[theme]}. Switch to ${labels[next]}.`}
      onClick={cycle}
    >
      {icon}
    </button>
  );
}
