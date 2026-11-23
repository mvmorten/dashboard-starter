import { useState, type Ref } from "react";
import Field from "./Field";

interface PasswordFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  // "current-password" for one you know, "new-password" for one you're choosing: password
  // managers fill the first and offer to generate and save the second.
  autoComplete: "current-password" | "new-password";
  hint?: string;
  error?: string;
  inputRef?: Ref<HTMLInputElement>;
}

// A password input with a show/hide button, in a Field. Seeing what you typed helps most on a
// phone keyboard, where a typo in a 12-character password is easy and hard to spot.
// No maxLength: it would silently cut a pasted password short. The form checks the length
// and says so instead.
export default function PasswordField({
  id,
  label,
  value,
  onChange,
  autoComplete,
  hint,
  error,
  inputRef,
}: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);

  return (
    <Field id={id} label={label} hint={hint} error={error}>
      {(control) => (
        <div className="password-input">
          <input
            {...control}
            ref={inputRef}
            type={visible ? "text" : "password"}
            autoComplete={autoComplete}
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            required
            value={value}
            onChange={(event) => onChange(event.target.value)}
          />
          {/* The visible word says what a press does; screen readers also hear which field. */}
          <button
            type="button"
            aria-controls={id}
            onClick={() => setVisible((shown) => !shown)}
          >
            {visible ? "Hide" : "Show"}
            <span className="visually-hidden"> {label.toLowerCase()}</span>
          </button>
        </div>
      )}
    </Field>
  );
}
