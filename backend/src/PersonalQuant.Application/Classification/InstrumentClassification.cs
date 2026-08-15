using PersonalQuant.Domain.Classification;

namespace PersonalQuant.Application.Classification;

/// <summary>
/// Where an instrument sits in the taxonomy, flattened to both levels.
/// </summary>
/// <remarks>
/// Both levels travel together because a caller that has the industry almost
/// always wants the sector too, and fetching it separately would mean a second
/// round trip for a value the first join already had in hand.
/// </remarks>
/// <param name="SectorCode">The sector's taxonomy code.</param>
/// <param name="SectorName">The sector's display name.</param>
/// <param name="IndustryCode">The industry's taxonomy code.</param>
/// <param name="IndustryName">The industry's display name.</param>
public sealed record InstrumentClassification(
    ClassificationCode SectorCode,
    string SectorName,
    ClassificationCode IndustryCode,
    string IndustryName);
