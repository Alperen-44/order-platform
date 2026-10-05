#!/usr/bin/env bash
# Test scriptlerinin ortak yardımcıları. Doğrudan çalıştırılmaz: "source scripts/lib.sh"

ORDER_URL="${ORDER_URL:-http://localhost:5001}"
INVENTORY_URL="${INVENTORY_URL:-http://localhost:5002}"
KEYBOARD="11111111-1111-1111-1111-111111111111"
CUSTOMER="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"

step() { printf '\n\033[1;34m== %s\033[0m\n' "$1"; }
ok()   { printf '\033[1;32m✔ %s\033[0m\n' "$1"; }
fail() { printf '\033[1;31m✘ %s\033[0m\n' "$1"; exit 1; }

# create_order <productId> <quantity> -> orderId yazdırır
create_order() {
  local json
  json=$(curl -fsS -X POST "$ORDER_URL/orders" -H 'Content-Type: application/json' \
    -d "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"$1\",\"quantity\":$2,\"unitPrice\":1500}]}")
  echo "$json" | sed -E 's/.*"id":"([^"]+)".*/\1/'
}

# order_status <orderId>
order_status() {
  curl -fsS "$ORDER_URL/orders/$1" | sed -E 's/.*"status":"([^"]+)".*/\1/'
}

# wait_for_status <orderId> <beklenen> [saniye] -> son görülen durumu yazdırır, olmazsa 1 döner
wait_for_status() {
  local deadline=$((SECONDS + ${3:-30})) status=""
  while (( SECONDS < deadline )); do
    status=$(order_status "$1")
    if [[ "$status" == "$2" ]]; then echo "$status"; return 0; fi
    sleep 1
  done
  echo "$status"
  return 1
}

# unpublished_count -> Order outbox'ında henüz Kafka'ya gitmemiş olay sayısı (son 50 kayıt içinde)
unpublished_count() {
  curl -fsS "$ORDER_URL/debug/outbox" | grep -o '"publishedAt":null' | wc -l | tr -d ' '
}
