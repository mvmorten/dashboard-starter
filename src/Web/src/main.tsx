import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { lazy, StrictMode, Suspense } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import App from "./App.tsx";
import "./styles/tokens.css";
import "./styles/base.css";
import "./styles/shell.css";
import "./styles/pages.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("Missing #root element in index.html");
}

const queryClient = new QueryClient();

// Development only. Vite replaces import.meta.env.DEV with a constant, so a production
// build drops this import and the devtools package never reaches the browser.
const QueryDevtools = import.meta.env.DEV
  ? lazy(() =>
      import("@tanstack/react-query-devtools").then((module) => ({
        default: module.ReactQueryDevtools,
      })),
    )
  : null;

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
      {QueryDevtools && (
        <Suspense fallback={null}>
          <QueryDevtools initialIsOpen={false} />
        </Suspense>
      )}
    </QueryClientProvider>
  </StrictMode>,
);
