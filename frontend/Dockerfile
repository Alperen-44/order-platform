# 1) Build aşaması: Node ile React uygulamasını statik dosyalara (dist/) derler.
FROM node:22-alpine AS build
WORKDIR /app

# Önce yalnızca paket dosyaları: kod değişse de bağımlılık katmanı önbellekten gelir.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite, VITE_ ile başlayan değişkenleri build sırasında koda gömer (çalışma anında değiştirilemez).
# Tarayıcı bu adreslere kullanıcının bilgisayarından bağlanır, o yüzden localhost.
ARG VITE_API_URL=http://localhost:8000
ARG VITE_KEYCLOAK_URL=http://localhost:8081
ENV VITE_API_URL=$VITE_API_URL VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL

# npm run build = TypeScript tip kontrolü + üretim build'i
RUN npm run build

# 2) Çalışma aşaması: yalnızca nginx ve derlenmiş dosyalar. Node ve node_modules imaja girmez,
#    imaj yaklaşık 1 GB yerine 50 MB olur.
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
# nginx "nginx" kullanıcısıyla çalışır. Kaynak dosyalar yalnızca sahibinin okuyabileceği izinlerle
# gelmişse (ör. 600) nginx onları okuyamaz ve 403 döner. Dosya izinlerinden bağımsız olsun diye
# her şeyi herkesin okuyabileceği hale getiriyoruz (klasörlere girme izni dahil).
RUN chmod -R a+rX /usr/share/nginx/html
EXPOSE 80
