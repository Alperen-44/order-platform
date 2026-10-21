import { createBrowserRouter } from "react-router-dom";
import { Layout } from "./components/Layout";
import { ProductsPage } from "./pages/ProductsPage";
import { CartPage } from "./pages/CartPage";
import { OrdersPage } from "./pages/OrdersPage";
import { OrderDetailPage } from "./pages/OrderDetailPage";
import { NotificationsPage } from "./pages/NotificationsPage";
import { NotFoundPage } from "./pages/NotFoundPage";

// Sayfalar Layout'un içinde (<Outlet />) gösterilir; üst menü her sayfada aynı kalır.
export const router = createBrowserRouter([
  {
    path: "/",
    element: <Layout />,
    children: [
      { index: true, element: <ProductsPage /> },
      { path: "cart", element: <CartPage /> },
      { path: "orders", element: <OrdersPage /> },
      { path: "orders/:orderId", element: <OrderDetailPage /> },
      { path: "notifications", element: <NotificationsPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);
