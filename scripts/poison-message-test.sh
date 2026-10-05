#!/usr/bin/env bash
# Zehirli mesaj testi: bozuk bir mesaj tüketiciyi kilitlemiyor, Dead Letter Queue'ya gidiyor.
#   1) order.events'e JSON olmayan bir "OrderCreated" mesajı yaz
#   2) Inventory bunu tekrar denemeden inventory-service.dlq'ya göndermeli
#   3) Arkasından gelen gerçek siparişler normal işlenmeye devam etmeli
# Kullanım: ./scripts/poison-message-test.sh
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib.sh

EVENT_ID=$(new_uuid)
KEY="poison-$EVENT_ID"

step "1) Bozuk bir OrderCreated mesajı doğrudan order.events'e yazılıyor"
kafka_produce order.events "$(printf 'event-type:OrderCreated,event-id:%s\t%s\tbu bir JSON degil {' "$EVENT_ID" "$KEY")"
echo "eventId = $EVENT_ID"

step "2) Mesaj inventory-service.dlq'ya gitmiş olmalı"
FOUND=""
for _ in 1 2 3; do
  FOUND=$(kafka_read inventory-service.dlq | grep "$EVENT_ID" || true)
  [[ -n "$FOUND" ]] && break
  sleep 2
done
if [[ -n "$FOUND" ]]; then
  ok "Mesaj DLQ'da. Eklenen hata başlıkları:"
  echo "$FOUND" | cut -f1 | tr ',' '\n' | grep '^dlq-' | sed 's/^/   /'
else
  fail "Mesaj DLQ'da bulunamadı. Loglar: docker compose logs inventory-service --tail=50"
fi

step "3) Tüketici kilitlenmedi mi? Arkasından gelen sipariş işlenmeli"
ORDER_ID=$(create_order "$KEYBOARD" 1)
if STATUS=$(wait_for_status "$ORDER_ID" StockReserved 30); then
  ok "Yeni sipariş: $STATUS"
else
  fail "Yeni sipariş işlenmedi (son durum: $STATUS)"
fi
curl -fsS -o /dev/null -X POST "$INVENTORY_URL/reservations/$ORDER_ID/release"

step "4) Bozuk mesaj processed_events'e yazılmamış olmalı"
COUNT=$(db_query inventory "SELECT count(*) FROM processed_events WHERE event_id = '$EVENT_ID'")
[[ "$COUNT" == "0" ]] && ok "processed_events kaydı yok (işlenmedi, DLQ'ya gitti)" || fail "Beklenmeyen kayıt sayısı: $COUNT"

printf '\n\033[1;32mBAŞARILI: Bozuk mesaj DLQ'"'"'ya gitti, sistem çalışmaya devam etti.\033[0m\n'
