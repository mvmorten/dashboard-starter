import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { ApiError, refreshAntiforgeryToken } from "../api";
import AuthCard from "../auth/AuthCard";
import { forgotPassword } from "../auth/password";
import { useSignInInfo } from "../auth/useSignInInfo";
import { usePageTitle } from "../layout/usePageTitle";

// Ask for a reset link. The answer is the same whatever happens (account or not), so the
// page can't be used to find out who has an account here.
export default function ForgotPassword() {
  usePageTitle("Forgot password");
  const { data: info } = useSignInInfo();
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [sent, setSent] = useState(false);
  const emailRef = useRef<HTMLInputElement>(null);
  const errorId = useId();

  useEffect(() => {
    emailRef.current?.focus();
    refreshAntiforgeryToken().catch(() => {});
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!email.trim()) {
      setError("Enter the email address you sign in with.");
      return;
    }

    setError(null);
    setPending(true);
    try {
      await forgotPassword(email.trim());
      setSent(true);
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.detail
          ? caught.detail
          : "The server didn't answer. Try again in a moment.",
      );
    } finally {
      setPending(false);
    }
  }

  if (info && !info.emailEnabled) {
    return (
      <AuthCard title="Forgot password">
        <p>
          This site can&apos;t send email, so passwords can&apos;t be reset from
          here. Ask whoever runs it to set a new password for you.
        </p>
        <p>
          <Link to="/sign-in" className="text-link">
            Back to sign in
          </Link>
        </p>
      </AuthCard>
    );
  }

  if (sent) {
    return (
      <AuthCard title="Check your email">
        <p role="status">
          If an account uses{" "}
          <strong className="break-anywhere">{email.trim()}</strong>, we&apos;ve
          sent it a link to choose a new password. The link works for 30
          minutes. Nothing there after a few minutes? Check your spam folder.
        </p>
        <p>
          <Link to="/sign-in" className="text-link">
            Back to sign in
          </Link>
        </p>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Forgot password">
      <p>
        Enter the email address you sign in with, and we&apos;ll send you a link
        to choose a new password.
      </p>
      <form
        method="post"
        onSubmit={submit}
        aria-describedby={error ? errorId : undefined}
        noValidate
      >
        <div className="field">
          <label htmlFor="forgot-email">Email</label>
          <input
            ref={emailRef}
            id="forgot-email"
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

        <div id={errorId} role="alert" className="form-error">
          {error}
        </div>

        <button type="submit" className="primary-button" disabled={pending}>
          {pending ? "Sending..." : "Send reset link"}
        </button>
      </form>
      <p className="form-footer">
        <Link to="/sign-in" className="text-link">
          Back to sign in
        </Link>
      </p>
    </AuthCard>
  );
}
