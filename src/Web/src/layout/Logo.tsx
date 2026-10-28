// The octopus. One color (currentColor), so it follows the theme like any text. Decorative:
// the brand text next to it carries the name. The same drawing is in public/favicon.svg,
// which can't use currentColor, so it sets its own light and dark colors. Keep the two in step.
export default function Logo() {
  return (
    <svg
      className="logo"
      viewBox="0 0 32 32"
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
    >
      <path
        fillRule="evenodd"
        d="M6 14C6 7.5 10.5 3 16 3S26 7.5 26 14c0 3-1.5 5-3 6H9c-1.5-1-3-3-3-6ZM10 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0ZM18 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0Z"
      />
      <g
        fill="none"
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
      >
        <path d="M9 19C9 23 5 23 5 27c0 1.5 1.5 2 2.5 1" />
        <path d="M12.5 19C12.5 24 10 25 10.5 29" />
        <path d="M16 19c-1.5 4 1.5 6 0 10" />
        <path d="M19.5 19C19.5 24 22 25 21.5 29" />
        <path d="M23 19C23 23 27 23 27 27c0 1.5-1.5 2-2.5 1" />
      </g>
    </svg>
  );
}
