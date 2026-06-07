import { apiRequest } from "./client";

export async function createFamilyMember(params: {
  childId: string;
  name: string;
  relationship: string;
  photoFile?: File;
}): Promise<void> {
  const form = new FormData();
  form.append("childId", params.childId);
  form.append("name", params.name);
  form.append("relationship", params.relationship);
  if (params.photoFile) {
    form.append("photo", params.photoFile);
  }

  await apiRequest("/api/family-members", {
    method: "POST",
    body: form,
  });
}
