using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Infrastructure.Persistence.Seeding;

/// <summary>
/// The venues and securities the seeder can create.
/// </summary>
/// <remarks>
/// <para>
/// Public reference data — a venue's name and time zone, a listed company's
/// ticker and registered name. It is not market data: there is no price, no
/// volume and no financial figure anywhere in this file, and nothing here came
/// from a licensed feed.
/// </para>
/// <para>
/// The instruments are recorded as trading with no first trading date. Their
/// real listing dates are public but are not reproduced here, because an
/// unsourced date typed from memory into the system of record is exactly the
/// kind of quiet error the instrument master exists to prevent. The Phase 2
/// provider import fills them in from a source that can be cited.
/// </para>
/// </remarks>
internal static class VietnamReferenceData
{
    /// <summary>The IANA zone every Vietnamese venue trades in.</summary>
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    /// <summary>The three venues that make up the Vietnamese market.</summary>
    /// <remarks>
    /// A MIC is given only where it is unambiguous. UPCOM is operated by HNX
    /// and its own code is not consistently reported, so it is left unset
    /// rather than guessed — the field is optional and never identity.
    /// </remarks>
    public static IReadOnlyList<ExchangeSeed> Exchanges { get; } =
    [
        new("HOSE", "Ho Chi Minh City Stock Exchange", "XSTC"),
        new("HNX", "Hanoi Stock Exchange", "XHNX"),
        new("UPCOM", "Unlisted Public Company Market", null),
    ];

    /// <summary>
    /// The upper level of the classification taxonomy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eleven groupings of the published industry-classification standard
    /// Vietnamese market data follows. Seeded whole rather than only where an
    /// industry exists beneath it, because the upper level is the part a
    /// provider mapping arrives keyed on — a mapping naming a sector this
    /// system has never heard of is a genuine gap, and one naming an empty
    /// sector is merely a sector nothing is listed under yet.
    /// </para>
    /// <para>
    /// Health care, telecommunications and utilities have no industry beneath
    /// them here. That is the seed set being small, not the taxonomy being
    /// incomplete: nothing in the starter instrument list falls under them.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SectorSeed> Sectors { get; } =
    [
        new("ENERGY", "Energy"),
        new("MATERIALS", "Basic Materials"),
        new("INDUSTRIALS", "Industrials"),
        new("CONSDISC", "Consumer Discretionary"),
        new("CONSSTAP", "Consumer Staples"),
        new("HEALTH", "Health Care"),
        new("FIN", "Financials"),
        new("REALEST", "Real Estate"),
        new("TECH", "Technology"),
        new("TELECOM", "Telecommunications"),
        new("UTIL", "Utilities"),
    ];

    /// <summary>
    /// The lower level of the classification taxonomy.
    /// </summary>
    /// <remarks>
    /// Only the nodes the starter instrument set needs. The standard defines
    /// far more, and inventing the rest here would put a taxonomy nothing has
    /// been mapped against into the system of record.
    /// </remarks>
    public static IReadOnlyList<IndustrySeed> Industries { get; } =
    [
        new("ENERGY", "ENERGY-OILGAS", "Oil, Gas and Coal"),
        new("MATERIALS", "MATERIALS-METAL", "Industrial Metals and Mining"),
        new("INDUSTRIALS", "INDUSTRIALS-TRANSPORT", "Industrial Transportation"),
        new("CONSDISC", "CONSDISC-RETAIL", "Retailers"),
        new("CONSSTAP", "CONSSTAP-FOOD", "Food Producers"),
        new("FIN", "FIN-BANK", "Banks"),
        new("FIN", "FIN-SECURITIES", "Investment Banking and Brokerage"),
        new("REALEST", "REALEST-DEV", "Real Estate Development"),
        new("TECH", "TECH-SOFT", "Software and Computer Services"),
    ];

