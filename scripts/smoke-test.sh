#!/usr/bin/env bash
# Faz 4 duman testi: saga'nın mutlu yolu ve stok yetersizliği yolu.
#   Order → Inventory (stok) → Payment (ödeme) → Order (Confirmed) → Inventory (kesinleştir) + Notification
# Kullanım: ./scripts/smoke-test.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

step "1) Sağlık kontrolleri (servisler hazır olana kadar en fazla 60 sn beklenir)"
wait_for_services 60

step "2) Stok durumu (önce)"
BEFORE=$(available_stock "$KEYBOARD")
echo "Mekanik Klavye available = $BEFORE"

step "3) Sipariş oluştur: 1 x Mekanik Klavye (yanıt hemen döner, durum Pending)"
ORDER_ID=$(create_order "$KEYBOARD" 1)
echo "orderId = $ORDER_ID, durum = $(order_status "$ORDER_ID")"

step "4) Saga: stok → ödeme → onay (beklenen: Confirmed)"
if STATUS=$(wait_for_status "$ORDER_ID" Confirmed 30); then
  ok "Sipariş durumu: $STATUS"
else
  fail "Sipariş 30 sn içinde Confirmed olmadı (son durum: $STATUS). Loglar: docker compose logs --tail=50"
fi

step "5) Her servisin kendi tarafındaki sonuç"
echo "Ödeme      : $(payment_status "$ORDER_ID")"
TOTAL=$(order_total "$ORDER_ID")
echo "Tutar      : $TOTAL TL (fiyatı istemci değil Inventory belirledi)"
[[ "${TOTAL%%.*}" == "1500" ]] || fail "Tutar $TOTAL (beklenen: Mekanik Klavye'nin fiyatı 1500)"
sleep 2 # rezervasyonun kesinleşmesi ve bildirim, OrderConfirmed olayından sonra gelir
curl -fsS "$INVENTORY_URL/reservations/$ORDER_ID" | sed 's/^/Rezervasyon: /'; echo
AFTER=$(available_stock "$KEYBOARD")
echo "Stok       : $BEFORE → $AFTER"
echo "Bildirimler:"
notifications_for "$ORDER_ID"

step "6) Outbox: bekleyen olay kalmamalı"
PENDING=$(unpublished_count)
[[ "$PENDING" == "0" ]] && ok "Order outbox'ında bekleyen olay yok" || fail "Outbox'ta $PENDING olay bekliyor"

step "7) Yetersiz stok: 1000 x Mekanik Klavye (beklenen: Cancelled, ödeme hiç istenmez)"
BIG_ORDER=$(create_order "$KEYBOARD" 1000)
if STATUS=$(wait_for_status "$BIG_ORDER" Cancelled 30); then
  ok "Sipariş durumu: $STATUS"
  echo "   sebep: $(cancellation_reason "$BIG_ORDER")"
  echo "   ödeme kaydı: $(payment_status "$BIG_ORDER" | sed 's/^$/yok (doğru: ödeme adımına hiç gelinmedi)/')"
else
  fail "Sipariş Cancelled olmadı (son durum: $STATUS)"
fi

printf '\n\033[1;32mTamam: saga uçtan uca çalışıyor.\033[0m\n'
