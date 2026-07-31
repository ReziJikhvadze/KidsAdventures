export type AdapterContext = {
  requestId: string;
  idempotencyKey: string;
};

export type VerifiedProviderEvent<TPayload> = {
  providerEventId: string;
  occurredAt: string;
  payload: TPayload;
};

export interface PaymentAdapter {
  verifyWebhook(
    headers: Headers,
    rawBody: Uint8Array,
  ): Promise<VerifiedProviderEvent<unknown>>;
  refund(
    paymentReference: string,
    amountMinor: number,
    context: AdapterContext,
  ): Promise<{ refundReference: string; status: string }>;
}

export interface GenerationAdapter {
  startBookGeneration(
    bookId: string,
    context: AdapterContext,
  ): Promise<{ externalJobId: string }>;
  getJob(externalJobId: string): Promise<{ status: string }>;
  verifyWebhook(
    headers: Headers,
    rawBody: Uint8Array,
  ): Promise<VerifiedProviderEvent<unknown>>;
}

export type CreateShipmentInput = {
  recipientName: string;
  phone: string;
  address: Record<string, string>;
  deliveryNote?: string;
  pickupLocation: string;
  quantity: number;
  weightGrams: number;
};

export interface CourierAdapter {
  createShipment(
    input: CreateShipmentInput,
    context: AdapterContext,
  ): Promise<{
    externalOrderId: string;
    trackingId?: string;
    status: string;
  }>;
  getShipment(externalOrderId: string): Promise<{ status: string }>;
  cancelShipment(
    externalOrderId: string,
    context: AdapterContext,
  ): Promise<void>;
  verifyWebhook(
    headers: Headers,
    rawBody: Uint8Array,
  ): Promise<VerifiedProviderEvent<unknown>>;
}

export interface NotificationAdapter {
  sendEmail(
    template: string,
    recipient: string,
    data: Record<string, unknown>,
    context: AdapterContext,
  ): Promise<{ messageId: string }>;
  sendSms(
    template: string,
    recipient: string,
    data: Record<string, unknown>,
    context: AdapterContext,
  ): Promise<{ messageId: string }>;
}
