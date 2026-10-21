import { expect, test } from "@playwright/test";
import { addToCart, placeOrderAs } from "./helpers";

// Gerçek sistem üzerinde (Keycloak + gateway + 4 servis + Kafka) saga'nın üç sonucu.
// Kablosuz Mouse kullanılıyor: stoğu bol, diğer testlerin klavye stoğunu tüketmiyor.

test("giriş yapmadan ürünler görünür ama sipariş için giriş istenir", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Ürünler" })).toBeVisible();
  await expect(page.getByText("Mekanik Klavye")).toBeVisible();

  await addToCart(page, "Kablosuz Mouse");
  await page.goto("/cart");
  await expect(page.getByRole("button", { name: "Sipariş vermek için giriş yap" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Siparişi ver" })).toHaveCount(0);
});

test("alice: ödeme alınır, sipariş onaylanır", async ({ page }) => {
  await placeOrderAs(page, "alice", "alice123");

  await expect(page.getByText("Sipariş onaylandı")).toBeVisible();
  await expect(page.getByText("Ödeme alındı", { exact: true })).toBeVisible();
  // Tutarı istemci değil Inventory belirledi (ADR 0007): Kablosuz Mouse 650 TL.
  await expect(page.getByText(/650,00/).last()).toBeVisible();

  await page.getByRole("link", { name: "Bildirimler" }).click();
  await expect(page.getByText("Siparişiniz onaylandı").first()).toBeVisible();
});

test("declined: kart reddedilir, stok geri bırakılır, sipariş iptal edilir", async ({ page }) => {
  await placeOrderAs(page, "declined", "declined123");

  await expect(page.getByText("Ödeme alınamadı")).toBeVisible();
  // "sipariş iptal edildi" hem adım başlığında hem iptal notunda geçiyor; başlık ilk sırada.
  await expect(page.getByText("Sipariş iptal edildi").first()).toBeVisible();
  await expect(page.getByText("Ödeme reddedildi", { exact: true })).toBeVisible();
  await expect(page.getByText(/geri bırakıldı/)).toBeVisible();
});

test("bob, alice'in siparişini göremez", async ({ page, browser }) => {
  await placeOrderAs(page, "alice", "alice123");
  const aliceOrderUrl = page.url();

  // Ayrı bir tarayıcı oturumu: bob olarak giriş. Elle açılan context baseURL'i bilmediği için tam adres.
  const bobContext = await browser.newContext();
  const bob = await bobContext.newPage();
  await bob.goto(new URL("/orders", aliceOrderUrl).toString());
  await bob.getByRole("button", { name: "Giriş yap" }).first().click();
  await bob.locator("#username").fill("bob");
  await bob.locator("#password").fill("bob123");
  await bob.locator("#kc-login").click();
  await expect(bob.getByRole("button", { name: "Çıkış yap" })).toBeVisible();

  await bob.goto(aliceOrderUrl);
  await expect(bob.getByText("Sipariş bulunamadı")).toBeVisible();
  await bobContext.close();
});
