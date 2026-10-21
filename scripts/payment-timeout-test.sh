#!/usr/bin/env bash
# Saga zaman aşımı + geç gelen ödemenin iadesi.
#   1) Ödemesi 25 sn süren "yavaş" müşteriyle sipariş verilir
#   2) 15 sn içinde ödeme sonucu gelmez → Order siparişi iptal eder, stok geri bırakılır
#   3) Ödeme sonradan başarıyla alınır → Payment, OrderCancelled'ı görünce parayı İADE eder
#
# Not: Yavaş ödeme sürerken aynı Kafka partition'ındaki diğer ödemeler de bekler.
# Gerçek sistemde ödeme sağlayıcısı çağrısı asenkron yapılır; burada senaryoyu göstermek için böyle.
# Kullanım: ./scripts/payment-timeout-test.sh   (yaklaşık 40 sn sürer)
set -euo pipefail
source "$(dirname "$0")/lib.sh"

step "0) Servisler hazır mı?"
wait_for_services 60

step "1) Başlangıç stoğu"
BEFORE=$(available_stock "$MOUSE")
echo "Kablosuz Mouse available = $BEFORE"

step "2) Ödemesi çok yavaş olan müşteriyle sipariş"
ORDER_ID=$(create_order "$MOUSE" 1 "$SLOW_CUSTOMER")
echo "orderId = $ORDER_ID"
START=$SECONDS

step "3) Ödeme sonucu 15 sn içinde gelmeyecek → zaman aşımı ile iptal bekleniyor"
if STATUS=$(wait_for_status "$ORDER_ID" Cancelled 40); then
  ok "Sipariş $((SECONDS - START)) sn sonra iptal edildi"
  echo "   sebep: $(cancellation_reason "$ORDER_ID")"
else
  fail "Sipariş zaman aşımıyla iptal edilmedi (son durum: $STATUS)"
fi

step "4) TELAFİ: stok geri bırakılmalı"
sleep 3
AFTER=$(available_stock "$MOUSE")
[[ "$AFTER" == "$BEFORE" ]] && ok "Stok geri geldi: $AFTER" || fail "Stok geri gelmedi: önce $BEFORE, şimdi $AFTER"

step "5) Ödeme sonradan alınıyor... iade edilmeli (beklenen: Refunded)"
if PAYMENT=$(wait_for_payment_status "$ORDER_ID" Refunded 45); then
  ok "Ödeme durumu: $PAYMENT ($((SECONDS - START)) sn)"
else
  fail "Ödeme iade edilmedi (son durum: '$PAYMENT')"
fi

step "6) Sipariş durumu değişmemeli (geç gelen PaymentCompleted iptali geri alamaz)"
FINAL=$(order_status "$ORDER_ID")
[[ "$FINAL" == "Cancelled" ]] && ok "Sipariş hâlâ: $FINAL" || fail "Sipariş durumu değişti: $FINAL"

step "7) Müşteri bildirimleri (iptal + iade)"
sleep 2
notifications_for "$ORDER_ID"

printf '\n\033[1;32mBAŞARILI: Zaman aşımında sipariş iptal edildi, stok bırakıldı, geç alınan ödeme iade edildi.\033[0m\n'
