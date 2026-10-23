/// <reference types="vite/client" />

// Build stamp, baked in by `npm run build` from environment variables the deploy
// pipeline sets (.github/workflows/deploy.yml). Vite copies any VITE_ variable into
// the bundle at build time. All of them are undefined in a local build.
interface ImportMetaEnv {
  readonly VITE_BUILD_NUMBER?: string;
  readonly VITE_BUILD_COMMIT?: string;
  readonly VITE_BUILD_TIME?: string;
  // Where commit links point, such as https://github.com/mvmorten/dashboard-starter.
  // Comes from the pipeline, so a fork links to its own repo.
  readonly VITE_REPO_URL?: string;
}
