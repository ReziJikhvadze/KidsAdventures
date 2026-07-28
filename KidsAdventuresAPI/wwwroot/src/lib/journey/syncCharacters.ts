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
    const photo =
      character.photoDataUrl != null
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
    if (serverId) {
      await charactersApi.updateCharacter(serverId, input);
    } else {
      const created = await charactersApi.createCharacter(input);
      serverId = created.id;
    }

    const next = { ...character, serverId };
    updated.push(next);
    if (character.isPrimary) primaryId = serverId;
    else supportingIds.push(serverId);
  }

  if (!primaryId) {
    throw new Error("მთავარი გმირი ვერ შეინახა.");
  }

  return { primaryId, supportingIds, updated };
}
