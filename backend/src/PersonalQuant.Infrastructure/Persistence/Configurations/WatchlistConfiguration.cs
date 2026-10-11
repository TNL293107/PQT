using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Watchlist"/> onto <c>terminal.watchlists</c>.</summary>
/// <remarks>
/// The <c>terminal</c> schema holds what the operator creates, apart from the
/// market facts in <c>quant</c> and the research output in <c>research</c>
/// (ADR-027). <c>quant_research</c> is granted nothing here, and no default
/// privilege reaches the schema, so a table added later stays out of the
/// research boundary without anyone having to remember to exclude it.
/// </remarks>
internal sealed class WatchlistConfiguration : IEntityTypeConfiguration<Watchlist>
{
    /// <summary>The schema operator state lives in.</summary>
    public const string Schema = "terminal";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Watchlist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("watchlists", Schema, table => table.HasCheckConstraint(
            "ck_watchlists_name",
            "name ~ '^[a-z0-9][a-z0-9_-]*$'"));

        builder.HasKey(watchlist => watchlist.Id).HasName("pk_watchlists");

        builder.Property(watchlist => watchlist.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new WatchlistId(value))
            .ValueGeneratedNever();

        builder.Property(watchlist => watchlist.Name)
            .HasColumnName("name")
            .HasMaxLength(Watchlist.MaxNameLength)
            .IsRequired();

        builder.Property(watchlist => watchlist.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(watchlist => watchlist.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(watchlist => watchlist.Name)
            .IsUnique()
            .HasDatabaseName("ux_watchlists_name");

        builder.HasMany(watchlist => watchlist.Entries)
            .WithOne()
            .HasForeignKey(entry => entry.WatchlistId)
            .HasConstraintName("fk_watchlist_entries_watchlists")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(watchlist => watchlist.Entries).HasField("_entries");
    }
}

/// <summary>Maps <see cref="WatchlistEntry"/> onto <c>terminal.watchlist_entries</c>.</summary>
internal sealed class WatchlistEntryConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("watchlist_entries", WatchlistConfiguration.Schema, table =>
            table.HasCheckConstraint("ck_watchlist_entries_position", "position >= 0"));

        // One row per instrument per list: the domain treats a second add as
        // already done, and the key makes that true under a race as well.
        builder.HasKey(entry => new { entry.WatchlistId, entry.InstrumentId })
            .HasName("pk_watchlist_entries");

        builder.Property(entry => entry.WatchlistId)
            .HasColumnName("watchlist_id")
            .HasConversion(id => id.Value, value => new WatchlistId(value));

        builder.Property(entry => entry.InstrumentId)
            .HasColumnName("instrument_id")
            .HasConversion(id => id.Value, value => new InstrumentId(value));

        builder.Property(entry => entry.Position)
            .HasColumnName("position")
            .IsRequired();

        builder.Property(entry => entry.AddedAtUtc)
            .HasColumnName("added_at_utc")
            .IsRequired();

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(entry => entry.InstrumentId)
            .HasConstraintName("fk_watchlist_entries_instrument")
            // Instruments are never deleted — a delisted security keeps its
            // history — so this only stops a list pointing at nothing.
            .OnDelete(DeleteBehavior.Restrict);

        // Serves the foreign key's check when an instrument row changes, and
        // "which lists hold this instrument".
        builder.HasIndex(entry => entry.InstrumentId)
            .HasDatabaseName("ix_watchlist_entries_instrument_id");
    }
}

/// <summary>Maps <see cref="Workspace"/> onto <c>terminal.workspaces</c>.</summary>
internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("workspaces", WatchlistConfiguration.Schema, table =>
        {
            table.HasCheckConstraint("ck_workspaces_name", "name ~ '^[a-z0-9][a-z0-9_-]*$'");
            table.HasCheckConstraint(
                "ck_workspaces_entries",
                $"cardinality(entries) BETWEEN 1 AND {Workspace.MaxEntries}");
        });

        builder.HasKey(workspace => workspace.Id).HasName("pk_workspaces");

        builder.Property(workspace => workspace.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new WorkspaceId(value))
            .ValueGeneratedNever();

        builder.Property(workspace => workspace.Name)
            .HasColumnName("name")
            .HasMaxLength(Watchlist.MaxNameLength)
            .IsRequired();

        // A text array: the lines are one value read and written whole, in
        // order, and never queried one by one.
        builder.Property<string[]>("_entries")
            .HasColumnName("entries")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Ignore(workspace => workspace.Entries);

        builder.Property(workspace => workspace.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(workspace => workspace.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex(workspace => workspace.Name)
            .IsUnique()
            .HasDatabaseName("ux_workspaces_name");
    }
}
