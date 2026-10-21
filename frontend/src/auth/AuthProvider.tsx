import { createContext, useContext, useMemo, type ReactNode } from "react";
import { keycloak } from "./keycloak";

export interface CurrentUser {
  /** Token'daki "sub". Backend'de müşteri kimliği (X-Customer-Id) olarak kullanılan değer bu. */
  id: string;
  username: string;
  displayName: string;
  email?: string;
}

interface AuthContextValue {
  user: CurrentUser | null;
  login: () => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Keycloak init tamamlandıktan sonra render edilir (main.tsx), o yüzden burada "yükleniyor" durumu yok.
 * Giriş ve çıkış Keycloak'a yönlendirip sayfayı yeniden yüklediği için oturum durumu sayfa açıkken değişmez.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const value = useMemo<AuthContextValue>(() => {
    const claims = keycloak.tokenParsed;
    const user: CurrentUser | null =
      keycloak.authenticated && claims?.sub
        ? {
            id: claims.sub,
            username: String(claims.preferred_username ?? ""),
            displayName: String(claims.given_name ?? claims.preferred_username ?? "Müşteri"),
            email: typeof claims.email === "string" ? claims.email : undefined,
          }
        : null;

    return {
      user,
      login: () => void keycloak.login({ redirectUri: window.location.href }),
      logout: () => void keycloak.logout({ redirectUri: `${window.location.origin}/` }),
    };
  }, []);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth, AuthProvider içinde kullanılmalı.");
  return context;
}
