import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { useCart } from "../cart/CartProvider";

const navItems = [
  { to: "/", label: "Ürünler", end: true },
  { to: "/orders", label: "Siparişlerim", end: false },
  { to: "/notifications", label: "Bildirimler", end: false },
];

export function Layout() {
  const { user, login, logout } = useAuth();
  const { count } = useCart();

  return (
    <div className="min-h-screen">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center gap-x-8 gap-y-3 px-4 py-4 sm:px-6">
          <NavLink to="/" className="flex items-center gap-2.5 rounded-md">
            <span aria-hidden className="grid h-8 w-8 place-items-center rounded-md bg-ink">
              <span className="h-2.5 w-4 rounded-[3px] bg-tag" />
            </span>
            <span className="font-display text-xl font-bold tracking-tight">Tezgâh</span>
          </NavLink>

          <nav aria-label="Ana menü" className="order-3 flex w-full gap-1 sm:order-none sm:w-auto">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  [
                    "rounded-md px-3 py-1.5 text-[15px] font-medium transition-colors",
                    isActive ? "bg-ink text-white" : "text-muted hover:bg-canvas hover:text-ink",
                  ].join(" ")
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            <NavLink
              to="/cart"
              className={({ isActive }) =>
                [
                  "flex items-center gap-2 rounded-md px-3 py-1.5 text-[15px] font-medium",
                  isActive ? "bg-ink text-white" : "text-ink hover:bg-canvas",
                ].join(" ")
              }
            >
              Sepet
              {count > 0 && (
                <span className="min-w-6 rounded-full bg-tag px-1.5 text-center text-sm font-bold text-ink tabular-nums">
                  {count}
                </span>
              )}
            </NavLink>
            {user ? (
              <>
                <div className="text-right leading-tight">
                  <p className="text-[15px] font-semibold">{user.displayName}</p>
                  {user.email && <p className="text-xs text-muted">{user.email}</p>}
                </div>
                <button
                  type="button"
                  onClick={logout}
                  className="rounded-md border border-line px-3 py-1.5 text-[15px] font-medium text-muted hover:border-ink hover:text-ink"
                >
                  Çıkış yap
                </button>
              </>
            ) : (
              <button
                type="button"
                onClick={login}
                className="rounded-md bg-ink px-4 py-1.5 text-[15px] font-semibold text-white hover:bg-ink/90"
              >
                Giriş yap
              </button>
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6">
        <Outlet />
      </main>
    </div>
  );
}
