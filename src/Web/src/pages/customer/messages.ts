import { ApiError } from "../../api";

export function listOf(items: string[]): string {
  if (items.length <= 1) {
    return items.join("");
  }

  return `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`;
}

export function messageOf(caught: unknown): string {
  if (caught instanceof ApiError) {
    return caught.detail ?? caught.title ?? "That didn't work.";
  }

  return "The server didn't answer. Try again in a moment.";
}
