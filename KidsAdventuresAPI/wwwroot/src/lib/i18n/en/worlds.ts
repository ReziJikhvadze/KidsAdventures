/**
 * Copy for the six Beki worlds.
 *
 * `into` is the locative form used inside generated sentences ("the map leads
 * them <into the dinosaur world>"). English does not inflect the noun, but the
 * key is kept so both catalogues share one shape.
 */
export const worlds = {
  dinosaurs: {
    theme: "Dinosaurs",
    mapLabel: "Rex’s Valley",
    place: "The Lost Valley",
    into: "to the dinosaurs",
    mapTitle: "The Lost Valley",
    chapter: "Chapter I · The Lost Valley",
    bookTitle: (hero: string) => `${hero} in the Lost Valley`,
    synopsis: (hero: string) => "Strange tracks. An old friend. One great adventure.",
    teaserTitle: "An ancient friendship",
    teaserBody: "Your child meets Rex, and together they discover the Lost Valley.",
    memoryTitle: "The Valley of the Dinosaurs",
    memoryBody:
      "This is where they first met Rex — the friend who follows them into every new chapter.",
  },
  space: {
    theme: "Space",
    mapLabel: "Path of Stars",
    place: "The Path of Stars",
    into: "among the stars",
    mapTitle: "The Path of Stars",
    chapter: "Chapter II · The Path of Stars",
    bookTitle: (hero: string) => `${hero} on the Path of Stars`,
    synopsis: (hero: string) => "Old friends. A new world. A path towards the stars.",
    teaserTitle: "Beyond the stars",
    teaserBody: "Distant planets, star maps, and a friend along the way.",
    memoryTitle: "The Star Observatory",
    memoryBody: "Here they found the lost star and lit a new path across the map.",
  },
  pirates: {
    theme: "Pirates",
    mapLabel: "Secret Island",
    place: "The Secret of the Shimmering Island",
    into: "to the pirates",
    mapTitle: "The Secret Island",
    chapter: "Chapter I · The Secret Island",
    bookTitle: (hero: string) => `${hero} and the Secret of the Shimmering Island`,
    synopsis: (hero: string) =>
      `An old golden map leads ${hero} to an island hidden out at sea, where every step uncovers another secret.`,
    teaserTitle: "The secret island",
    teaserBody: "An old map leads to a story hidden out at sea.",
    memoryTitle: "The Shimmering Pirate Island",
    memoryBody: "An old golden map points to an island hidden out at sea.",
  },
  animals: {
    theme: "The world of animals",
    mapLabel: "Friends’ Forest",
    place: "The Enchanted Forest",
    into: "into the enchanted forest",
    mapTitle: "New Friends of the Forest",
    chapter: "Chapter I · The Enchanted Forest",
    bookTitle: (hero: string) => `${hero} and the Friends of the Enchanted Forest`,
    synopsis: (hero: string) =>
      `${hero} steps into a glowing forest where every creature keeps a small secret, and friendship arrives in the most unexpected way.`,
    teaserTitle: "The enchanted forest",
    teaserBody: "A forest where every creature keeps a small secret.",
    memoryTitle: "The Enchanted Forest",
    memoryBody: "The glowing forest keeps its secret for a future book.",
  },
  airplanes: {
    theme: "Aeroplanes",
    mapLabel: "City of Clouds",
    place: "The City of Clouds",
    into: "beyond the clouds",
    mapTitle: "The City Beyond the Clouds",
    chapter: "Chapter I · The City of Clouds",
    bookTitle: (hero: string) => `${hero} and the City Hidden Beyond the Clouds`,
    synopsis: (hero: string) =>
      `${hero}'s first great flight heads for an unknown horizon — to where a whole city lies hidden beyond the clouds.`,
    teaserTitle: "Above the clouds",
    teaserBody: "A first great flight towards an unknown horizon.",
    memoryTitle: "The Kingdom of Clouds",
    memoryBody: "Another path shows beyond the clouds — the next adventure will open it.",
  },
  magic: {
    theme: "A magical world",
    mapLabel: "Gate of Light",
    place: "The City of Light",
    into: "into the world of magic",
    mapTitle: "The Gate of Light",
    chapter: "Chapter I · The Gate of Light",
    bookTitle: (hero: string) => `${hero} in the City of Light`,
    synopsis: (hero: string) => "One small choice. One great change.",
    teaserTitle: "The gate of light",
    teaserBody: "Living books, and a city that shows itself only to the brave.",
    memoryTitle: "The City of Light",
    memoryBody: "The city gate begins to glow softly when it hears the name.",
  },
};
