import { Link } from "react-router-dom";
import { EmptyState } from "../components/EmptyState";

export function NotFoundPage() {
  return (
    <EmptyState title="Bu sayfa yok">
      Adresi kontrol et ya da{" "}
      <Link to="/" className="font-semibold text-ink underline underline-offset-4">
        ürünlere dön
      </Link>
      .
    </EmptyState>
  );
}
