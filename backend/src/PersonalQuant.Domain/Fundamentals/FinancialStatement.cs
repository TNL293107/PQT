namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// Which statement of a report a <see cref="StatementLine"/> belongs to.
/// </summary>
public enum FinancialStatement
{
    /// <summary>Not specified. Never valid on a stored line.</summary>
    Unspecified = 0,

    /// <summary>The balance sheet (<em>bảng cân đối kế toán</em>). Its lines are balances at an instant.</summary>
    BalanceSheet = 1,

    /// <summary>The income statement (<em>kết quả kinh doanh</em>). Its lines are flows over a span.</summary>
    IncomeStatement = 2,

    /// <summary>The cash-flow statement, direct method. Flows over a span.</summary>
    CashFlowDirect = 3,

    /// <summary>The cash-flow statement, indirect method. Flows over a span.</summary>
    CashFlowIndirect = 4,
}
