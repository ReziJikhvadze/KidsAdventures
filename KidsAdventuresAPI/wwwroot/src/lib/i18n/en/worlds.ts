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
    into: "into the world of dinosaurs",
    mapTitle: "The Lost Valley of the Dinosaurs",
    chapter: "Chapter I · The Lost Valley",
    bookTitle: (hero: string) => `${hero} and the Secret of the Lost Valley`,
    synopsis: (hero: string) =>
      `When ${hero} finds a strange set of tracks, a journey begins with a new friend called Rex — one that shows what courage really means.`,
    teaserTitle: "An ancient friendship",
    teaserBody: "Your child meets Rex, and together they discover the Lost Valley.",
    memoryTitle: "The Lost Valley of the Dinosaurs",
    memoryBody:
      "This is where they first met Rex — the friend who follows them into every new chapter.",
  },
  space: {
    theme: "Space",
    mapLabel: "Path of Stars",
    place: "The Path of Stars",
    into: "into space",
    mapTitle: "The Lost Path of Stars",
    chapter: "Chapter II · The Path of Stars",
    bookTitle: (hero: string) => `${hero} and the Lost Path of Stars`,
    synopsis: (hero: string) =>
      `${hero} and Rex follow a glowing trail out beyond the moon — to the place where an old friendship opens a whole new world.`,
    teaserTitle: "Beyond the stars",
    teaserBody: "Distant planets, star maps, and a friend along the way.",
    memoryTitle: "The Star Observatory",
    memoryBody: "Here they found the lost star and lit a new path across the map.",
  },
  pirates: {
    theme: "Pirates",
    mapLabel: "Secret Island",
    place: "The Shimmering Island",
    into: "into the world of pirates",
    mapTitle: "The Secret of the Shimmering Island",
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
    theme: "Animals",
    mapLabel: "Friends’ Forest",
    place: "The Enchanted Forest",
    into: "into the world of animals",
    mapTitle: "Friends of the Enchanted Forest",
    chapter: "Chapter I · The Enchanted Forest",
    bookTitle: (hero: string) => `${hero} and the Friends of the Enchanted Forest`,
    synopsis: (hero: string) =>
      `${hero} steps into a glowing forest where every creature keeps a small secret, and friendship arrives in the most unexpected way.`,
    teaserTitle: "The enchanted forest",
    teaserBody: "A forest where every creature keeps a small secret.",
    memoryTitle: "The Enchanted Animal Forest",
    memoryBody: "The glowing forest keeps its secret for a future book.",
  },
  airplanes: {
    theme: "Aeroplanes",
    mapLabel: "City of Clouds",
    place: "The City of Clouds",
    into: "into the world of aeroplanes",
    mapTitle: "The City Hidden Beyond the Clouds",
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
    into: "into the magical world",
    mapTitle: "The Gate to the City of Light",
    chapter: "Chapter I · The Gate of Light",
    bookTitle: (hero: string) => `${hero} and the Gate to the City of Light`,
    synopsis: (hero: string) =>
      `One kind wish carries ${hero} to an enchanted city, where even the smallest choice leaves a great light behind.`,
    teaserTitle: "The gate of light",
    teaserBody: "Living books, and a city that shows itself only to the brave.",
    memoryTitle: "The Magical City of Light",
    memoryBody: "The city gate begins to glow softly when it hears the name.",
  },
};
