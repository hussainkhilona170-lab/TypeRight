using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using TypeRight.Domain.Models;

namespace TypeRight.Configuration
{
    public class ConfigurationSettings
    {
        public LanguageVariant LanguageVariant { get; set; } = LanguageVariant.Auto;
        public bool PersonalLearningEnabled { get; set; } = false;
        public string[] ExcludedApplications { get; set; } = new string[0];
        public string VocabularyPath { get; set; } = "vocabulary.sqlite";
        public string ConfigPath { get; set; } = "typeRight.json";
        public float ConfidenceThreshold { get; set; } = 0.65f;
        public bool ShowSuggestions { get; set; } = true;

        public void Load()
        {
            var configFile = Path.Combine(AppContext.BaseDirectory, ConfigPath);
            if (File.Exists(configFile))
            {
                var json = File.ReadAllText(configFile);
                var settings = JsonConvert.DeserializeObject<ConfigurationSettings>(json);
                LanguageVariant = settings.LanguageVariant;
                PersonalLearningEnabled = settings.PersonalLearningEnabled;
                ExcludedApplications = settings.ExcludedApplications;
                ConfidenceThreshold = settings.ConfidenceThreshold;
                ShowSuggestions = settings.ShowSuggestions;
            }
        }

        public void Save()
        {
            var configFile = Path.Combine(AppContext.BaseDirectory, ConfigPath);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(configFile, json);
        }

        public void InitializeVocabulary()
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, VocabularyPath);
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS Vocabulary (word TEXT PRIMARY KEY, frequency REAL, language_variant TEXT)";
            command.ExecuteNonQuery();
        }
    }
}