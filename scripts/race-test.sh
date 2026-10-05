#!/usr/bin/env bash
# "Son ürün" testi: stokta 1 adet kalan ürünü N kişi AYNI ANDA almaya çalışıyor.
# Beklenen: tam olarak 1 istek 201 alır, diğerleri 409. Stok asla eksiye düşmez.
# Kullanım: ./scripts/race-test.sh [istek_sayısı]   (varsayılan 100)
set -euo pipefail

N="${1:-100}"
export INVENTORY_URL="${INVENTORY_URL:-http://localhost:5002}"
export PRODUCT="33333333-3333-3333-3333-333333333333"   # Sınırlı Sayıda Kulaklık (stok: 1)
RESULTS="$(mktemp)"

reserve() {
  local order_id code
  order_id="$(uuidgen | tr '[:upper:]' '[:lower:]')"
  code="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$INVENTORY_URL/reservations" \
    -H 'Content-Type: application/json' \
    -d "{\"orderId\":\"$order_id\",\"items\":[{\"productId\":\"$PRODUCT\",\"quantity\":1}]}")"
  echo "$code $order_id"
}
export -f reserve

echo "Başlangıç stoğu:"
curl -fsS "$INVENTORY_URL/products/$PRODUCT"; echo

echo "$N eşzamanlı rezervasyon isteği gönderiliyor..."
seq 1 "$N" | xargs -P "$N" -I{} bash -c reserve > "$RESULTS"

echo
echo "Sonuç (HTTP kodu -> adet):"
cut -d' ' -f1 "$RESULTS" | sort | uniq -c

echo
echo "Bitiş stoğu:"
curl -fsS "$INVENTORY_URL/products/$PRODUCT"; echo

WINNERS=$(grep -c '^201 ' "$RESULTS" || true)
if [[ "$WINNERS" == "1" ]]; then
  printf '\n\033[1;32mBAŞARILI: %s istekten yalnızca 1 tanesi ürünü aldı.\033[0m\n' "$N"
else
  printf '\n\033[1;31mHATA: %s istek ürünü aldı (beklenen 1).\033[0m\n' "$WINNERS"
fi

# Testi tekrar çalıştırabilmek için kazananın rezervasyonunu bırak (stok tekrar 1 olur)
grep '^201 ' "$RESULTS" | cut -d' ' -f2 | while read -r id; do
  curl -fsS -o /dev/null -X POST "$INVENTORY_URL/reservations/$id/release"
done
rm -f "$RESULTS"
echo "Stok sıfırlandı; test tekrar çalıştırılabilir."
