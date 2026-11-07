import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { ApiError, refreshAntiforgeryToken } from "../api";
import AuthCard from "../auth/AuthCard";
import PasswordField from "../auth/PasswordField";
import {
  checkNewPassword,
  passwordHint,
  resetPassword,
} from "../auth/password";
import { usePageTitle } from "../layout/usePageTitle";

// Read once, when the page opens: the token arrives in the address's fragment
// (/reset-password#token=...), which the browser never sends to any server.
function readToken(): string | null {
  return new URLSearchParams(window.location.hash.slice(1)).get("token");
}

// Choose a new password from the emailed link. Afterwards, sign in with it: the reset
// itself doesn't sign anyone in.
export default function ResetPassword() {
  usePageTitle("Choose a new password");
  const navigate = useNavigate();
  const [token] = useState(readToken);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [linkFailed, setLinkFailed] = useState(false);
  const [pending, setPending] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);
  const errorId = useId();

  useEffect(() => {
    // Take the token out of the address bar (and so out of history, bookmarks, and
    // screenshots) as soon as it's been read. React Router's own history state is kept.
    if (window.location.hash) {
      window.history.replaceState(
        window.history.state,
        "",
        window.location.pathname + window.location.search,
      );
    }

    passwordRef.current?.focus();
    refreshAntiforgeryToken().catch(() => {});
  }, []);

  if (!token) {
    return (
      <AuthCard title="Choose a new password">
        <p>
          This page needs the link from a password reset email. Open the link
          from the email again, or ask for a new one.
        </p>
        <p>
          <Link to="/forgot-password" className="text-link">
            Get a new reset link
          </Link>
        </p>
      </AuthCard>
    );
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const problem = checkNewPassword(password, confirmation);
    if (problem) {
      setError(problem);
      return;
    }

    setError(null);
    setLinkFailed(false);
    setPending(true);
    try {
      await resetPassword(token!, password);
      navigate("/sign-in", {
        replace: true,
        state: { notice: "Password changed. Sign in with your new password." },
      });
    } catch (caught) {
      const detail = caught instanceof ApiError ? caught.detail : null;
      setError(detail ?? "The server didn't answer. Try again in a moment.");
      setLinkFailed(
        caught instanceof ApiError && caught.title === "Reset link not valid",
      );
      setPending(false);
    }
  }

  return (
    <AuthCard title="Choose a new password">
      <form
        method="post"
        onSubmit={submit}
        aria-describedby={error ? errorId : undefined}
        noValidate
      >
        <PasswordField
          id="reset-new-password"
          label="New password"
          autoComplete="new-password"
          hint={passwordHint}
          value={password}
          onChange={setPassword}
          inputRef={passwordRef}
        />
        <PasswordField
          id="reset-confirm-password"
          label="New password again"
          autoComplete="new-password"
          value={confirmation}
          onChange={setConfirmation}
        />

        <div id={errorId} role="alert" className="form-error">
          {error}
          {linkFailed && (
            <>
              {" "}
              <Link to="/forgot-password">Get a new reset link</Link>
            </>
          )}
        </div>

        <button type="submit" className="primary-button" disabled={pending}>
          {pending ? "Saving..." : "Set new password"}
        </button>
      </form>
    </AuthCard>
  );
}
