#!/usr/bin/env bash
# Faz 7: API Gateway, Keycloak girişi, sahte kimlik engeli, Idempotency-Key ve rate limiting.
# Faz 8.5: sipariş tutarını istemci değil Inventory belirler (fiyat manipülasyonu testi).
# Bütün istekler gateway (http://localhost:8000) üzerinden gider.
# Kullanım: ./scripts/gateway-test.sh
set -euo pipefail
source "$(dirname "$0")/lib.sh"

ORDER_BODY="{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":1}]}"

step "0) Gateway ve Keycloak hazır mı?"
wait_for_gateway 120

step "1) Giriş yapmadan ürünler görülebilir (beklenen: 200)"
CODE=$(http_status "$GATEWAY_URL/api/products")
[[ "$CODE" == "200" ]] && ok "GET /api/products → $CODE" || fail "GET /api/products → $CODE"

step "2) Giriş yapmadan sipariş verilemez (beklenen: 401)"
CODE=$(http_status -X POST "$GATEWAY_URL/api/orders" -H 'Content-Type: application/json' -d "$ORDER_BODY")
[[ "$CODE" == "401" ]] && ok "POST /api/orders → $CODE" || fail "POST /api/orders → $CODE"

step "3) Keycloak'tan token al (alice ve bob)"
ALICE=$(get_token alice alice123)
BOB=$(get_token bob bob123)
ALICE_ID=$(token_sub "$ALICE")
BOB_ID=$(token_sub "$BOB")
echo "alice → $ALICE_ID"
echo "bob   → $BOB_ID"

step "4) Sahte kimlik denemesi: alice, gövdede ve başlıkta bob'un kimliğini gönderiyor"
SPOOF_BODY="{\"customerId\":\"$BOB_ID\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":1}]}"
ORDER_JSON=$(curl -fsS -X POST "$GATEWAY_URL/api/orders" \
  -H "Authorization: Bearer $ALICE" -H 'Content-Type: application/json' \
  -H "X-Customer-Id: $BOB_ID" -d "$SPOOF_BODY")
ORDER_ID=$(echo "$ORDER_JSON" | sed -E 's/.*"id":"([^"]+)".*/\1/')
OWNER=$(echo "$ORDER_JSON" | sed -E 's/.*"customerId":"([^"]+)".*/\1/')
[[ "$OWNER" == "$ALICE_ID" ]] \
  && ok "Sipariş alice adına oluştu (gateway sahte kimliği sildi, token'daki kimliği yazdı)" \
  || fail "Sipariş sahibi $OWNER (beklenen alice: $ALICE_ID)"

step "5) bob, alice'in siparişini göremez (beklenen: 404)"
CODE=$(http_status -H "Authorization: Bearer $BOB" "$GATEWAY_URL/api/orders/$ORDER_ID")
[[ "$CODE" == "404" ]] && ok "bob → $CODE" || fail "bob → $CODE"
CODE=$(http_status -H "Authorization: Bearer $ALICE" "$GATEWAY_URL/api/orders/$ORDER_ID")
[[ "$CODE" == "200" ]] && ok "alice → $CODE" || fail "alice → $CODE"

step "6) Saga gateway arkasında da çalışıyor (beklenen: Confirmed)"
STATUS=""
for _ in $(seq 1 30); do
  STATUS=$(curl -fsS -H "Authorization: Bearer $ALICE" "$GATEWAY_URL/api/orders/$ORDER_ID" | sed -E 's/.*"status":"([^"]+)".*/\1/')
  [[ "$STATUS" == "Confirmed" ]] && break
  sleep 1
done
[[ "$STATUS" == "Confirmed" ]] && ok "Sipariş durumu: $STATUS" || fail "Sipariş durumu: $STATUS"

