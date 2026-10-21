# Order Platform Web (Tezgâh)

Order Platform'un müşteri arayüzü: React + TypeScript + Vite, Tailwind CSS, React Router ve TanStack Query.
Bütün istekler API Gateway'e (`http://localhost:8000`) gider.

## Çalıştırma

Önce backend'in ayakta olması gerekir (proje kökünde `docker compose up -d`).

```
cd frontend
npm install
npm run dev
```

Site: http://localhost:5173

Gateway başka bir adresteyse `.env.example` dosyasını `.env.local` adıyla kopyalayıp `VITE_API_URL` değerini değiştir.

## Komutlar

| Komut | Ne yapar |
|---|---|
| `npm run dev` | Geliştirme sunucusu (kaydedince sayfa kendini yeniler) |
| `npm run typecheck` | TypeScript tip kontrolü |
| `npm run build` | Tip kontrolü + `dist/` klasörüne üretim build'i |
