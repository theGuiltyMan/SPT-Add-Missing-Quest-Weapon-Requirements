using AddMissingQuestRequirements.Pipeline.Quest;

namespace AddMissingQuestRequirements.Spt;

/// <summary>
/// Write-back contract for a single CounterCreator sub-condition. Matches the
/// real SPT <c>QuestConditionCounterCondition</c> property shape
/// (<see cref="HashSet{T}"/> for <see cref="Weapon"/>,
/// <see cref="IEnumerable{T}"/> of <see cref="List{T}"/> for the mods fields) —
/// confirmed in the Phase 10 spec's recon section.
/// </summary>
public interface IConditionWriteTarget
{
    HashSet<string>? Weapon { get; set; }
    IEnumerable<List<string>>? WeaponModsInclusive { get; set; }
    IEnumerable<List<string>>? WeaponModsExclusive { get; set; }
}

/// <summary>
/// Reassigns the writable fields on an SPT sub-condition from a patched
/// <see cref="ConditionNode"/>. All collections are freshly allocated so
/// post-writeback mutation of the ConditionNode cannot leak into SPT's view.
/// </summary>
public static class QuestWriteback
{
    /// <summary>
    /// For each (patched, target) pair, reassigns <c>Weapon</c>,
    /// <c>WeaponModsInclusive</c>, and <c>WeaponModsExclusive</c> on the target.
    /// <c>Weapon</c> becomes a concrete <see cref="HashSet{T}"/> (never null)
    /// using <see cref="StringComparer.Ordinal"/>. The mod groups become a
    /// concrete <see cref="List{T}"/> of <see cref="List{T}"/> (never null),
    /// with each inner group defensively copied.
    /// </summary>
    public static void WriteBack(IEnumerable<(ConditionNode Patched, IConditionWriteTarget Target)> pairs)
    {
        foreach (var (patched, target) in pairs)
        {
            target.Weapon = new HashSet<string>(patched.Weapon, StringComparer.Ordinal);

            var inclusive = new List<List<string>>(patched.WeaponModsInclusive.Count);
            foreach (var group in patched.WeaponModsInclusive)
            {
                inclusive.Add(new List<string>(group));
            }
            target.WeaponModsInclusive = inclusive;

            var exclusive = new List<List<string>>(patched.WeaponModsExclusive.Count);
            foreach (var group in patched.WeaponModsExclusive)
            {
                exclusive.Add(new List<string>(group));
            }
            target.WeaponModsExclusive = exclusive;
        }
    }
}
