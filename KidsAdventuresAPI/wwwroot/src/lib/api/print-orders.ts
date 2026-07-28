import { apiRequest } from "./client";
import type {
  AddressResponse,
  PrintOrderResponse,
  SaveAddressRequest,
  ShippingAddressRequest,
} from "./types";

export async function listPrintOrders(): Promise<PrintOrderResponse[]> {
  return apiRequest<PrintOrderResponse[]>("/api/print-orders");
}

export async function getPrintOrder(id: string): Promise<PrintOrderResponse> {
  return apiRequest<PrintOrderResponse>(`/api/print-orders/${id}`);
}

export async function updatePrintOrderAddress(
  id: string,
  address: ShippingAddressRequest,
): Promise<PrintOrderResponse> {
  return apiRequest<PrintOrderResponse>(`/api/print-orders/${id}/address`, {
    method: "PUT",
    body: JSON.stringify(address),
  });
}

export async function listAddresses(): Promise<AddressResponse[]> {
  return apiRequest<AddressResponse[]>("/api/addresses");
}

export async function saveAddress(request: SaveAddressRequest): Promise<AddressResponse> {
  return apiRequest<AddressResponse>("/api/addresses", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function deleteAddress(id: string): Promise<void> {
  await apiRequest<void>(`/api/addresses/${id}`, { method: "DELETE" });
}
