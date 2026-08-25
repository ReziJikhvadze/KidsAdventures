namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The model that edits a written book, and the name to call it by — which need not be the model
/// that wrote it.
///
/// A record holding a client and a model name, rather than a second implementation of
/// <see cref="IStoryModelClient"/>, because nothing about the polish call is different: it is the
/// same schema, the same transport and the same parse. The only thing that varies is which vendor
/// is on the other end and what that vendor calls its model, and those are two values, not a type.
///
/// It exists as its own registration so that <see cref="MasterStoryService"/> can be handed the
/// editor without having to ask which provider is configured — the same reason
/// <see cref="Ai.AiServiceRouter"/> exists on the picture side.
/// </summary>
/// <param name="Client">Whichever vendor answers the polish pass.</param>
/// <param name="ModelName">
/// What to ask that vendor for. Meaningful to the OpenAI client, which sends it; the Gemini
/// client deliberately ignores the argument and uses its own configured story model, because an
/// OpenAI product name means nothing to it.
/// </param>
public sealed record StoryPolishClient(IStoryModelClient Client, string ModelName);
