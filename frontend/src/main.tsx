import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import { initAuth } from "./auth/keycloak";
import { AuthProvider } from "./auth/AuthProvider";
import { CartProvider } from "./cart/CartProvider";
import { router } from "./router";
import "./index.css";

// TanStack Query: API'den gelen veriyi önbellekte tutar, yükleniyor/hata durumlarını yönetir.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000, // 10 sn boyunca veri "taze" sayılır, tekrar istek atılmaz
      retry: 1,
      refetchOnWindowFocus: true,
    },
  },
});

const root = createRoot(document.getElementById("root")!);

function render() {
  root.render(
    <StrictMode>
      <AuthProvider>
        <CartProvider>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </CartProvider>
      </AuthProvider>
    </StrictMode>,
  );
}

// Önce oturum durumunu öğreniyoruz, sonra siteyi çiziyoruz. Böylece "Giriş yap" butonu bir an görünüp
// kullanıcının adına dönüşmüyor. Keycloak'a ulaşılamazsa site yine de misafir olarak açılır.
initAuth()
  .catch((error: unknown) => console.error("Keycloak başlatılamadı:", error))
  .finally(render);
