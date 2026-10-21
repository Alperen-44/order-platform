import { useAuth } from "../auth/AuthProvider";
import { EmptyState } from "./EmptyState";

export function LoginPrompt({ title, children }: { title: string; children: string }) {
  const { login } = useAuth();
  return (
    <EmptyState title={title}>
      <p>{children}</p>
      <button
        type="button"
        onClick={login}
        className="mt-5 rounded-md bg-ink px-5 py-2 font-semibold text-white hover:bg-ink/90"
      >
        Giriş yap
      </button>
    </EmptyState>
  );
}
