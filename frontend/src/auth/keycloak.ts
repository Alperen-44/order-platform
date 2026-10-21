import Keycloak from "keycloak-js";

// Tek bir Keycloak nesnesi: bütün uygulama aynı oturumu kullanır.
export const keycloak = new Keycloak({
  url: import.meta.env.VITE_KEYCLOAK_URL ?? "http://localhost:8081",
  realm: import.meta.env.VITE_KEYCLOAK_REALM ?? "order-platform",
  clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID ?? "order-platform-web",
});

let initPromise: Promise<boolean> | undefined;

/**
 * Uygulama açılırken bir kez çağrılır. Dönen değer: kullanıcı giriş yapmış mı?
 *
 * - check-sso: Kullanıcıyı zorla giriş sayfasına göndermez. Keycloak'ta oturumu varsa sessizce geri yükler,
 *   yoksa site "misafir" olarak açılır (ürünler herkese açık).
 * - pkceMethod S256: Authorization Code + PKCE akışı. Tarayıcıda client secret tutulamayacağı için
 *   public istemcilerde standart yöntem bu.
 * - checkLoginIframe false: Keycloak'ı arka planda sürekli yoklayan iframe'i kapatıyoruz; token
 *   süresini kendimiz yönetiyoruz (getToken).
 */
export function initAuth(): Promise<boolean> {
  // React StrictMode geliştirmede bileşenleri iki kez çalıştırır; init yalnızca bir kez yapılmalı.
  initPromise ??= keycloak.init({
    onLoad: "check-sso",
    pkceMethod: "S256",
    checkLoginIframe: false,
    silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`,
  });
  return initPromise;
}

/**
 * API isteğinden hemen önce çağrılır. Token'ın 30 saniyeden az ömrü kaldıysa Keycloak'tan yenisini alır.
 * Giriş yapılmamışsa undefined döner (istek token'sız gider).
 */
export async function getToken(): Promise<string | undefined> {
  if (!keycloak.authenticated) return undefined;
  try {
    await keycloak.updateToken(30);
    return keycloak.token;
  } catch {
    // Yenileme başarısız: Keycloak'taki oturum bitmiş. Tekrar giriş yapılmalı.
    await keycloak.login();
    return undefined;
  }
}
