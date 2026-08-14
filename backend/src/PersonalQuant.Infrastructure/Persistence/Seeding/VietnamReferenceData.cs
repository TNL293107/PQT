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
    /// A small, deterministic set of well-known Vietnamese securities.
    /// </summary>
    /// <remarks>
    /// Chosen to cover the asset types the model distinguishes and the venues
    /// it spans, not to be complete. Every ticker here is unique across the
    /// three venues, which reflects reality: tickers are assigned centrally in
    /// Vietnam, so the same symbol is not live on two exchanges at once.
    /// Search has to handle that case anyway, and the integration tests
    /// construct it rather than this file inventing it.
    /// </remarks>
    public static IReadOnlyList<InstrumentSeed> Instruments { get; } =
    [
        new("HOSE", "FPT", "FPT Corporation", AssetType.Equity),
        new("HOSE", "VNM", "Vietnam Dairy Products Joint Stock Company", AssetType.Equity),
        new("HOSE", "VIC", "Vingroup Joint Stock Company", AssetType.Equity),
        new("HOSE", "VHM", "Vinhomes Joint Stock Company", AssetType.Equity),
        new("HOSE", "VCB", "Joint Stock Commercial Bank for Foreign Trade of Vietnam", AssetType.Equity),
        new("HOSE", "TCB", "Vietnam Technological and Commercial Joint Stock Bank", AssetType.Equity),
        new("HOSE", "HPG", "Hoa Phat Group Joint Stock Company", AssetType.Equity),
        new("HOSE", "MSN", "Masan Group Corporation", AssetType.Equity),
        new("HOSE", "MWG", "Mobile World Investment Corporation", AssetType.Equity),
        new("HOSE", "SSI", "SSI Securities Corporation", AssetType.Equity),
        new("HOSE", "FUEVFVND", "DCVFM VNDIAMOND ETF", AssetType.Etf),
        new("HOSE", "VNINDEX", "VN-Index", AssetType.Index),
        new("HOSE", "VN30", "VN30 Index", AssetType.Index),
        new("HNX", "SHS", "Saigon - Hanoi Securities Joint Stock Company", AssetType.Equity),
        new("HNX", "PVS", "PetroVietnam Technical Services Corporation", AssetType.Equity),
        new("HNX", "IDC", "IDICO Corporation", AssetType.Equity),
        new("HNX", "HNXINDEX", "HNX-Index", AssetType.Index),
        new("UPCOM", "BSR", "Binh Son Refining and Petrochemical Joint Stock Company", AssetType.Equity),
        new("UPCOM", "ACV", "Airports Corporation of Vietnam", AssetType.Equity),
    ];

    /// <summary>A venue to create if it does not exist.</summary>
    /// <param name="Code">The operating code.</param>
    /// <param name="Name">The full venue name.</param>
    /// <param name="Mic">The ISO 10383 MIC, where it is unambiguous.</param>
    internal sealed record ExchangeSeed(string Code, string Name, string? Mic);

    /// <summary>A security to create if its ticker is free on its venue.</summary>
    /// <param name="ExchangeCode">The venue it lists on.</param>
    /// <param name="Ticker">The exchange ticker.</param>
    /// <param name="Name">The registered security name.</param>
    /// <param name="AssetType">The broad asset class.</param>
    internal sealed record InstrumentSeed(
        string ExchangeCode,
        string Ticker,
        string Name,
        AssetType AssetType);
}
