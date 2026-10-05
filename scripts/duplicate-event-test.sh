#!/usr/bin/env bash
# Idempotent consumer testi: AYNI olay (aynı eventId) Kafka'ya 3 kez yazılıyor.
# Kafka "en az bir kez" teslim ettiği için gerçekte de olabilecek bir durum.
# Beklenen: stok yalnızca bir kez düşer, processed_events'te tek kayıt olur, 2 tekrar atlanır.
#
# Not: Olaydaki sipariş Order servisinde yok (testte uydurduk). Order servisi gelen
# StockReserved olayı için "unknown order" uyarısı loglar; bu beklenen bir durum.
# Kullanım: ./scripts/duplicate-event-test.sh
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib.sh

MOUSE="22222222-2222-2222-2222-222222222222"
EVENT_ID=$(new_uuid)
ORDER_ID=$(new_uuid)
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)

VALUE=$(printf '{"eventId":"%s","eventType":"OrderCreated","version":1,"occurredAt":"%s","correlationId":"%s","payload":{"orderId":"%s","customerId":"%s","totalAmount":100,"items":[{"productId":"%s","quantity":1,"unitPrice":100}]}}' \
  "$EVENT_ID" "$NOW" "$ORDER_ID" "$ORDER_ID" "$CUSTOMER" "$MOUSE")
LINE=$(printf 'event-type:OrderCreated,event-id:%s\t%s\t%s' "$EVENT_ID" "$ORDER_ID" "$VALUE")

step "1) Başlangıç stoğu (Kablosuz Mouse)"
BEFORE=$(available_stock "$MOUSE")
echo "available = $BEFORE"

step "2) Aynı OrderCreated olayı 3 kez gönderiliyor (eventId = $EVENT_ID)"
for i in 1 2 3; do
  kafka_produce order.events "$LINE"
  echo "  gönderim $i"
done
sleep 5

step "3) Stok yalnızca 1 düşmeli"
AFTER=$(available_stock "$MOUSE")
echo "available = $AFTER"
[[ $((BEFORE - AFTER)) == 1 ]] && ok "Stok $BEFORE → $AFTER (bir kez düştü)" || fail "Stok $((BEFORE - AFTER)) düştü (beklenen 1)"

step "4) processed_events'te tek kayıt olmalı"
COUNT=$(db_query inventory "SELECT count(*) FROM processed_events WHERE event_id = '$EVENT_ID'")
[[ "$COUNT" == "1" ]] && ok "processed_events: $COUNT kayıt" || fail "processed_events: $COUNT kayıt (beklenen 1)"

step "5) Loglarda atlanan tekrarlar"
SKIPPED=$(docker compose logs inventory-service --since 5m 2> /dev/null | grep "Skipping duplicate" | grep -c "$EVENT_ID" || true)
echo "Atlanan tekrar sayısı: $SKIPPED (beklenen 2)"

step "6) Temizlik: rezervasyonu bırak"
curl -fsS -o /dev/null -X POST "$INVENTORY_URL/reservations/$ORDER_ID/release"
echo "available = $(available_stock "$MOUSE")"

printf '\n\033[1;32mBAŞARILI: Aynı olay 3 kez geldi, yalnızca bir kez işlendi.\033[0m\n'
