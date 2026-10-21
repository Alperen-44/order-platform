// Siteyi deneyen birinin (mülakatçı, hoca) hangi hesapla neyi göreceğini bilmesi için.
// Kullanıcılar infra/keycloak/realm-order-platform.json dosyasında tanımlı. declined ve slowpay'in
// Keycloak kimlikleri, Payment Service'teki test müşterileriyle aynı: ödeme sonucu önceden belli.
const accounts = [
  { username: "alice", password: "alice123", result: "Ödeme başarılı, sipariş onaylanır." },
  { username: "bob", password: "bob123", result: "Ödeme başarılı. Alice'in siparişlerini göremez." },
  {
    username: "declined",
    password: "declined123",
    result: "Kart her zaman reddedilir: ayrılan stok geri bırakılır, sipariş iptal edilir.",
  },
  {
    username: "slowpay",
    password: "slowpay123",
    result: "Ödeme 25 sn sürer. Sipariş 15 sn'de zaman aşımıyla iptal edilir, geç gelen ödeme iade edilir.",
  },
];

export function DemoAccounts() {
  return (
    <details className="group rounded-xl border border-line bg-surface">
      <summary className="cursor-pointer list-none px-5 py-4 font-semibold marker:hidden">
        <span className="inline-block w-4 text-muted transition-transform group-open:rotate-90">›</span> Deneme
        hesapları
      </summary>
      <div className="border-t border-line px-5 py-4">
        <p className="text-[15px] text-muted">
          Her hesap saga'nın farklı bir yolunu gösterir. Giriş yap, sepete bir ürün ekle ve sipariş ver.
        </p>
        <table className="mt-3 w-full text-left text-[15px]">
          <thead>
            <tr className="text-sm text-muted">
              <th className="py-2 pr-4 font-medium">Kullanıcı</th>
              <th className="py-2 pr-4 font-medium">Şifre</th>
              <th className="py-2 font-medium">Ne olur</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {accounts.map((a) => (
              <tr key={a.username} className="align-top">
                <td className="py-2 pr-4 font-semibold">{a.username}</td>
                <td className="py-2 pr-4 tabular-nums">{a.password}</td>
                <td className="py-2">{a.result}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}
