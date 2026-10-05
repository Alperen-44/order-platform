#!/usr/bin/env bash
# Faz 1 duman testi: servisler ayakta mı, temel akış çalışıyor mu?
# Kullanım: ./scripts/smoke-test.sh
set -euo pipefail

ORDER_URL="${ORDER_URL:-http://localhost:5001}"
INVENTORY_URL="${INVENTORY_URL:-http://localhost:5002}"
KEYBOARD="11111111-1111-1111-1111-111111111111"
CUSTOMER="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"

step() { printf '\n\033[1;34m== %s\033[0m\n' "$1"; }

step "1) Sağlık kontrolleri"
curl -fsS "$ORDER_URL/health"; echo "  <- order-service"
curl -fsS "$INVENTORY_URL/health"; echo "  <- inventory-service"

step "2) Ürünler ve stok"
curl -fsS "$INVENTORY_URL/products"; echo

step "3) Sipariş oluştur (2 x Mekanik Klavye)"
ORDER_JSON=$(curl -fsS -X POST "$ORDER_URL/orders" \
  -H 'Content-Type: application/json' \
  -d "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":2,\"unitPrice\":1500}]}")
echo "$ORDER_JSON"
ORDER_ID=$(echo "$ORDER_JSON" | sed -E 's/.*"id":"([^"]+)".*/\1/')
echo "orderId = $ORDER_ID"

step "4) Siparişi oku"
curl -fsS "$ORDER_URL/orders/$ORDER_ID"; echo

step "5) Outbox: OrderCreated olayı siparişle aynı transaction'da yazıldı mı?"
curl -fsS "$ORDER_URL/debug/outbox" | head -c 600; echo " ..."

step "6) Stok rezerve et (beklenen: 201)"
RESERVE_BODY="{\"orderId\":\"$ORDER_ID\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":2}]}"
curl -sS -o /dev/null -w "HTTP %{http_code}\n" -X POST "$INVENTORY_URL/reservations" \
  -H 'Content-Type: application/json' -d "$RESERVE_BODY"

step "7) Aynı isteği tekrar gönder (beklenen: 200, stok İKİNCİ KEZ düşmemeli)"
curl -sS -o /dev/null -w "HTTP %{http_code}\n" -X POST "$INVENTORY_URL/reservations" \
  -H 'Content-Type: application/json' -d "$RESERVE_BODY"
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

step "8) Telafi: rezervasyonu bırak (iki kez; stok yalnızca bir kez geri gelmeli)"
curl -fsS -X POST "$INVENTORY_URL/reservations/$ORDER_ID/release"; echo
curl -fsS -X POST "$INVENTORY_URL/reservations/$ORDER_ID/release"; echo
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

printf '\n\033[1;32mTamam: temel akış çalışıyor.\033[0m\n'
