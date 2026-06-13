import { apiRequest } from "./client";

export type ContactPayload = {
  name: string;
  email: string;
  message: string;
  company?: string;
};

export type ContactResponse = {
  success: boolean;
  message: string;
};

export async function submitContactForm(payload: ContactPayload): Promise<ContactResponse> {
  return apiRequest<ContactResponse>("/api/contact", {
    method: "POST",
    auth: false,
    body: JSON.stringify(payload),
  });
}
