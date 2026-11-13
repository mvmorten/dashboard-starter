import { useId, useState, type FormEvent } from "react";
import { ApiError } from "../api";
import PasswordField from "../auth/PasswordField";
import {
  changePassword,
  checkNewPassword,
  passwordHint,
} from "../auth/password";
import { useMe } from "../auth/useMe";
import { usePageTitle } from "../layout/usePageTitle";

// Change your password while signed in. Every other device is signed out; this one stays.
export default function ChangePassword() {
  usePageTitle("Change password");
  const { data: me } = useMe();
  const [current, setCurrent] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);
  const errorId = useId();

  if (me?.user.isDemo) {
    return (
      <>
        <h1>Change password</h1>
        <p className="notice">
          Demo accounts don&apos;t have a password: yours is deleted when the
          demo ends.
        </p>
      </>
    );
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setDone(false);
    if (!current) {
      setError("Enter your current password.");
      return;
    }

    const problem = checkNewPassword(password, confirmation);
    if (problem) {
      setError(problem);
      return;
    }

    setError(null);
    setPending(true);
    try {
      await changePassword(current, password);
      setCurrent("");
      setPassword("");
      setConfirmation("");
      setDone(true);
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

  return (
    <>
      <h1>Change password</h1>
      <p className="lead">
        Changing your password signs you out everywhere else. You stay signed in
        here.
      </p>

      <div role="status">
        {done && <p className="notice">Password changed.</p>}
      </div>

      <form
        method="post"
        onSubmit={submit}
        className="narrow-form"
        aria-describedby={error ? errorId : undefined}
        noValidate
      >
        {/* Tells password managers which account the new password belongs to. */}
        <input
          type="email"
          name="username"
          autoComplete="username"
          value={me?.user.email ?? ""}
          readOnly
          hidden
        />
        <PasswordField
          id="change-current-password"
          label="Current password"
          autoComplete="current-password"
          value={current}
          onChange={setCurrent}
        />
        <PasswordField
          id="change-new-password"
          label="New password"
          autoComplete="new-password"
          hint={passwordHint}
          value={password}
          onChange={setPassword}
        />
        <PasswordField
          id="change-confirm-password"
          label="New password again"
          autoComplete="new-password"
          value={confirmation}
          onChange={setConfirmation}
        />

        <div id={errorId} role="alert" className="form-error">
          {error}
        </div>

        <button type="submit" className="primary-button" disabled={pending}>
          {pending ? "Saving..." : "Change password"}
        </button>
      </form>
    </>
  );
}
