using AdventurePacks.Api.Domain.Story;
using AdventurePacks.Api.Services.Story.Composite;
using Xunit;

namespace Adventrya.Story.Tests;

/// <summary>
/// A composite book with no previewed cover to adopt.
///
/// The defect: the composite branch of the illustrator opened the way the legacy branch does —
/// draw the cover first, so a book whose cover cannot be produced stops before spending eight image
/// calls — and on this path the reader-facing cover is a stated failure, always. So every composite
/// book whose preview cover was blank or could not be downloaded failed with LAYOUT_FAILED before
/// its first spread, over a picture the composite path never ships: its one cover master is the
/// wrap, drawn downstream from the accepted anchor.
///
/// One of the classes CompositePipelineTestBase serves; see it for the fixtures these use.
/// </summary>
public class CompositeCoverWithoutPreviewTests : CompositePipelineTestBase
{
    [Fact]
    public async Task A_composite_book_with_no_previewed_cover_is_drawn_without_asking_for_one()
    {
        var images = new StubImageService();
        var pipeline = new CoverCountingPipeline(
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images));

        var generator = Generator(images, pipeline, compositeEnabled: true);

        var book = await generator.IllustrateAsync(
            Plan(), Photo(), "image/png", existingCover: null, onImage: null,
            cancellationToken: CancellationToken.None, existingSpreads: null,
            composite: Context());

        // The reader-facing cover drawer was never asked — it would have refused.
        Assert.Equal(0, pipeline.CoverCalls);

        // Eight spreads, eight image calls, and nothing else bought.
        Assert.Equal(BookFormat.SpreadCount, book.Spreads.Count);
        Assert.Equal(BookFormat.SpreadCount, images.ImageCalls);
        Assert.NotNull(book.Composite);

        // The cover slot says what happened: nothing was drawn, and nothing was adopted either.
        Assert.True(book.Cover.Accepted);
        Assert.Equal(0, book.Cover.Attempts);
        Assert.Empty(book.Cover.AttemptDetails);
        Assert.Empty(book.Cover.Image);
    }

    /// <summary>
    /// The other half of the same rule: a previewed cover that IS there is carried through
    /// untouched, with the same zero-attempt record — the composite path draws no cover either way.
    /// </summary>
    [Fact]
    public async Task A_previewed_cover_is_carried_through_and_still_nothing_is_drawn_for_it()
    {
        var images = new StubImageService();
        var pipeline = new CoverCountingPipeline(
            Pipeline(new ScriptedStoryModelClient(ScenarioFixture()), images));
        var previewed = Png(1024, 1536, red: 42);

        var book = await Generator(images, pipeline, compositeEnabled: true).IllustrateAsync(
            Plan(), Photo(), "image/png", existingCover: previewed, onImage: null,
            cancellationToken: CancellationToken.None, existingSpreads: null,
            composite: Context());

        Assert.Equal(0, pipeline.CoverCalls);
        Assert.Equal(previewed, book.Cover.Image);
        Assert.Equal(0, book.Cover.Attempts);
    }

    /// <summary>
    /// The real pipeline behind a counter on the one method the book path must not reach. Wrapping
    /// rather than stubbing, because the assertion is about a whole book being drawn — a stub that
    /// returned a plausible result would let a taken cover branch pass.
    /// </summary>
    private sealed class CoverCountingPipeline(ICompositeBookPipeline inner) : ICompositeBookPipeline
    {
        public int CoverCalls { get; private set; }

        public Task<CompositeBookResult> RunAsync(
            CompositeBookRequest request, CancellationToken cancellationToken) =>
            inner.RunAsync(request, cancellationToken);

        public Task<byte[]> DrawCoverAsync(
            CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
            string childPhotoContentType, CancellationToken cancellationToken)
        {
            CoverCalls++;
            throw new InvalidOperationException(
                "The composite reader-facing cover must not be asked for on the book path.");
        }

        public Task<CompositeCoverWrap> DrawCoverWrapAsync(
            CompositeBookContext context, VisualScenarioV2 scenario, byte[] childPhoto,
            string childPhotoContentType, ChildIdentitySpec identity, byte[]? childAnchor,
            CancellationToken cancellationToken) =>
            inner.DrawCoverWrapAsync(
                context, scenario, childPhoto, childPhotoContentType, identity, childAnchor,
                cancellationToken);
    }
}
