import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // The browser only ever talks to this dev server, which forwards /api to the
      // ASP.NET Core API (port set in src/Api/Properties/launchSettings.json).
      // One origin means no CORS, and the auth cookie in step 4 is a plain
      // same-site cookie, just like in production.
      "/api": "http://localhost:5080",
    },
  },
});
