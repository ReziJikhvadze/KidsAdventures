import { OrderDetailClient } from "./OrderDetailClient";

export default async function OrderDetailPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ tab?: string | string[] }>;
}) {
  const { id } = await params;
  const query = await searchParams;
  const requestedTab = Array.isArray(query.tab) ? query.tab[0] : query.tab;
  return <OrderDetailClient orderId={id} initialTab={requestedTab} />;
}
