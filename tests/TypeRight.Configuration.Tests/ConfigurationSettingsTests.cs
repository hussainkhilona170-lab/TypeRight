using Microsoft.Data.Sqlite;
using TypeRight.Configuration;
using TypeRight.Domain.Models;

namespace TypeRight.Configuration.Tests;

public sealed class ConfigurationSettingsTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "TypeRight.Config.Tests." + Guid.NewGuid().ToString("N"));

    public ConfigurationSettingsTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void LoadPersonalWords_ReturnsEmptyWhenLearningDisabled()
    {
        var settings = CreateSettings(enablePersonalLearning: false);

        Assert.Empty(settings.LoadPersonalWords());
        Assert.False(File.Exists(settings.VocabularyPath));
    }

    [Fact]
    public void AddPersonalWord_IsOptInAndRejectsInvalidValues()
    {
        var disabled = CreateSettings(enablePersonalLearning: false);
        Assert.False(disabled.AddPersonalWord("qwerty"));
        Assert.False(File.Exists(disabled.VocabularyPath));

        var enabled = CreateSettings();
        Assert.False(enabled.AddPersonalWord("   "));
        Assert.False(enabled.AddPersonalWord(" two words"));
        Assert.False(enabled.AddPersonalWord("line\nbreak"));
        Assert.False(enabled.AddPersonalWord("tab\tword"));
        Assert.False(enabled.AddPersonalWord(new string('a', 129)));
    }

    [Fact]
    public void AddPersonalWord_RoundTripsOnceAndIncrementsFrequency()
    {
        var settings = CreateSettings();

        Assert.True(settings.AddPersonalWord("qwerty", LanguageVariant.EnUs));
        Assert.True(settings.AddPersonalWord("QWERTY", LanguageVariant.EnUs));

        var words = settings.LoadPersonalWords();
        Assert.Single(words);
        Assert.Equal("qwerty", words[0]);

        var builder = new SqliteConnectionStringBuilder { DataSource = settings.VocabularyPath };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT frequency, language_variant FROM Vocabulary WHERE word = 'qwerty'";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(2d, reader.GetDouble(0));
        Assert.Equal("EnUs", reader.GetString(1));
    }

    [Fact]
    public void LoadPersonalWords_ReturnsEmptyForMissingOrCorruptDatabase()
    {
        var missing = CreateSettings();
        Assert.Empty(missing.LoadPersonalWords());

        var corrupt = CreateSettings();
        File.WriteAllText(corrupt.VocabularyPath, "this is not a sqlite database");
        Assert.Empty(corrupt.LoadPersonalWords());
    }

    [Fact]
    public void InitializeVocabulary_IsIdempotent()
    {
        var settings = CreateSettings();

        settings.InitializeVocabulary();
        settings.InitializeVocabulary();

        Assert.True(File.Exists(settings.VocabularyPath));
    }

    [Fact]
    public void GetPersonalWordCount_IsOptInAndCountsWithoutTextAccess()
    {
        var disabled = CreateSettings(enablePersonalLearning: false);
        Assert.Equal(0, disabled.GetPersonalWordCount());
        Assert.False(File.Exists(disabled.VocabularyPath));

        var enabled = CreateSettings();
        Assert.Equal(0, enabled.GetPersonalWordCount());

        Assert.True(enabled.AddPersonalWord("first", LanguageVariant.EnUs));
        Assert.True(enabled.AddPersonalWord("SECOND", LanguageVariant.EnGb));
        Assert.True(enabled.AddPersonalWord("first", LanguageVariant.EnUs));
        Assert.Equal(2, enabled.GetPersonalWordCount());
    }

    [Fact]
    public void GetPersonalWordCount_ReturnsZeroForCorruptDatabase()
    {
        var corrupt = CreateSettings();
        File.WriteAllText(corrupt.VocabularyPath, "this is not a sqlite database");

        Assert.Equal(0, corrupt.GetPersonalWordCount());
    }

    private ConfigurationSettings CreateSettings(bool enablePersonalLearning = true)
        => new()
        {
            ConfigPath = Path.Combine(_tempDirectory, "typeRight.json"),
            VocabularyPath = Path.Combine(_tempDirectory, "vocabulary.sqlite"),
            PersonalLearningEnabled = enablePersonalLearning
        };
}
