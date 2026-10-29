// Placeholder logo: a plain circle in the text color (black in light, white in dark).
// Decorative: the brand text next to it carries the name. The same drawing is in
// public/favicon.svg, which can't use currentColor, so it sets its own light and dark
// colors. Replace both when the real logo exists.
export default function Logo() {
  return (
    <svg
      className="logo"
      viewBox="0 0 32 32"
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="16" cy="16" r="14" />
    </svg>
  );
}
