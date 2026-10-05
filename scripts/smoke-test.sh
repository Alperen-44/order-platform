#!/usr/bin/env bash
# Faz 2 duman testi: sipariş Kafka üzerinden Inventory'ye gidiyor, sonuç Order'a geri dönüyor mu?
# Kullanım: ./scripts/smoke-test.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

step "1) Sağlık kontrolleri"
curl -fsS "$ORDER_URL/health"; echo "  <- order-service"
curl -fsS "$INVENTORY_URL/health"; echo "  <- inventory-service"

step "2) Stok durumu (önce)"
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

step "3) Sipariş oluştur: 2 x Mekanik Klavye (yanıt hemen döner, durum Pending)"
ORDER_ID=$(create_order "$KEYBOARD" 2)
echo "orderId = $ORDER_ID, durum = $(order_status "$ORDER_ID")"

step "4) Saga: Order → Kafka → Inventory → Kafka → Order (beklenen: StockReserved)"
if STATUS=$(wait_for_status "$ORDER_ID" StockReserved 30); then
  ok "Sipariş durumu: $STATUS"
else
  fail "Sipariş 30 sn içinde StockReserved olmadı (son durum: $STATUS). Loglar: docker compose logs order-service inventory-service"
fi

step "5) Inventory tarafında rezervasyon otomatik oluştu mu?"
curl -fsS "$INVENTORY_URL/reservations/$ORDER_ID"; echo
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

step "6) Outbox: olay Kafka'ya gönderildi mi? (beklenen: bekleyen olay 0)"
PENDING=$(unpublished_count)
[[ "$PENDING" == "0" ]] && ok "Outbox'ta bekleyen olay yok" || fail "Outbox'ta $PENDING olay bekliyor"

step "7) Yetersiz stok: 1000 x Mekanik Klavye (beklenen: Cancelled)"
BIG_ORDER=$(create_order "$KEYBOARD" 1000)
if STATUS=$(wait_for_status "$BIG_ORDER" Cancelled 30); then
  ok "Sipariş durumu: $STATUS"
  curl -fsS "$ORDER_URL/orders/$BIG_ORDER" | sed -E 's/.*"cancellationReason":"([^"]*)".*/   sebep: \1/'
else
  fail "Sipariş Cancelled olmadı (son durum: $STATUS)"
fi

step "8) Idempotency: aynı sipariş için rezervasyon tekrar istenirse (beklenen: 200, stok değişmez)"
curl -sS -o /dev/null -w "HTTP %{http_code}\n" -X POST "$INVENTORY_URL/reservations" \
  -H 'Content-Type: application/json' \
  -d "{\"orderId\":\"$ORDER_ID\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":2}]}"
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

step "9) Temizlik: rezervasyonu bırak (test tekrarlanabilsin diye)"
curl -fsS -o /dev/null -X POST "$INVENTORY_URL/reservations/$ORDER_ID/release"
curl -fsS "$INVENTORY_URL/products/$KEYBOARD"; echo

printf '\n\033[1;32mTamam: olay güdümlü akış çalışıyor.\033[0m\n'