step "7) Idempotency-Key: aynı anahtarla iki istek → tek sipariş"
KEY=$(new_uuid)
FIRST=$(curl -fsS -D /tmp/idem1.h -X POST "$GATEWAY_URL/api/orders" -H "Authorization: Bearer $ALICE" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" -d "$ORDER_BODY")
SECOND=$(curl -fsS -D /tmp/idem2.h -X POST "$GATEWAY_URL/api/orders" -H "Authorization: Bearer $ALICE" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" -d "$ORDER_BODY")
FIRST_ID=$(echo "$FIRST" | sed -E 's/.*"id":"([^"]+)".*/\1/')
SECOND_ID=$(echo "$SECOND" | sed -E 's/.*"id":"([^"]+)".*/\1/')
echo "1. istek: HTTP $(head -1 /tmp/idem1.h | cut -d' ' -f2) → $FIRST_ID"
echo "2. istek: HTTP $(head -1 /tmp/idem2.h | cut -d' ' -f2) → $SECOND_ID  $(grep -i '^idempotent-replayed' /tmp/idem2.h | tr -d '\r')"
[[ "$FIRST_ID" == "$SECOND_ID" ]] && ok "İkisi de aynı sipariş" || fail "İki farklı sipariş oluştu"

step "8) Aynı anahtar, farklı sepet (beklenen: 422)"
OTHER_BODY="{\"items\":[{\"productId\":\"$MOUSE\",\"quantity\":3}]}"
CODE=$(http_status -X POST "$GATEWAY_URL/api/orders" -H "Authorization: Bearer $ALICE" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" -d "$OTHER_BODY")
[[ "$CODE" == "422" ]] && ok "→ $CODE" || fail "→ $CODE"

step "9) Siparişlerim: alice yalnızca kendi siparişlerini görür"
COUNT=$(curl -fsS -H "Authorization: Bearer $ALICE" "$GATEWAY_URL/api/orders" | { grep -o '"customerId":"[^"]*"' || true; } | sort -u)
echo "$COUNT"
[[ "$(echo "$COUNT" | wc -l | tr -d ' ')" == "1" && "$COUNT" == *"$ALICE_ID"* ]] \
  && ok "Listede yalnızca alice'in siparişleri var" || fail "Listede başka müşterinin siparişi var"

step "10) Rate limiting: 10 saniyede 30 istek (sınır 20, beklenen: bir kısmı 429)"
CODES=$(for _ in $(seq 1 30); do http_status -H "Authorization: Bearer $BOB" "$GATEWAY_URL/api/orders"; echo; done | sort | uniq -c)
echo "$CODES"
echo "$CODES" | grep -q ' 429$' && ok "Fazla istekler 429 Too Many Requests aldı" || fail "Hiç 429 alınmadı"

step "11) Fiyat manipülasyonu: alice klavyeyi 1 TL'ye almaya çalışıyor (beklenen tutar: 1500)"
CHEAT_BODY="{\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":1,\"unitPrice\":1}]}"
CHEAT_ID=$(curl -fsS -X POST "$GATEWAY_URL/api/orders" -H "Authorization: Bearer $ALICE" \
  -H 'Content-Type: application/json' -d "$CHEAT_BODY" | sed -E 's/.*"id":"([^"]+)".*/\1/')
CHEAT_JSON=""
for _ in $(seq 1 30); do
  CHEAT_JSON=$(curl -fsS -H "Authorization: Bearer $ALICE" "$GATEWAY_URL/api/orders/$CHEAT_ID")
  [[ "$CHEAT_JSON" == *'"status":"Confirmed"'* ]] && break
  sleep 1
done
CHEAT_TOTAL=$(echo "$CHEAT_JSON" | sed -E 's/.*"totalAmount":([0-9.]+).*/\1/')
[[ "${CHEAT_TOTAL%%.*}" == "1500" ]] \
  && ok "Gönderilen 1 TL yok sayıldı, tutar Inventory fiyatıyla hesaplandı: $CHEAT_TOTAL TL" \
  || fail "Tutar $CHEAT_TOTAL (beklenen 1500)"

rm -f /tmp/idem1.h /tmp/idem2.h
printf '\n\033[1;32mBAŞARILI: Gateway, giriş, sahiplik, idempotency ve rate limiting çalışıyor.\033[0m\n'
