import { dataUrlToFile } from "@/lib/api/utils";
import type { DraftCharacter } from "@/lib/journey/draft";
import { resolvedRelationship } from "@/lib/journey/draft";
import type { SaveCharacterInput } from "@/lib/api/types";
import * as charactersApi from "@/lib/api/characters";

export async function ensureServerCharacters(
  characters: DraftCharacter[],
): Promise<{ primaryId: string; supportingIds: string[]; updated: DraftCharacter[] }> {
  const updated: DraftCharacter[] = [];
  let primaryId = "";
  const supportingIds: string[] = [];

  for (const character of characters) {
    // Only a portrait the parent chose in this session is bytes worth sending. A saved hero's
    // photo is on the account already and is shown from an object URL, which is not a data URL
    // and cannot be turned into a file — re-uploading it would also throw away the appearance
    // cache that keeps the child's face the same from book to book.
    const photo =
      !character.photoStored && character.photoDataUrl?.startsWith("data:")
        ? dataUrlToFile(character.photoDataUrl, `${character.name || "portrait"}.jpg`)
        : undefined;

    const input: SaveCharacterInput = {
      name: character.name.trim(),
      birthDate: character.birthDate || undefined,
      gender: character.gender ?? undefined,
      eyeColor: character.eyeColor ?? undefined,
      characterType: character.characterType,
      relationship: character.isPrimary ? undefined : resolvedRelationship(character) || undefined,
      isPrimary: character.isPrimary,
      photo,
    };

    let serverId = character.serverId;
    let isDifferentName = false;

    if (serverId) {
      let originalName = character.originalName;
      if (originalName === undefined) {
        try {
          const remote = await charactersApi.getCharacter(serverId);
          originalName = remote.name;
        } catch {
          originalName = character.name;
        }
      }
      if (character.name.trim().toLowerCase() !== originalName.trim().toLowerCase()) {
        isDifferentName = true;
      }
    }

    if (serverId && !isDifferentName) {
      await charactersApi.updateCharacter(serverId, input);
    } else {
      const created = await charactersApi.createCharacter(input);
      serverId = created.id;
    }

    const next = { ...character, serverId, originalName: input.name };
    updated.push(next);
    if (character.isPrimary) primaryId = serverId;
    else supportingIds.push(serverId);
  }

  if (!primaryId) {
    throw new Error("მთავარი გმირი ვერ შეინახა.");
  }

  return { primaryId, supportingIds, updated };
}
