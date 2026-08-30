using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Universe"/> onto the <c>universes</c> table.
/// </summary>
/// <remarks>
/// The coverage claim is stored on the universe rather than derived from its
/// membership rows, because the question it answers — <em>is this date
/// known?</em> — has no answer in the rows. A universe with nothing recorded
/// for 2018 and one whose 2018 constituents nobody has sourced look identical
/// from the membership table, and they are opposite facts.
/// </remarks>
internal sealed class UniverseConfiguration : IEntityTypeConfiguration<Universe>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Universe> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "universes",
            table => table.HasCheckConstraint(
                "ck_universes_coverage",
                // The claim is whole or absent. A stored end with no start
                // would be a claim nobody can evaluate, and the read that asks
                // "is this date known" would have to guess what it meant.
                """
                (coverage_from IS NULL AND coverage_until IS NULL)
                OR (coverage_from IS NOT NULL
                    AND (coverage_until IS NULL OR coverage_until > coverage_from))
                """));

        builder.HasKey(universe => universe.Id).HasName("pk_universes");

        builder.Property(universe => universe.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new UniverseId(value))
            .ValueGeneratedNever();

        builder.Property(universe => universe.Code)
            .HasColumnName("code")
            .HasConversion(code => code.Value, value => UniverseCode.Create(value))
            .HasMaxLength(UniverseCode.MaxLength)
            .IsRequired();

        builder.Property(universe => universe.Name)
            .HasColumnName("name")
            .HasMaxLength(Universe.MaxNameLength)
            .IsRequired();

        builder.Property(universe => universe.Kind)
            .HasColumnName("kind")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(universe => universe.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        // Optional by design, and the absence is the meaningful state: a
        // universe that claims nothing knows nothing, and every as-of read
        // against it reports that rather than returning an empty set. The
        // lower bound is not nullable, so the two columns cannot express a
        // half-made claim.
        builder.OwnsOne(universe => universe.Coverage, coverage =>
        {
            coverage.Property(span => span.From)
                .HasColumnName("coverage_from")
                .IsRequired();

            // Null means the claim runs on, not that its end is unknown.
            coverage.Property(span => span.Until)
                .HasColumnName("coverage_until");
        });

        builder.Navigation(universe => universe.Coverage).IsRequired(false);

        builder.Property(universe => universe.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(universe => universe.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // The code is how a dataset manifest and a research call name a
        // universe, so two universes sharing one would make a manifest
        // ambiguous about which set a result was computed over.
        builder.HasIndex(universe => universe.Code)
            .IsUnique()
            .HasDatabaseName("ux_universes_code");
    }
}

/// <summary>
/// Maps <see cref="UniverseMembership"/> onto the <c>universe_memberships</c>
/// table.
/// </summary>
/// <remarks>
/// <para>
/// Append-only. The key is the universe, the security and the first date of
/// membership, so re-entry is a new row rather than an edit: a security demoted
/// in July and restored the following January keeps both spells, and the gap
/// between them stays visible to an as-of read.
/// </para>
/// <para>
/// Overlap is refused by an exclusion constraint added in the migration, which
/// EF cannot express. It is the constraint that makes the table's central claim
/// enforceable rather than merely intended: a security cannot belong to one
/// universe twice at the same time, however many import runs say otherwise.
/// </para>
/// </remarks>
internal sealed class UniverseMembershipConfiguration : IEntityTypeConfiguration<UniverseMembership>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UniverseMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "universe_memberships",
            table => table.HasCheckConstraint(
                "ck_universe_memberships_interval",
                // Half-open: [effective_from, effective_to). An interval that
                // ends where it starts covers no session, and a membership that
                // covered no session did not happen.
                "effective_to IS NULL OR effective_to > effective_from"));

        builder.HasKey(membership => new
        {
            membership.UniverseId,
            membership.InstrumentId,
            membership.EffectiveFrom,
        }).HasName("pk_universe_memberships");

        builder.Property(membership => membership.UniverseId)
            .HasColumnName("universe_id")
            .HasConversion(id => id.Value, value => new UniverseId(value))
            .IsRequired();

        builder.Property(membership => membership.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value))
            .IsRequired();

        // Effective time. Inclusive lower bound, exclusive upper bound, so a
        // review that removes one name and admits another puts the review date
        // on exactly one side of each.
        builder.Property(membership => membership.EffectiveFrom)
            .HasColumnName("effective_from")
            .IsRequired();

        // Null means still a member, not "end unknown".
        builder.Property(membership => membership.EffectiveTo)
            .HasColumnName("effective_to");

        // Recorded, and not read until U4. An index review is published before
        // it takes effect, and acting on it earlier than publication is
        // look-ahead of exactly the kind the announcement date exists to catch.
        builder.Property(membership => membership.AnnouncedOn)
            .HasColumnName("announced_on");

        builder.Property(membership => membership.Source)
            .HasColumnName("source")
            .HasConversion(source => source.Value, value => SourceCode.Create(value))
            .HasMaxLength(SourceCode.MaxLength)
            .IsRequired();

        builder.Property(membership => membership.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .IsRequired();

        // Derived from the interval, and must not become a column that can
        // disagree with it.
        builder.Ignore(membership => membership.IsCurrent);

        builder.HasOne<Universe>()
            .WithMany()
            .HasForeignKey(membership => membership.UniverseId)
            .HasConstraintName("fk_universe_memberships_universe")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(membership => membership.InstrumentId)
            .HasConstraintName("fk_universe_memberships_instrument")
            // Same rule as bars and revisions: a delisted security keeps its
            // history, and its history includes what it used to belong to.
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the as-of read: one universe, then the intervals that straddle
        // a date. The primary key leads on the universe too, but orders by
        // security before date, which is the wrong way round for this question.
        builder.HasIndex(membership => new
        {
            membership.UniverseId,
            membership.EffectiveFrom,
            membership.EffectiveTo,
        }).HasDatabaseName("ix_universe_memberships_as_of");

        // Serves the mirror question — which universes did this security belong
        // to — which a coverage review asks for every instrument it checks.
        builder.HasIndex(membership => membership.InstrumentId)
            .HasDatabaseName("ix_universe_memberships_instrument");
    }
}
