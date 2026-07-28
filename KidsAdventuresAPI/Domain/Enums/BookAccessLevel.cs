namespace AdventurePacks.Api.Domain.Enums;

/// <summary>
/// How much of a book the parent may read. Replaces the old arrangement where the
/// story was free and the PDF cost a credit: now the cover and first page are the
/// free sample, and paying opens the rest.
/// </summary>
public enum BookAccessLevel
{
    /// <summary>Cover plus page one.</summary>
    Preview = 0,

    /// <summary>Every page, the PDF, and the adventure-map unlock.</summary>
    Full = 1
}
