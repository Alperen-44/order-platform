#!/usr/bin/env bash
# Outbox pattern'in kanıtı: Kafka kapalıyken verilen siparişler kaybolmuyor.
#   1) Kafka'yı durdur
#   2) Sipariş vermeye devam et → API çalışmaya devam eder, olaylar outbox'ta birikir
#   3) Kafka'yı geri başlat → relay biriken olayları gönderir, siparişler StockReserved olur
# Kullanım: ./scripts/kafka-outage-test.sh [sipariş_sayısı]   (varsayılan 5)
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib.sh

N="${1:-5}"
ORDERS=()

step "1) Kafka durduruluyor"
docker compose stop kafka
ok "Kafka kapalı"

step "2) Kafka kapalıyken $N sipariş veriliyor"
for _ in $(seq 1 "$N"); do
  id=$(create_order "$KEYBOARD" 1)
  ORDERS+=("$id")
  echo "  $id -> $(order_status "$id")"
done
ok "API Kafka olmadan da sipariş almaya devam etti"

sleep 2
step "3) Outbox durumu"
echo "Kafka'ya gönderilmeyi bekleyen olay: $(unpublished_count)"

step "4) Kafka yeniden başlatılıyor"
docker compose start kafka
echo "Relay ve consumer'lar yeniden bağlanıyor (30-60 sn sürebilir)..."

step "5) Bütün siparişler StockReserved olmalı"
FAILED=0
for id in "${ORDERS[@]}"; do
  if status=$(wait_for_status "$id" StockReserved 90); then
    echo "  $id -> $status"
  else
    echo "  $id -> $status  (beklenen StockReserved)"
    FAILED=$((FAILED + 1))
  fi
done

echo "Outbox'ta bekleyen olay: $(unpublished_count)"

step "6) Temizlik: rezervasyonları bırak"
for id in "${ORDERS[@]}"; do
  curl -fsS -o /dev/null -X POST "$INVENTORY_URL/reservations/$id/release" || true
done

if (( FAILED == 0 )); then
  printf '\n\033[1;32mBAŞARILI: Kafka kesintisinde verilen %s siparişin hiçbiri kaybolmadı.\033[0m\n' "$N"
else
  printf '\n\033[1;31mHATA: %s sipariş tamamlanmadı.\033[0m\n' "$FAILED"
  exit 1
fi
