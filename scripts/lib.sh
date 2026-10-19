#!/usr/bin/env bash
# Test scriptlerinin ortak yardımcıları. Doğrudan çalıştırılmaz: "source scripts/lib.sh"

ORDER_URL="${ORDER_URL:-http://localhost:5001}"
INVENTORY_URL="${INVENTORY_URL:-http://localhost:5002}"
PAYMENT_URL="${PAYMENT_URL:-http://localhost:5003}"
NOTIFICATION_URL="${NOTIFICATION_URL:-http://localhost:5004}"

KEYBOARD="11111111-1111-1111-1111-111111111111"
MOUSE="22222222-2222-2222-2222-222222222222"

CUSTOMER="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
# Payment servisinin test müşterileri (Provider/FakePaymentProvider.cs → TestCustomers)
DECLINED_CUSTOMER="00000000-0000-0000-0000-00000000fa11"   # ödemesi her zaman reddedilir
SLOW_CUSTOMER="00000000-0000-0000-0000-000000005103"       # ödemesi saga zaman aşımından uzun sürer

step() { printf '\n\033[1;34m== %s\033[0m\n' "$1"; }
ok()   { printf '\033[1;32m✔ %s\033[0m\n' "$1"; }
fail() { printf '\033[1;31m✘ %s\033[0m\n' "$1"; exit 1; }

# wait_for_services [saniye] -> dört servis de /health'e cevap verene kadar bekler
# "docker compose up" komutundan hemen sonra servislerin açılması 10-30 sn sürer.
wait_for_services() {
  local deadline=$((SECONDS + ${1:-60})) url
  for url in "$ORDER_URL" "$INVENTORY_URL" "$PAYMENT_URL" "$NOTIFICATION_URL"; do
    until curl -fs "$url/health" > /dev/null 2>&1; do
      if (( SECONDS >= deadline )); then
        fail "$url cevap vermiyor. Docker Desktop açık mı? 'docker compose ps' ve 'docker compose logs' ile kontrol et."
      fi
      sleep 2
    done
    printf 'Healthy  <- %s\n' "$url"
  done
}

# create_order <productId> <quantity> [customerId] -> orderId yazdırır
create_order() {
  local json customer="${3:-$CUSTOMER}"
  json=$(curl -fsS -X POST "$ORDER_URL/orders" -H 'Content-Type: application/json' \
    -d "{\"customerId\":\"$customer\",\"items\":[{\"productId\":\"$1\",\"quantity\":$2,\"unitPrice\":1500}]}")
  echo "$json" | sed -E 's/.*"id":"([^"]+)".*/\1/'
}

# order_status <orderId>
order_status() {
  curl -fsS "$ORDER_URL/orders/$1" | sed -E 's/.*"status":"([^"]+)".*/\1/'
}

# cancellation_reason <orderId>
cancellation_reason() {
  curl -fsS "$ORDER_URL/orders/$1" | sed -E 's/.*"cancellationReason":"([^"]*)".*/\1/'
}

# payment_status <orderId> -> Completed | Failed | Refunded | (boş)
payment_status() {
  curl -fsS "$PAYMENT_URL/payments/$1" 2> /dev/null | sed -E 's/.*"status":"([^"]+)".*/\1/' || true
}

# wait_for_payment_status <orderId> <beklenen> [saniye]
wait_for_payment_status() {
  local deadline=$((SECONDS + ${3:-30})) status=""
  while (( SECONDS < deadline )); do
    status=$(payment_status "$1")
    if [[ "$status" == "$2" ]]; then echo "$status"; return 0; fi
    sleep 1
  done
  echo "$status"
  return 1
}

# notifications_for <orderId> -> bildirim mesajları, her satırda bir tane
notifications_for() {
  curl -fsS "$NOTIFICATION_URL/notifications?orderId=$1" \
    | { grep -o '"message":"[^"]*"' || true; } | sed -E 's/"message":"(.*)"/   • \1/'
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
# Not: grep hiç eşleşme bulamazsa 1 ile çıkar. "set -o pipefail" bunu hata sanmasın diye "|| true".
unpublished_count() {
  curl -fsS "$ORDER_URL/debug/outbox" | { grep -o '"publishedAt":null' || true; } | wc -l | tr -d ' '
}

# available_stock <productId> -> satılabilir adet
available_stock() {
  curl -fsS "$INVENTORY_URL/products/$1" | sed -E 's/.*"available":([0-9]+).*/\1/'
}

# ---------- Kafka yardımcıları (repo kökünden çalıştırılmalı) ----------

# kafka_produce <topic> <satır>
# Satır biçimi: "baslik1:deger1,baslik2:deger2<TAB>anahtar<TAB>değer"
kafka_produce() {
  printf '%s\n' "$2" | docker compose exec -T kafka /opt/kafka/bin/kafka-console-producer.sh \
    --bootstrap-server kafka:19092 --topic "$1" \
    --property parse.key=true --property parse.headers=true > /dev/null
}

# kafka_read <topic> -> topic'teki bütün mesajları başlık ve anahtarlarıyla yazdırır
kafka_read() {
  docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
    --bootstrap-server kafka:19092 --topic "$1" --from-beginning --timeout-ms 8000 \
    --property print.key=true --property print.headers=true 2> /dev/null || true
}

# db_query <order|inventory> <sql> -> tek değer döndürür
db_query() {
  if [[ "$1" == "order" ]]; then
    docker compose exec -T order-db psql -U order_user -d orders -tAc "$2"
  else
    docker compose exec -T inventory-db psql -U inventory_user -d inventory -tAc "$2"
  fi
}

new_uuid() { uuidgen | tr '[:upper:]' '[:lower:]'; }
