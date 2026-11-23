import CharacterCount from "./CharacterCount";
import { countMessage } from "./count";
import Field from "./Field";

interface TextAreaFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  name?: string;
  optional?: boolean;
  hint?: string;
  error?: string;
  rows?: number;
  // For a long limit: the length as the rule counts it (trimmed, like the server), and the
  // limit. Shows a count once the text gets near it.
  count?: { length: number; max: number };
}

// A multi-line text input in a Field. No maxLength, as in TextField.
export default function TextAreaField({
  id,
  label,
  value,
  onChange,
  name,
  optional,
  hint,
  error,
  rows = 4,
  count,
}: TextAreaFieldProps) {
  const countId = `${id}-count`;
  const countShowing = count && countMessage(count.length, count.max) !== null;

  return (
    <Field
      id={id}
      label={label}
      optional={optional}
      hint={hint}
      error={error}
      footer={
        count && (
          <CharacterCount id={countId} length={count.length} max={count.max} />
        )
      }
      footerId={countShowing ? countId : undefined}
    >
      {(control) => (
        <textarea
          {...control}
          name={name}
          rows={rows}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  );
}
