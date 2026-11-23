import { useState, type FormEvent } from "react";
import { ApiError } from "../api";
import {
  changePassword,
  passwordHint,
  passwordsDontMatch,
} from "../auth/password";
import { checkPassword } from "../auth/passwordRules";
import { useMe } from "../auth/useMe";
import PasswordField from "../forms/PasswordField";
import { useForm, type FormErrors } from "../forms/useForm";
import { usePageTitle } from "../layout/usePageTitle";

// A type, not an interface, so it fits useForm's Record<string, string>.
type ChangePasswordFields = {
  current: string;
  new: string;
  confirm: string;
};

const emptyFields: ChangePasswordFields = { current: "", new: "", confirm: "" };

// What the form can check before sending. The new password gets the server's own rules
// (passwordRules.ts), email included, since the signed-in account's email is known here.
function check(
  fields: ChangePasswordFields,
  email: string,
): FormErrors<ChangePasswordFields> {
  const errors: FormErrors<ChangePasswordFields> = {};
  if (!fields.current) {
    errors.current = "Enter your current password.";
  }

  const problem = checkPassword(fields.new, email);
  if (problem) {
    errors.new = problem;
  }

  if (fields.confirm !== fields.new) {
    errors.confirm = passwordsDontMatch;
  }

  return errors;
}

// Change your password while signed in. Every other device is signed out; this one stays.
export default function ChangePassword() {
  usePageTitle("Change password");
  const { data: me } = useMe();
  const email = me?.user.email ?? "";
  const form = useForm({
    initial: emptyFields,
    idPrefix: "change",
    check: (fields) => check(fields, email),
  });
  // What the server said, when it isn't about one field: "The current password is incorrect."
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [pending, setPending] = useState(false);

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
    setError(null);
    if (!form.validate()) {
      return;
    }

    setPending(true);
    try {
      await changePassword(form.values.current, form.values.new);
      form.setValues(emptyFields);
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
        aria-describedby={error ? "change-error" : undefined}
        noValidate
      >
        {/* Tells password managers which account the new password belongs to. */}
        <input
          type="email"
          name="username"
          autoComplete="username"
          value={email}
          readOnly
          hidden
        />
        <PasswordField
          label="Current password"
          autoComplete="current-password"
          {...form.field("current")}
        />
        <PasswordField
          label="New password"
          autoComplete="new-password"
          hint={passwordHint}
          {...form.field("new")}
        />
        <PasswordField
          label="New password again"
          autoComplete="new-password"
          {...form.field("confirm")}
        />

        <div id="change-error" role="alert" className="form-error">
          {error}
        </div>

        <button type="submit" className="primary-button" disabled={pending}>
          {pending ? "Saving..." : "Change password"}
        </button>
      </form>
    </>
  );
}
