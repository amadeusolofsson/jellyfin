using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests.Entities;

public class EpisodeTests
{
    private readonly Mock<ILocalizationManager> _localizationManager;
    private readonly Mock<ILibraryManager> _libraryManager;

    public EpisodeTests()
    {
        _localizationManager = new Mock<ILocalizationManager>();
        _libraryManager = new Mock<ILibraryManager>();

        _localizationManager.Setup(x => x.GetRatingScore("TV-MA", null)).Returns(new ParentalRatingScore(900, null));
        _localizationManager.Setup(x => x.GetRatingScore("TV-14", null)).Returns(new ParentalRatingScore(600, null));
        _localizationManager.Setup(x => x.GetRatingScore("TV-PG", null)).Returns(new ParentalRatingScore(500, null));
        _localizationManager.Setup(x => x.GetRatingScore("TV-PG-1", null)).Returns(new ParentalRatingScore(500, 1));
        _localizationManager.Setup(x => x.GetRatingScore("TV-PG-2", null)).Returns(new ParentalRatingScore(500, 2));
        _localizationManager.Setup(x => x.GetRatingScore("TV-G", null)).Returns(new ParentalRatingScore(300, null));

        BaseItem.LocalizationManager = _localizationManager.Object;
        BaseItem.LibraryManager = _libraryManager.Object;
    }

    [Theory]
    // Episode unrated -> inherits series rating
    [InlineData("TV-MA", null, 900, null)]
    // Series unrated -> retains episode rating
    [InlineData(null, "TV-PG", 500, null)]
    // Series more restrictive (900 vs 500)
    [InlineData("TV-MA", "TV-PG", 900, null)]
    // Episode more restrictive (500 vs 900)
    [InlineData("TV-PG", "TV-MA", 900, null)]
    // Equal score -> higher sub-score wins
    [InlineData("TV-PG-2", "TV-PG-1", 500, 2)]
    // Both unrated -> null
    [InlineData(null, null, null, null)]
    public void GetParentalRatingScore_VariousRatingCombinations_ReturnsMostRestrictive(
        string? seriesRating,
        string? episodeRating,
        int? expectedScore,
        int? expectedSubScore)
    {
        var seriesId = Guid.NewGuid();
        var series = new Series
        {
            Id = seriesId,
            OfficialRating = seriesRating
        };
        _libraryManager.Setup(x => x.GetItemById(seriesId)).Returns(series);

        var episode = new Episode
        {
            SeriesId = seriesId,
            OfficialRating = episodeRating
        };

        var score = episode.GetParentalRatingScore();

        if (expectedScore.HasValue)
        {
            Assert.NotNull(score);
            Assert.Equal(expectedScore.Value, score.Score);
            Assert.Equal(expectedSubScore, score.SubScore);
        }
        else
        {
            Assert.Null(score);
        }
    }

    [Fact]
    public void GetParentalRatingScore_NoSeries_ReturnsEpisodeRatingOrNull()
    {
        var episodeUnrated = new Episode();
        Assert.Null(episodeUnrated.GetParentalRatingScore());

        var episodeRated = new Episode
        {
            OfficialRating = "TV-14"
        };
        var score = episodeRated.GetParentalRatingScore();
        Assert.NotNull(score);
        Assert.Equal(600, score.Score);
    }

    [Fact]
    public void OnMetadataChanged_EpisodeInheritsSeriesRatingScore()
    {
        var seriesId = Guid.NewGuid();
        var series = new Series
        {
            Id = seriesId,
            OfficialRating = "TV-MA"
        };
        _libraryManager.Setup(x => x.GetItemById(seriesId)).Returns(series);

        var episode = new Episode
        {
            SeriesId = seriesId
        };

        var updateType = episode.OnMetadataChanged();

        Assert.NotEqual(ItemUpdateType.None, updateType);
        Assert.Equal(900, episode.InheritedParentalRatingValue);
    }
}
