import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router";
import { ApiError, refreshAntiforgeryToken } from "../api";
import { safeReturnUrl, signIn } from "../auth/session";
import { useMe } from "../auth/useMe";
import { useSignInInfo } from "../auth/useSignInInfo";
import Logo from "../layout/Logo";
import ThemeToggle from "../layout/ThemeToggle";
import { usePageTitle } from "../layout/usePageTitle";

// A plain form with the autocomplete values password managers look for ("username",
// "current-password"), real labels, and one error region that screen readers announce.
export default function SignIn() {
  usePageTitle("Sign in");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const returnUrl = safeReturnUrl(searchParams.get("returnUrl"));
  const { data: me } = useMe();
  const { data: info } = useSignInInfo();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const emailRef = useRef<HTMLInputElement>(null);
  const submitRef = useRef<HTMLButtonElement>(null);
  const errorId = useId();

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

  function fillDemoAccount() {
    if (!info?.demoAccount) {
      return;
    }

    setEmail(info.demoAccount.email);
    setPassword(info.demoAccount.password);
    setError(null);
    submitRef.current?.focus();
  }

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
        <h1>Sign in</h1>

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

          <div className="field">
            <label htmlFor="sign-in-password">Password</label>
            <input
              id="sign-in-password"
              name="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>

          <div id={errorId} role="alert" className="form-error">
            {error}
          </div>

          <button
            ref={submitRef}
            type="submit"
            className="primary-button"
            disabled={pending}
          >
            {pending ? "Signing in..." : "Sign in"}
          </button>
        </form>

        {info?.demoAccount && (
          <section className="demo-account" aria-labelledby="demo-heading">
            <h2 id="demo-heading">Try the demo</h2>
            <p>
              Everyone shares this account, so don&apos;t put anything private
              in it.
            </p>
            <dl>
              <dt>Email</dt>
              <dd>
                <code>{info.demoAccount.email}</code>
              </dd>
              <dt>Password</dt>
              <dd>
                <code>{info.demoAccount.password}</code>
              </dd>
            </dl>
            <button type="button" onClick={fillDemoAccount}>
              Use demo account
            </button>
          </section>
        )}
      </div>
    </main>
  );
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
