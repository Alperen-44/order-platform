#!/usr/bin/env bash
# Telafi işlemi (compensation) testi: ödeme reddedilince ayrılan stok geri bırakılıyor mu?
#   1) Stok rezerve edilir
#   2) Ödeme reddedilir (her zaman reddedilen test müşterisi)
#   3) Order siparişi iptal eder ve OrderCancelled yayınlar
#   4) Inventory stoğu geri bırakır, Notification müşteriye bildirir
# Kullanım: ./scripts/payment-failure-test.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

step "0) Servisler hazır mı?"
wait_for_services 60

step "1) Başlangıç stoğu"
BEFORE=$(available_stock "$KEYBOARD")
echo "Mekanik Klavye available = $BEFORE"

step "2) Ödemesi reddedilecek müşteriyle 2 adetlik sipariş"
ORDER_ID=$(create_order "$KEYBOARD" 2 "$DECLINED_CUSTOMER")
echo "orderId = $ORDER_ID"

step "3) Beklenen: Pending → StockReserved → Cancelled"
if STATUS=$(wait_for_status "$ORDER_ID" Cancelled 30); then
  ok "Sipariş durumu: $STATUS"
  echo "   sebep: $(cancellation_reason "$ORDER_ID")"
else
  fail "Sipariş iptal edilmedi (son durum: $STATUS)"
fi

step "4) Ödeme kaydı"
PAYMENT=$(payment_status "$ORDER_ID")
[[ "$PAYMENT" == "Failed" ]] && ok "Ödeme: $PAYMENT" || fail "Ödeme durumu: '$PAYMENT' (beklenen Failed)"

step "5) TELAFİ: stok geri bırakılmalı"
sleep 3 # OrderCancelled → Inventory
AFTER=$(available_stock "$KEYBOARD")
curl -fsS "$INVENTORY_URL/reservations/$ORDER_ID" | sed 's/^/Rezervasyon: /'; echo
if [[ "$AFTER" == "$BEFORE" ]]; then
  ok "Stok $BEFORE → (rezerve: $((BEFORE - 2))) → $AFTER, geri geldi"
else
  fail "Stok geri gelmedi: önce $BEFORE, şimdi $AFTER"
fi

step "6) Müşteri bildirimi"
notifications_for "$ORDER_ID"

printf '\n\033[1;32mBAŞARILI: Ödeme reddedildi, sipariş iptal edildi, stok geri bırakıldı.\033[0m\n'
