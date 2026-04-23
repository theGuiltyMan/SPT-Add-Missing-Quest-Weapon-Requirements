using AddMissingQuestRequirements.Models;
using AddMissingQuestRequirements.Pipeline.Shared;
using AddMissingQuestRequirements.Util;
using FluentAssertions;

namespace AddMissingQuestRequirements.Tests.Pipeline.Shared;

public class GroupExpanderTests
{
    private sealed class FakeCat : IItemCategorization
    {
        public IReadOnlyDictionary<string, IReadOnlySet<string>> ItemToType { get; init; } = new Dictionary<string, IReadOnlySet<string>>();
        public IReadOnlyDictionary<string, IReadOnlySet<string>> TypeToItems { get; init; } = new Dictionary<string, IReadOnlySet<string>>();
        public IReadOnlyDictionary<string, IReadOnlySet<string>> CanBeUsedAs { get; init; } = new Dictionary<string, IReadOnlySet<string>>();
        public IReadOnlySet<string> KnownItemIds { get; init; } = new HashSet<string>();
    }

    private static FakeCat MakeCat()
    {
        return new FakeCat
        {
            ItemToType = new Dictionary<string, IReadOnlySet<string>>
            {
                ["a"] = new HashSet<string> { "T" },
                ["b"] = new HashSet<string> { "T" },
            },
            TypeToItems = new Dictionary<string, IReadOnlySet<string>>
            {
                ["T"] = new HashSet<string> { "a", "b" },
            },
            KnownItemIds = new HashSet<string> { "a", "b", "u" },  // u is in DB but uncategorized
        };
    }

    [Fact]
    public void BucketAndLog_splits_into_three_buckets()
    {
        var cat    = MakeCat();
        var logger = new CapturingModLogger();

        var buckets = GroupExpander.BucketAndLog(
            ["a", "u", "not_exists"], cat,
            UnknownWeaponHandling.KeepAll, logger, "c1", "weapon", NullNameResolver.Instance);

        buckets.Categorized.Should().BeEquivalentTo(["a"]);
        buckets.UncategorizedInDb.Should().BeEquivalentTo(["u"]);
        buckets.NotInDb.Should().BeEquivalentTo(["not_exists"]);
        buckets.KeepUncategorizedInDb.Should().BeTrue();
        buckets.KeepNotInDb.Should().BeTrue();
    }

    [Fact]
    public void BucketAndLog_Strip_sets_both_flags_false_and_warns()
    {
        var cat    = MakeCat();
        var logger = new CapturingModLogger();

        var buckets = GroupExpander.BucketAndLog(
            ["a", "u", "not_exists"], cat,
            UnknownWeaponHandling.Strip, logger, "c1", "weapon", NullNameResolver.Instance);

        buckets.KeepUncategorizedInDb.Should().BeFalse();
        buckets.KeepNotInDb.Should().BeFalse();
        logger.Warnings.Should().Contain(w => w.Contains("'u'"));
        logger.Warnings.Should().Contain(w => w.Contains("not_exists"));
    }

    [Fact]
    public void BucketAndLog_KeepInDb_keeps_only_in_db_bucket()
    {
        var cat    = MakeCat();
        var logger = new CapturingModLogger();

        var buckets = GroupExpander.BucketAndLog(
            ["a", "u", "not_exists"], cat,
            UnknownWeaponHandling.KeepInDb, logger, "c1", "weapon", NullNameResolver.Instance);

        buckets.KeepUncategorizedInDb.Should().BeTrue();
        buckets.KeepNotInDb.Should().BeFalse();
        logger.Warnings.Should().Contain(w => w.Contains("not_exists"));
        logger.Warnings.Should().NotContain(w => w.Contains("'u'"));
    }

    [Fact]
    public void ApplyAliasesAndReattach_reattaches_preserved_buckets()
    {
        var cat = MakeCat();
        var buckets = new ExpansionBuckets
        {
            Categorized           = ["a"],
            UncategorizedInDb     = ["u"],
            NotInDb               = ["n"],
            KeepUncategorizedInDb = true,
            KeepNotInDb           = true,
        };

        var working = new List<string> { "a", "b" };

        var result = GroupExpander.ApplyAliasesAndReattach(working, buckets, cat);

        result.Should().BeEquivalentTo(["a", "b", "u", "n"]);
    }

    [Fact]
    public void ApplyAliasesAndReattach_applies_canBeUsedAs_and_respects_exclude()
    {
        var cat = new FakeCat
        {
            ItemToType = new Dictionary<string, IReadOnlySet<string>>
            {
                ["a"] = new HashSet<string> { "T" },
            },
            TypeToItems = new Dictionary<string, IReadOnlySet<string>>
            {
                ["T"] = new HashSet<string> { "a" },
            },
            CanBeUsedAs = new Dictionary<string, IReadOnlySet<string>>
            {
                ["a"] = new HashSet<string> { "alias_of_a" },
            },
            KnownItemIds = new HashSet<string> { "a", "u" },
        };

        var buckets = new ExpansionBuckets
        {
            Categorized           = ["a"],
            UncategorizedInDb     = ["u"],
            NotInDb               = [],
            KeepUncategorizedInDb = true,
            KeepNotInDb           = false,
        };

        var result = GroupExpander.ApplyAliasesAndReattach(
            new List<string> { "a" }, buckets, cat,
            excludeSet: new HashSet<string> { "u" });

        result.Should().Contain("a");
        result.Should().Contain("alias_of_a");
        result.Should().NotContain("u", "excludeSet filters the reattach step");
    }
}
