using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using TypeRight.Domain.Models;

namespace TypeRight.Configuration
{
    public class ConfigurationSettings
    {
        private const string DefaultConfigPath = "typeRight.json";
        private const string DefaultVocabularyPath = "vocabulary.sqlite";
        private const float DefaultConfidenceThreshold = 0.65f;
        private const int MaxPersonalWordLength = 128;

        public LanguageVariant LanguageVariant { get; set; } = LanguageVariant.Auto;
        public bool PersonalLearningEnabled { get; set; } = false;
        public string[] ExcludedApplications { get; set; } = new string[0];
        public string VocabularyPath { get; set; } = DefaultVocabularyPath;
        public string ConfigPath { get; set; } = DefaultConfigPath;
        public float ConfidenceThreshold { get; set; } = DefaultConfidenceThreshold;
        public bool ShowSuggestions { get; set; } = true;

        /// <summary>
        /// Loads local settings. Malformed JSON, null content and IO errors all
        /// leave the instance in a safe default state instead of throwing.
        /// </summary>
        public void Load()
        {
            string configFile;
            try
            {
                configFile = ResolvePath(ConfigPath, DefaultConfigPath);
            }
            catch (Exception)
            {
                ApplyDefaults();
                return;
            }

            if (!File.Exists(configFile))
                return;

            try
            {
                var json = File.ReadAllText(configFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    ApplyDefaults();
                    return;
                }

                var settings = JsonConvert.DeserializeObject<ConfigurationSettings>(json);
                if (settings == null)
                {
                    ApplyDefaults();
                    return;
                }

                LanguageVariant = settings.LanguageVariant;
                PersonalLearningEnabled = settings.PersonalLearningEnabled;
                ExcludedApplications = settings.ExcludedApplications ?? new string[0];
                VocabularyPath = string.IsNullOrWhiteSpace(settings.VocabularyPath)
                    ? DefaultVocabularyPath
                    : settings.VocabularyPath;
                ConfidenceThreshold = float.IsNaN(settings.ConfidenceThreshold)
                    ? DefaultConfidenceThreshold
                    : settings.ConfidenceThreshold;
                ShowSuggestions = settings.ShowSuggestions;
            }
            catch (Exception)
            {
                // Never surface file contents or exception text. Fall back to defaults.
                ApplyDefaults();
            }
        }

        public void Save()
        {
            var configFile = ResolvePath(ConfigPath, DefaultConfigPath);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(configFile, json);
        }

        public void InitializeVocabulary()
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = ResolveVocabularyPath() };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS Vocabulary (word TEXT PRIMARY KEY, frequency REAL, language_variant TEXT)";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Loads accepted personal words. Personal learning is opt-in, so a disabled
        /// setting never opens the database. Corrupt or missing files fail closed to
        /// an empty list instead of throwing into application startup.
        /// </summary>
        public IReadOnlyList<string> LoadPersonalWords()
        {
            if (!PersonalLearningEnabled)
                return Array.Empty<string>();

            try
            {
                var dbPath = ResolveVocabularyPath();
                if (!File.Exists(dbPath))
                    return Array.Empty<string>();

                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                };
                using var connection = new SqliteConnection(builder.ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT word FROM Vocabulary WHERE word IS NOT NULL AND TRIM(word) <> '' ORDER BY word";

                var words = new List<string>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string word = reader.GetString(0)?.Trim();
                    if (!string.IsNullOrEmpty(word))
                        words.Add(word);
                }
                return words;
            }
            catch (Exception)
            {
                // Never surface file content or exception text.
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns the number of accepted personal words without reading any word
        /// text into managed memory. Personal learning is opt-in; a disabled
        /// setting, missing database or corrupt store returns zero.
        /// </summary>
        public int GetPersonalWordCount()
        {
            if (!PersonalLearningEnabled)
                return 0;

            try
            {
                var dbPath = ResolveVocabularyPath();
                if (!File.Exists(dbPath))
                    return 0;

                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                };
                using var connection = new SqliteConnection(builder.ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT COUNT(*) FROM Vocabulary WHERE word IS NOT NULL AND TRIM(word) <> ''";
                object result = command.ExecuteScalar();
                if (result is long count)
                    return (int)Math.Min(count, int.MaxValue);
                return 0;
            }
            catch (Exception)
            {
                // Never surface file content, schema text or exception details.
                return 0;
            }
        }

        /// <summary>
        /// Adds a single accepted word to the local vocabulary. Rejected when
        /// personal learning is off, the value is not a single bounded word, or
        /// storage is unavailable. Repeated additions only increase frequency.
        /// </summary>
        public bool AddPersonalWord(string word, LanguageVariant languageVariant = LanguageVariant.Auto)
        {
            if (!PersonalLearningEnabled)
                return false;

            string normalized = NormalizePersonalWord(word);
            if (normalized == null)
                return false;

            try
            {
                InitializeVocabulary();
                var builder = new SqliteConnectionStringBuilder { DataSource = ResolveVocabularyPath() };
                using var connection = new SqliteConnection(builder.ConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO Vocabulary (word, frequency, language_variant)
VALUES ($word, 1.0, $variant)
ON CONFLICT(word) DO UPDATE SET
    frequency = COALESCE(frequency, 0) + 1,
    language_variant = excluded.language_variant";
                command.Parameters.AddWithValue("$word", normalized);
                command.Parameters.AddWithValue("$variant", languageVariant.ToString());
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormalizePersonalWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return null;

            var trimmed = word.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxPersonalWordLength)
                return null;
            if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl))
                return null;

            // The engine dictionary is case-insensitive; store one canonical form
            // so repeated additions update frequency instead of creating duplicates.
            return trimmed.ToLowerInvariant();
        }

        private string ResolveVocabularyPath()
            => ResolvePath(VocabularyPath, DefaultVocabularyPath);

        private static string ResolvePath(string configured, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
            return Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
        }

        private void ApplyDefaults()
        {
            LanguageVariant = TypeRight.Domain.Models.LanguageVariant.Auto;
            PersonalLearningEnabled = false;
            ExcludedApplications = new string[0];
            VocabularyPath = DefaultVocabularyPath;
            ConfidenceThreshold = DefaultConfidenceThreshold;
            ShowSuggestions = true;
        }
    }
}