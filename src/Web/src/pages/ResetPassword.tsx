import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { ApiError, refreshAntiforgeryToken } from "../api";
import AuthCard from "../auth/AuthCard";
import {
  passwordHint,
  passwordsDontMatch,
  resetPassword,
} from "../auth/password";
import { checkPasswordLength } from "../auth/passwordRules";
import PasswordField from "../forms/PasswordField";
import { useForm, type FormErrors } from "../forms/useForm";
import { usePageTitle } from "../layout/usePageTitle";

// A type, not an interface, so it fits useForm's Record<string, string>.
type ResetFields = {
  new: string;
  confirm: string;
};

// What the form can check before sending: the length only. The account's email isn't known
// until the server looks up the link's token, so "not your email" is checked there.
function check(fields: ResetFields): FormErrors<ResetFields> {
  const errors: FormErrors<ResetFields> = {};
  const problem = checkPasswordLength(fields.new);
  if (problem) {
    errors.new = problem;
  }

  if (fields.confirm !== fields.new) {
    errors.confirm = passwordsDontMatch;
  }

  return errors;
}

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
  const form = useForm({
    initial: { new: "", confirm: "" },
    idPrefix: "reset",
    check,
  });
  // What the server said: an expired link, or a rule only it can check.
  const [error, setError] = useState<string | null>(null);
  const [linkFailed, setLinkFailed] = useState(false);
  const [pending, setPending] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);

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
    setError(null);
    setLinkFailed(false);
    if (!form.validate()) {
      return;
    }

    setPending(true);
    try {
      await resetPassword(token!, form.values.new);
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
        aria-describedby={error ? "reset-error" : undefined}
        noValidate
      >
        <PasswordField
          label="New password"
          autoComplete="new-password"
          hint={passwordHint}
          inputRef={passwordRef}
          {...form.field("new")}
        />
        <PasswordField
          label="New password again"
          autoComplete="new-password"
          {...form.field("confirm")}
        />

        <div id="reset-error" role="alert" className="form-error">
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
