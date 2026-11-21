using System;
using System.IO;
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
                configFile = Path.Combine(AppContext.BaseDirectory,
                    string.IsNullOrWhiteSpace(ConfigPath) ? DefaultConfigPath : ConfigPath);
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
            var configFile = Path.Combine(AppContext.BaseDirectory,
                string.IsNullOrWhiteSpace(ConfigPath) ? DefaultConfigPath : ConfigPath);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(configFile, json);
        }

        public void InitializeVocabulary()
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory,
                string.IsNullOrWhiteSpace(VocabularyPath) ? DefaultVocabularyPath : VocabularyPath);
            var builder = new SqliteConnectionStringBuilder { DataSource = dbPath };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS Vocabulary (word TEXT PRIMARY KEY, frequency REAL, language_variant TEXT)";
            command.ExecuteNonQuery();
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