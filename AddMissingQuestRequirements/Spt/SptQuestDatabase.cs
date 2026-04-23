using AddMissingQuestRequirements.Pipeline.Quest;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// Adapter over <see cref="DatabaseServer"/>'s quest table, exposing the shape
/// the pipeline phases expect via <see cref="IQuestDatabase"/> and a parallel
/// <see cref="Sources"/> map from each emitted <see cref="ConditionNode"/>
/// back to the real SPT <see cref="QuestConditionCounterCondition"/> it came
/// from. <c>QuestWriteback</c> consumes the source map after the patcher
/// finishes so the in-memory SPT database sees the expanded arrays.
/// </summary>
/// <remarks>
/// Construction walks every <c>CounterCreator</c> sub-condition across all
/// five <see cref="QuestConditionTypes"/> buckets
/// (<c>AvailableForFinish</c>, <c>AvailableForStart</c>, <c>Fail</c>,
/// <c>Started</c>, <c>Success</c>). Null buckets are skipped silently.
/// Nullable <see cref="MongoId"/> IDs are flattened to strings via
/// <see cref="MongoId.ToString"/>; <c>null</c> sub-condition IDs become
/// <see cref="string.Empty"/>.
/// </remarks>
public sealed class SptQuestDatabase : IQuestDatabase
{
    private readonly Dictionary<string, QuestNode> _quests;
    private readonly Dictionary<ConditionNode, IConditionWriteTarget> _sources;

    public SptQuestDatabase(
        DatabaseServer databaseServer,
        ISptLogger<SptQuestDatabase> logger)
    {
        var raw = databaseServer.GetTables().Templates?.Quests
            ?? throw new InvalidOperationException(
                "DatabaseServer.GetTables().Templates.Quests is null — database not loaded.");

        _quests = new Dictionary<string, QuestNode>(raw.Count);
        _sources = new Dictionary<ConditionNode, IConditionWriteTarget>();

        foreach (var kvp in raw)
        {
            var quest = kvp.Value;
            var questId = quest.Id.ToString();

            var node = new QuestNode
            {
                Id = questId,
                TraderId = quest.TraderId.ToString(),
                Location = quest.Location ?? string.Empty,
                QuestType = quest.Type.ToString(),
                Conditions = [],
            };

            IngestConditionSections(quest, node);

            _quests[questId] = node;
        }

        logger.Debug(
            $"SptQuestDatabase loaded {_quests.Count} quests, "
            + $"{_sources.Count} CounterCreator sub-conditions.");
    }

    public IReadOnlyDictionary<string, QuestNode> Quests => _quests;

    /// <summary>
    /// Map from each emitted <see cref="ConditionNode"/> back to a write
    /// target wrapping the real SPT sub-condition. Used by
    /// <see cref="QuestWriteback"/> to push expanded arrays back into the
    /// database after the patcher runs.
    /// </summary>
    public IReadOnlyDictionary<ConditionNode, IConditionWriteTarget> Sources => _sources;

    private void IngestConditionSections(Quest quest, QuestNode node)
    {
        var sections = new[]
        {
            quest.Conditions?.AvailableForFinish,
            quest.Conditions?.AvailableForStart,
            quest.Conditions?.Fail,
            quest.Conditions?.Started,
            quest.Conditions?.Success,
        };

        foreach (var section in sections)
        {
            if (section is null)
            {
                continue;
            }

            foreach (var cond in section)
            {
                if (cond.ConditionType != "CounterCreator")
                {
                    continue;
                }

                var subs = cond.Counter?.Conditions;
                if (subs is null)
                {
                    continue;
                }

                var parentConditionId = cond.Id.ToString();

                foreach (var sub in subs)
                {
                    var child = MapSubCondition(parentConditionId, sub, cond.Value);
                    node.Conditions.Add(child);
                    _sources[child] = new SubConditionWriteTarget(sub);
                }
            }
        }
    }

    private static ConditionNode MapSubCondition(
        string parentConditionId,
        QuestConditionCounterCondition sub,
        double? parentKillCount)
    {
        return new ConditionNode
        {
            Id = sub.Id?.ToString() ?? string.Empty,
            // QuestPatcher filters on ConditionType == "CounterCreator" (the parent's type).
            // The SPT sub-condition's own ConditionType is "Kills" / "Shots" / etc., which
            // would make every condition get skipped by QuestPatcher. Mirror SliceLoader:
            // tag each emitted sub-condition with its parent's type.
            ConditionType = "CounterCreator",
            ParentConditionId = parentConditionId,
            Weapon = sub.Weapon?.ToList() ?? [],
            WeaponCaliber = sub.WeaponCaliber?.ToList() ?? [],
            WeaponModsInclusive = (sub.WeaponModsInclusive ?? [])
                .Select(g => new List<string>(g))
                .ToList(),
            WeaponModsExclusive = (sub.WeaponModsExclusive ?? [])
                .Select(g => new List<string>(g))
                .ToList(),
            KillCount = parentKillCount.HasValue ? (int?)parentKillCount.Value : null,
            EnemyTypes = sub.SavageRole?.ToList() ?? [],
            ConditionLocation = sub.Zones?.ToList() ?? [],
            Distance = FormatDistance(sub.Distance),
        };
    }

    private static string? FormatDistance(CounterConditionDistance? distance)
    {
        if (distance is null)
        {
            return null;
        }

        if (!distance.Value.HasValue)
        {
            return null;
        }

        var comparer = string.IsNullOrEmpty(distance.CompareMethod) ? ">=" : distance.CompareMethod;
        return $"{comparer} {distance.Value.Value}";
    }

    /// <summary>
    /// Thin wrapper around a single SPT <see cref="QuestConditionCounterCondition"/>
    /// so <see cref="QuestWriteback"/> can reassign the three patchable
    /// collection properties without taking a direct dependency on the SPT
    /// type. Delegates every get/set straight through.
    /// </summary>
    private sealed class SubConditionWriteTarget : IConditionWriteTarget
    {
        private readonly QuestConditionCounterCondition _sub;

        public SubConditionWriteTarget(QuestConditionCounterCondition sub)
        {
            _sub = sub;
        }

        public HashSet<string>? Weapon
        {
            get => _sub.Weapon;
            set => _sub.Weapon = value;
        }

        public IEnumerable<List<string>>? WeaponModsInclusive
        {
            get => _sub.WeaponModsInclusive;
            set => _sub.WeaponModsInclusive = value;
        }

        public IEnumerable<List<string>>? WeaponModsExclusive
        {
            get => _sub.WeaponModsExclusive;
            set => _sub.WeaponModsExclusive = value;
        }
    }
}
