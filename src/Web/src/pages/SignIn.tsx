import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import {
  Link,
  Navigate,
  useLocation,
  useNavigate,
  useSearchParams,
} from "react-router";
import { ApiError, refreshAntiforgeryToken } from "../api";
import AuthCard from "../auth/AuthCard";
import PasswordField from "../auth/PasswordField";
import { safeReturnUrl, signIn, startDemo } from "../auth/session";
import { useMe } from "../auth/useMe";
import { useSignInInfo } from "../auth/useSignInInfo";
import { usePageTitle } from "../layout/usePageTitle";

// A plain form with the autocomplete values password managers look for ("username",
// "current-password"), real labels, and one error region that screen readers announce.
export default function SignIn() {
  usePageTitle("Sign in");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  // A message from the page that sent us here, such as "Password changed" after a reset.
  const notice = readNotice(location.state);
  const [searchParams] = useSearchParams();
  const returnUrl = safeReturnUrl(searchParams.get("returnUrl"));
  const { data: me } = useMe();
  const { data: info } = useSignInInfo();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const emailRef = useRef<HTMLInputElement>(null);
  const [demoError, setDemoError] = useState<string | null>(null);
  const [demoPending, setDemoPending] = useState(false);
  const errorId = useId();
  const demoErrorId = useId();

  useEffect(() => {
    emailRef.current?.focus();
    // A token issued to nobody: any token from an earlier session no longer matches.
    refreshAntiforgeryToken().catch(() => {});
  }, []);

  // Already signed in (a bookmarked sign-in page, or the back button): carry on.
  if (me) {
    return <Navigate to={returnUrl} replace />;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setPending(true);
    try {
      await signIn(queryClient, email, password);
      // The shell moves focus to <main> when it sees this state.
      navigate(returnUrl, { replace: true, state: { focusMain: true } });
    } catch (caught) {
      setError(describe(caught));
      setPending(false);
    }
  }

  async function tryDemo() {
    setDemoError(null);
    setDemoPending(true);
    try {
      await startDemo(queryClient);
      navigate("/", { replace: true, state: { focusMain: true } });
    } catch (caught) {
      setDemoError(describeDemo(caught));
      setDemoPending(false);
    }
  }

  return (
    <AuthCard title="Sign in">
      {notice && (
        <p className="notice" role="status">
          {notice}
        </p>
      )}

      <form
        method="post"
        onSubmit={submit}
        aria-describedby={error ? errorId : undefined}
        noValidate
      >
        <div className="field">
          <label htmlFor="sign-in-email">Email</label>
          <input
            ref={emailRef}
            id="sign-in-email"
            name="email"
            type="email"
            autoComplete="username"
            inputMode="email"
            autoCapitalize="none"
            spellCheck={false}
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>

        <PasswordField
          id="sign-in-password"
          label="Password"
          autoComplete="current-password"
          value={password}
          onChange={setPassword}
        />
        {info?.emailEnabled && (
          <p className="field-link">
            <Link to="/forgot-password" className="text-link">
              Forgot password?
            </Link>
          </p>
        )}

        <div id={errorId} role="alert" className="form-error">
          {error}
        </div>

        <button type="submit" className="primary-button" disabled={pending}>
          {pending ? "Signing in..." : "Sign in"}
        </button>
      </form>

      {info?.demo && (
        <section className="demo-account" aria-labelledby="demo-heading">
          <h2 id="demo-heading">Try the demo</h2>
          <p>
            Get your own private copy with sample data. Nobody else can see it.
            It&apos;s deleted after {duration(info.demo.sandboxIdleMinutes)}{" "}
            without use, or {duration(info.demo.sandboxHours * 60)} at most, and
            signing out ends it.
          </p>
          <div id={demoErrorId} role="alert" className="form-error">
            {demoError}
          </div>
          <button
            type="button"
            onClick={tryDemo}
            disabled={demoPending}
            aria-describedby={demoError ? demoErrorId : undefined}
          >
            {demoPending ? "Setting up your demo..." : "Try the demo"}
          </button>
        </section>
      )}
    </AuthCard>
  );
}

function readNotice(state: unknown): string | null {
  if (state && typeof state === "object" && "notice" in state) {
    const notice = (state as { notice: unknown }).notice;
    return typeof notice === "string" ? notice : null;
  }

  return null;
}

// "2 hours", "90 minutes", "24 hours".
function duration(minutes: number): string {
  if (minutes % 60 === 0) {
    const hours = minutes / 60;
    return hours === 1 ? "1 hour" : `${hours} hours`;
  }

  return `${minutes} minutes`;
}

// "The demo is busy" (503) and the rate limit (429) both say what to do in their detail.
function describeDemo(error: unknown): string {
  if (error instanceof ApiError && error.detail) {
    return error.detail;
  }

  return "The demo couldn't start. Try again in a moment.";
}

function describe(error: unknown): string {
  if (error instanceof ApiError && error.detail) {
    return error.detail;
  }

  if (error instanceof ApiError && error.status === 400) {
    return "Enter your email and password.";
  }

  return "The server didn't answer. Try again in a moment.";
}
