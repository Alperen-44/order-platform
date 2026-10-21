import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Site http://localhost:5173 adresinde açılır.
// Keycloak'taki istemci (order-platform-web) ve gateway'in CORS ayarı bu adrese göre yapıldı,
// o yüzden port sabit: 5173 doluysa başka porta geçmek yerine hata versin.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
  },
});
