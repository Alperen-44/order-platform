import { useQuery } from "@tanstack/react-query";
import { useAuth } from "../auth/AuthProvider";
import { apiGet } from "../lib/api";
import { formatDateTime, shortId } from "../lib/format";
import type { Notification } from "../lib/types";
import { PageHeader } from "../components/PageHeader";
import { EmptyState } from "../components/EmptyState";
import { LoginPrompt } from "../components/LoginPrompt";

export function NotificationsPage() {
  const { user } = useAuth();

  return (
    <>
      <PageHeader title="Bildirimler" />
      {user ? (
        <NotificationList />
      ) : (
        <LoginPrompt title="Bildirimlerini görmek için giriş yap">
          Siparişin onaylandığında ya da iptal edildiğinde gönderilen mesajlar burada görünür.
        </LoginPrompt>
      )}
    </>
  );
}

function NotificationList() {
  const notifications = useQuery({
    queryKey: ["notifications"],
    queryFn: ({ signal }) => apiGet<Notification[]>("/api/notifications", signal),
    refetchInterval: 5_000,
  });

  if (notifications.isPending) return <p className="text-muted">Bildirimler yükleniyor…</p>;
  if (notifications.isError) {
    return <EmptyState title="Bildirimler yüklenemedi">{notifications.error.message}</EmptyState>;
  }
  if (notifications.data.length === 0) {
    return <EmptyState title="Henüz bildirim yok">Bir sipariş onaylandığında ya da iptal edildiğinde burada görünecek.</EmptyState>;
  }

  return (
    <ul className="space-y-3">
      {notifications.data.map((n) => (
        <li key={n.id} className="rounded-xl border border-line bg-surface px-5 py-4">
          <p className="text-[16px]">{n.message}</p>
          <p className="mt-1 text-sm text-muted">
            Sipariş {shortId(n.orderId)}, {formatDateTime(n.createdAt)}
          </p>
        </li>
      ))}
    </ul>
  );
}
