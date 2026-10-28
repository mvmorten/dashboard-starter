import { useState } from "react";
import { applyTheme, nextTheme, readTheme, type Theme } from "../theme";
import { MoonIcon, SunIcon, SystemIcon } from "./icons";

const labels: Record<Theme, string> = {
  system: "system",
  light: "light",
  dark: "dark",
};

// One button that cycles system, light, dark. The label says what is on now and what a
// click does, since the icon alone doesn't.
export default function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(readTheme);
  const next = nextTheme(theme);

  function cycle() {
    applyTheme(next);
    setTheme(next);
  }

  return (
    <button
      type="button"
      className="icon-button"
      aria-label={`Theme: ${labels[theme]}. Switch to ${labels[next]}.`}
      onClick={cycle}
    >
      {theme === "system" && <SystemIcon />}
      {theme === "light" && <SunIcon />}
      {theme === "dark" && <MoonIcon />}
    </button>
  );
}
