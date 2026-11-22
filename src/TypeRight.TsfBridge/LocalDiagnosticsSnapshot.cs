using System;
using System.Collections.Generic;
using System.Linq;
using TypeRight.Configuration;
using TypeRight.Privacy;
using TypeRight.Suggestions;

namespace TypeRight.TsfBridge;

/// <summary>
/// Content-free snapshot of local health. It combines bridge counters with
/// counts from local settings, feature gates and policy rules. No setting value,
/// rule text, process name, word, path or exception text is ever captured.
/// </summary>
public sealed class LocalDiagnosticsSnapshot
{
    public BridgeHealthSnapshot Bridge { get; }
    public int ActiveFeatureCount { get; }
    public int TotalFeatureCount { get; }
    public int ExcludedApplicationCount { get; }
    public int PersonalWordCount { get; }
    public int BlockedClientCount { get; }
    public int BlockedDomainCount { get; }
    public int IntegrationRuleCount { get; }
    public int SensitivityRuleCount { get; }

    private LocalDiagnosticsSnapshot(BridgeHealthSnapshot bridge, int activeFeatureCount,
                                     int totalFeatureCount, int excludedApplicationCount,
                                     int personalWordCount, int blockedClientCount,
                                     int blockedDomainCount, int integrationRuleCount,
                                     int sensitivityRuleCount)
    {
        Bridge = bridge ?? BridgeHealthSnapshot.Empty;
        ActiveFeatureCount = activeFeatureCount;
        TotalFeatureCount = totalFeatureCount;
        ExcludedApplicationCount = excludedApplicationCount;
        PersonalWordCount = personalWordCount;
        BlockedClientCount = blockedClientCount;
        BlockedDomainCount = blockedDomainCount;
        IntegrationRuleCount = integrationRuleCount;
        SensitivityRuleCount = sensitivityRuleCount;
    }

    /// <summary>
    /// Captures counts only. Any dependency fault yields zero local counts while
    /// preserving the already content-free bridge snapshot.
    /// </summary>
    public static LocalDiagnosticsSnapshot Capture(BridgeHealthSnapshot bridge,
                                                    ConfigurationSettings configuration,
                                                    LocalFeatureGates features,
                                                    PolicyEvaluator policy)
    {
        BridgeHealthSnapshot safeBridge = bridge ?? BridgeHealthSnapshot.Empty;
        int totalFeatures = Enum.GetValues(typeof(FeatureKey)).Length;

        try
        {
            IReadOnlyDictionary<FeatureKey, bool> gates = features?.Snapshot();
            int activeFeatures = gates?.Count(pair => pair.Value) ?? 0;
            int excluded = configuration?.ExcludedApplications?.Length ?? 0;
            int personalWords = configuration?.GetPersonalWordCount() ?? 0;

            return new LocalDiagnosticsSnapshot(
                safeBridge,
                activeFeatures,
                totalFeatures,
                excluded,
                personalWords,
                policy?.BlockedClientCount ?? 0,
                policy?.BlockedDomainCount ?? 0,
                policy?.RuleCount ?? 0,
                policy?.SensitivityRuleCount ?? 0);
        }
        catch (Exception)
        {
            // Diagnostics must never become a content or availability risk.
            return new LocalDiagnosticsSnapshot(safeBridge, 0, totalFeatures, 0, 0, 0, 0, 0, 0);
        }
    }
}
