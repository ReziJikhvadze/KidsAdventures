import { apiRequest } from "./client";

export type CaptureLeadPayload = {
  email: string;
  source?: string;
  childName?: string;
  theme?: string;
  company?: string;
};

export type CaptureLeadResponse = {
  success: boolean;
  message: string;
};

export async function captureLead(payload: CaptureLeadPayload): Promise<CaptureLeadResponse> {
  return apiRequest<CaptureLeadResponse>("/api/leads", {
    method: "POST",
    auth: false,
    body: JSON.stringify(payload),
  });
}
