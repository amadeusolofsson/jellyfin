using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
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

        _libraryManager.Setup(x => x.GetCollectionFolders(It.IsAny<BaseItem>())).Returns(new List<Folder>());

        BaseItem.LocalizationManager = _localizationManager.Object;
        BaseItem.LibraryManager = _libraryManager.Object;
        BaseItem.Logger = Mock.Of<ILogger<BaseItem>>();
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

    [Theory]
    // TV-MA series, unrated episode -> blocked for TV-PG user (500)
    [InlineData("TV-MA", null, 500, false)]
    // TV-PG series, unrated episode -> allowed for TV-PG user (500)
    [InlineData("TV-PG", null, 500, true)]
    // TV-PG series, TV-MA episode -> blocked for TV-PG user (500)
    [InlineData("TV-PG", "TV-MA", 500, false)]
    // User has no parental rating restriction -> allowed
    [InlineData("TV-MA", null, null, true)]
    public void IsParentalAllowed_VariousRatingCombinations_EvaluatesInheritedRating(
        string? seriesRating,
        string? episodeRating,
        int? userMaxRating,
        bool expectedAllowed)
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

        episode.OnMetadataChanged();

        var user = new User("test", "test", "test")
        {
            MaxParentalRatingScore = userMaxRating
        };

        var allowed = episode.IsParentalAllowed(user, skipAllowedTagsCheck: true);

        Assert.Equal(expectedAllowed, allowed);
    }
}