    /// <summary>
    /// A small, deterministic set of well-known Vietnamese securities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chosen to cover the asset types the model distinguishes and the venues
    /// it spans, not to be complete. Every ticker here is unique across the
    /// three venues, which reflects reality: tickers are assigned centrally in
    /// Vietnam, so the same symbol is not live on two exchanges at once.
    /// Search has to handle that case anyway, and the integration tests
    /// construct it rather than this file inventing it.
    /// </para>
    /// <para>
    /// An industry is given only where the classification is not in dispute.
    /// The indices and the ETF are left unclassified because they are not in
    /// an industry at all, and IDICO because it is a genuine conglomerate
    /// whose dominant activity depends on which provider is asked. Guessing at
    /// either would put a peer group into the system of record that no source
    /// stands behind.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<InstrumentSeed> Instruments { get; } =
    [
        new("HOSE", "FPT", "FPT Corporation", AssetType.Equity, "TECH-SOFT"),
        new("HOSE", "VNM", "Vietnam Dairy Products Joint Stock Company", AssetType.Equity, "CONSSTAP-FOOD"),
        new("HOSE", "VIC", "Vingroup Joint Stock Company", AssetType.Equity, "REALEST-DEV"),
        new("HOSE", "VHM", "Vinhomes Joint Stock Company", AssetType.Equity, "REALEST-DEV"),
        new("HOSE", "VCB", "Joint Stock Commercial Bank for Foreign Trade of Vietnam", AssetType.Equity, "FIN-BANK"),
        new("HOSE", "TCB", "Vietnam Technological and Commercial Joint Stock Bank", AssetType.Equity, "FIN-BANK"),
        new("HOSE", "HPG", "Hoa Phat Group Joint Stock Company", AssetType.Equity, "MATERIALS-METAL"),
        new("HOSE", "MSN", "Masan Group Corporation", AssetType.Equity, "CONSSTAP-FOOD"),
        new("HOSE", "MWG", "Mobile World Investment Corporation", AssetType.Equity, "CONSDISC-RETAIL"),
        new("HOSE", "SSI", "SSI Securities Corporation", AssetType.Equity, "FIN-SECURITIES"),
        new("HOSE", "FUEVFVND", "DCVFM VNDIAMOND ETF", AssetType.Etf, null),
        new("HOSE", "VNINDEX", "VN-Index", AssetType.Index, null),
        new("HOSE", "VN30", "VN30 Index", AssetType.Index, null),
        new("HNX", "SHS", "Saigon - Hanoi Securities Joint Stock Company", AssetType.Equity, "FIN-SECURITIES"),
        new("HNX", "PVS", "PetroVietnam Technical Services Corporation", AssetType.Equity, "ENERGY-OILGAS"),
        new("HNX", "IDC", "IDICO Corporation", AssetType.Equity, null),
        new("HNX", "HNXINDEX", "HNX-Index", AssetType.Index, null),
        new("UPCOM", "BSR", "Binh Son Refining and Petrochemical Joint Stock Company", AssetType.Equity, "ENERGY-OILGAS"),
        new("UPCOM", "ACV", "Airports Corporation of Vietnam", AssetType.Equity, "INDUSTRIALS-TRANSPORT"),
    ];

    /// <summary>A venue to create if it does not exist.</summary>
    /// <param name="Code">The operating code.</param>
    /// <param name="Name">The full venue name.</param>
    /// <param name="Mic">The ISO 10383 MIC, where it is unambiguous.</param>
    internal sealed record ExchangeSeed(string Code, string Name, string? Mic);

    /// <summary>A sector to create if its code is unknown.</summary>
    /// <param name="Code">The taxonomy code.</param>
    /// <param name="Name">The display name.</param>
    internal sealed record SectorSeed(string Code, string Name);

    /// <summary>An industry to create if its code is unknown.</summary>
    /// <param name="SectorCode">The sector it belongs to.</param>
    /// <param name="Code">The taxonomy code.</param>
    /// <param name="Name">The display name.</param>
    internal sealed record IndustrySeed(string SectorCode, string Code, string Name);

    /// <summary>A security to create if its ticker is free on its venue.</summary>
    /// <param name="ExchangeCode">The venue it lists on.</param>
    /// <param name="Ticker">The exchange ticker.</param>
    /// <param name="Name">The registered security name.</param>
    /// <param name="AssetType">The broad asset class.</param>
    /// <param name="IndustryCode">
    /// The industry to classify it under, or <see langword="null"/> when no
    /// classification is defensible.
    /// </param>
    internal sealed record InstrumentSeed(
        string ExchangeCode,
        string Ticker,
        string Name,
        AssetType AssetType,
        string? IndustryCode);
}
