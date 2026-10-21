import { expect, test } from "@playwright/test";
import { placeOrderAs } from "./helpers";

// README için ekran görüntüleri: `npm run screenshots` (sistem :3000'de çalışıyor olmalı).
// Görüntüler docs/images/ klasörüne yazılır. Normal test çalıştırmalarında atlanır.
test.skip(!process.env.SCREENSHOTS, "Yalnızca npm run screenshots ile çalışır");
test.use({ viewport: { width: 1280, height: 860 }, deviceScaleFactor: 2 });

const out = (name: string) => `../docs/images/${name}.png`;

test("ürünler ve sepet", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("Mekanik Klavye")).toBeVisible();
  await page.screenshot({ path: out("products") });
});

test("onaylanan sipariş", async ({ page }) => {
  await placeOrderAs(page, "alice", "alice123", "Mekanik Klavye");
  await expect(page.getByText("Ödeme alındı", { exact: true })).toBeVisible();
  await page.screenshot({ path: out("order-confirmed") });
});

test("reddedilen ödeme", async ({ page }) => {
  await placeOrderAs(page, "declined", "declined123");
  // Ödeme kaydı, siparişin iptal edilmesinden bir an önce oluşabiliyor (iki ayrı servis).
  // Görüntü ara durumda çekilmesin diye zaman çizelgesinin de sonuçlanmasını bekliyoruz.
  await expect(page.getByText("Ödeme alınamadı")).toBeVisible();
  await expect(page.getByText("Ödeme reddedildi", { exact: true })).toBeVisible();
  await page.screenshot({ path: out("order-declined") });
});

test("zaman aşımı ve geç ödemenin iadesi", async ({ page }) => {
  test.setTimeout(120_000);
  await placeOrderAs(page, "slowpay", "slowpay123");
  await expect(page.getByText("Geç gelen ödeme iade edildi")).toBeVisible({ timeout: 60_000 });
  await page.screenshot({ path: out("order-timeout-refund") });
});
