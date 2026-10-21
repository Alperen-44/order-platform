import { defineConfig, devices } from "@playwright/test";

// Uçtan uca testler gerçek sisteme karşı çalışır: önce proje kökünde `docker compose up -d`.
// Varsayılan adres Docker'daki site (:3000). Geliştirme sunucusuna karşı çalıştırmak için:
//   E2E_BASE_URL=http://localhost:5173 npm run test:e2e
export default defineConfig({
  testDir: "./e2e",
  timeout: 90_000, // slowpay gibi senaryolar ve Keycloak girişi dahil
  expect: { timeout: 30_000 }, // saga birkaç saniye sürer
  retries: process.env.CI ? 1 : 0,
  workers: 1, // aynı stoktan sipariş veren testler birbirini etkilemesin
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    locale: "tr-TR",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
