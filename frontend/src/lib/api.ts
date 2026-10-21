import { getToken, keycloak } from "../auth/keycloak";

// Bütün istekler gateway'e gider. Servislerin kendi portlarına (5001-5004) site hiç bağlanmaz.
const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:8000";

/** Gateway'den başarısız yanıt geldiğinde fırlatılır; ekranda durum koduna göre mesaj gösteririz. */
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

interface RequestOptions {
  method?: "GET" | "POST";
  body?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

async function request(path: string, options: RequestOptions = {}): Promise<Response> {
  const headers = new Headers({ Accept: "application/json", ...options.headers });
  if (options.body !== undefined) headers.set("Content-Type", "application/json");

  // Giriş yapılmışsa her isteğe token eklenir. Gateway token'ı doğrular ve içindeki "sub" değerini
  // X-Customer-Id başlığıyla servislere iletir; site müşteri kimliğini kendisi hiç göndermez.
  const token = await getToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      method: options.method ?? "GET",
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    // fetch yalnızca ağ hatasında fırlatır: gateway kapalı ya da adres yanlış.
    throw new ApiError(0, "Gateway'e ulaşılamadı. Bağlantını ve Docker'daki servisleri kontrol et.");
  }

  if (response.status === 401 && keycloak.authenticated) {
    // Token gateway tarafından reddedildi (örneğin Keycloak yeniden başlatıldı). Tekrar giriş yap.
    await keycloak.login();
  }

  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response));
  }
  return response;
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await request(path, { signal });
  return (await response.json()) as T;
}

export interface PostResult<T> {
  data: T;
  /** Idempotency-Key daha önce kullanılmışsa sunucu yeni kayıt açmaz, ilk yanıtı tekrar döner. */
  replayed: boolean;
}

export async function apiPost<T>(
  path: string,
  body: unknown,
  options: { idempotencyKey?: string } = {},
): Promise<PostResult<T>> {
  const response = await request(path, {
    method: "POST",
    body,
    headers: options.idempotencyKey ? { "Idempotency-Key": options.idempotencyKey } : undefined,
  });
  return {
    data: (await response.json()) as T,
    replayed: response.headers.get("Idempotent-Replayed") === "true",
  };
}

/**
 * ASP.NET Core hataları "problem details" biçiminde döner: { title, detail, errors }.
 * Varsa sunucunun mesajını, yoksa durum koduna göre genel bir mesaj gösteririz.
 */
async function errorMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as {
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    };
    if (problem.errors) return Object.values(problem.errors).flat().join(" ");
    if (problem.detail) return problem.detail;
    if (problem.title) return problem.title;
  } catch {
    // Gövde boş ya da JSON değil (401, 429 gibi).
  }

  switch (response.status) {
    case 401:
      return "Bu işlem için giriş yapmalısın.";
    case 404:
      return "Aradığın kayıt bulunamadı.";
    case 429:
      return "Çok kısa sürede çok fazla istek gönderildi. Birkaç saniye bekleyip tekrar dene.";
    default:
      return `İstek başarısız oldu (${response.status}).`;
  }
}
