import { useSearchParams } from "react-router";
import {
  readListState,
  writeListState,
  type ListSpec,
  type ListState,
} from "./listState";

// The list's view, read from and written to the URL (listState.ts has the rules). Each change
// is a new history entry, so Back goes to the view before.
export function useListState(spec: ListSpec) {
  const [params, setParams] = useSearchParams();
  const state = readListState(params, spec);

  function show(next: ListState) {
    setParams(writeListState(next, spec));
  }

  return { state, show };
}
