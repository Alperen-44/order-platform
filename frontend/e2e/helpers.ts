import { expect, type Page } from "@playwright/test";

/** Keycloak'ın giriş formunu doldurur. Giriş sonrası Keycloak siteye geri yönlendirir. */
export async function signInOnKeycloak(page: Page, username: string, password: string) {
  await expect(page).toHaveURL(/localhost:8081\/realms\/order-platform/);
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.locator("#kc-login").click();
}

/** Ürünler sayfasında bir ürünü sepete ekler. */
export async function addToCart(page: Page, productName: string) {
  await page.goto("/");
  const row = page.getByRole("listitem").filter({ hasText: productName });
  await row.getByRole("button", { name: "Sepete ekle" }).click();
  await expect(row.getByRole("group", { name: `${productName} adedi` })).toBeVisible();
}

/**
 * Giriş yapmamış kullanıcı olarak: ürünü sepete ekle → sepet → giriş → siparişi ver.
 * Sipariş detay sayfasında kalır.
 */
export async function placeOrderAs(page: Page, username: string, password: string, productName = "Kablosuz Mouse") {
  await addToCart(page, productName);
  await page.goto("/cart");
  await page.getByRole("button", { name: "Sipariş vermek için giriş yap" }).click();
  await signInOnKeycloak(page, username, password);

  // Girişten sonra sepete geri dönülür; sepet tarayıcıda saklandığı için kaybolmaz.
  await expect(page).toHaveURL(/\/cart/);
  await page.getByRole("button", { name: "Siparişi ver" }).click();
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]{36}$/);
}
