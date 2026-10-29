// The logo: an app-shell window (top bar, sidebar, main area) cut out of a rounded block.
// It is built from one cell, c: the drawing is 9c wide and 6c tall, the frame and gaps are
// 1c, the sidebar is 2c wide and the main area 4c wide (a 1:2 split). One viewBox unit is
// c / 4 here, so c = 4. Decorative: the brand text next to it carries the name. The same
// drawing is in public/favicon.svg, which can't use currentColor, so it sets its own light
// and dark colors. Change both together.
export default function Logo() {
  return (
    <svg
      className="logo"
      viewBox="0 0 36 36"
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
    >
      <path
        fillRule="evenodd"
        d="M5 6H31A5 5 0 0 1 36 11V25A5 5 0 0 1 31 30H5A5 5 0 0 1 0 25V11A5 5 0 0 1 5 6ZM5.2 10H30.8A1.2 1.2 0 0 1 32 11.2V12.8A1.2 1.2 0 0 1 30.8 14H5.2A1.2 1.2 0 0 1 4 12.8V11.2A1.2 1.2 0 0 1 5.2 10ZM5.2 18H10.8A1.2 1.2 0 0 1 12 19.2V24.8A1.2 1.2 0 0 1 10.8 26H5.2A1.2 1.2 0 0 1 4 24.8V19.2A1.2 1.2 0 0 1 5.2 18ZM17.2 18H30.8A1.2 1.2 0 0 1 32 19.2V24.8A1.2 1.2 0 0 1 30.8 26H17.2A1.2 1.2 0 0 1 16 24.8V19.2A1.2 1.2 0 0 1 17.2 18Z"
      />
    </svg>
  );
}
