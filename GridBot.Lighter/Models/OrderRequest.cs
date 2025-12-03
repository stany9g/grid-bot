namespace GridBot.Lighter.Models;

/// <summary>
/// Request model for creating a market order with automatic slippage protection.
/// </summary>
public class MarketOrderRequest
{
    /// <summary>
    /// Market identifier (e.g., 0 for BTC-USDC).
    /// </summary>
    public required int MarketIndex { get; init; }

    /// <summary>
    /// Client-side unique order identifier.
    /// Must be unique across all active orders for this account.
    /// </summary>
    public required long ClientOrderIndex { get; init; }

    /// <summary>
    /// Order size in base asset units (scaled).
    /// </summary>
    public required long BaseAmount { get; init; }

    /// <summary>
    /// True for sell (ask), false for buy (bid).
    /// </summary>
    public required bool IsAsk { get; init; }

    /// <summary>
    /// Maximum slippage tolerance as a decimal (e.g., 0.01 = 1%).
    /// For buys: willing to pay up to this % above current best ask.
    /// For sells: willing to accept down to this % below current best bid.
    /// Default is 0.5% (0.005).
    /// </summary>
    public decimal MaxSlippage { get; init; } = 0.005m;

    /// <summary>
    /// If true, order will only reduce position size (cannot increase).
    /// </summary>
    public bool ReduceOnly { get; init; } = false;

    /// <summary>
    /// Validates the market order request parameters.
    /// </summary>
    /// <returns>Null if valid, otherwise an error message.</returns>
    public string? Validate()
    {
        if (MarketIndex < 0)
            return "MarketIndex must be non-negative";

        if (ClientOrderIndex <= 0)
            return "ClientOrderIndex must be positive";

        if (BaseAmount <= 0)
            return "BaseAmount must be positive";

        if (MaxSlippage < 0 || MaxSlippage > 0.5m)
            return "MaxSlippage must be between 0 and 0.5 (50%)";

        return null;
    }
}

/// <summary>
/// High-level request model for creating a single order with validation.
/// </summary>
public class CreateOrderRequest
{
    /// <summary>
    /// Market identifier (e.g., 0 for BTC-USDC).
    /// </summary>
    public required int MarketIndex { get; init; }

    /// <summary>
    /// Client-side unique order identifier.
    /// Must be unique across all active orders for this account.
    /// </summary>
    public required long ClientOrderIndex { get; init; }

    /// <summary>
    /// Order size in base asset units (scaled).
    /// For example, to trade 0.01 BTC, this might be 1000000 depending on scaling.
    /// </summary>
    public required long BaseAmount { get; init; }

    /// <summary>
    /// Order price (scaled according to market tick size).
    /// </summary>
    public required long Price { get; init; }

    /// <summary>
    /// True for sell (ask), false for buy (bid).
    /// </summary>
    public required bool IsAsk { get; init; }

    /// <summary>
    /// Order type (limit, market, stop-loss, etc.).
    /// </summary>
    public OrderType OrderType { get; init; } = OrderType.Limit;

    /// <summary>
    /// Time-in-force setting.
    /// </summary>
    public TimeInForce TimeInForce { get; init; } = TimeInForce.GoodTillTime;

    /// <summary>
    /// If true, order will only reduce position size (cannot increase).
    /// </summary>
    public bool ReduceOnly { get; init; } = false;

    /// <summary>
    /// Trigger price for stop-loss or take-profit orders.
    /// Use OrderConstants.NilTriggerPrice for non-conditional orders.
    /// </summary>
    public int TriggerPrice { get; init; } = OrderConstants.NilTriggerPrice;

    /// <summary>
    /// Order expiry timestamp (Unix seconds).
    /// Use OrderConstants.Default28DayOrderExpiry for default 28-day expiry.
    /// </summary>
    public long OrderExpiry { get; init; } = OrderConstants.Default28DayOrderExpiry;

    /// <summary>
    /// Validates the order request parameters.
    /// </summary>
    /// <returns>Null if valid, otherwise an error message.</returns>
    public string? Validate()
    {
        if (MarketIndex < 0)
            return "MarketIndex must be non-negative";

        if (ClientOrderIndex <= 0)
            return "ClientOrderIndex must be positive";

        if (BaseAmount <= 0)
            return "BaseAmount must be positive";

        if (Price <= 0 && OrderType != OrderType.Market)
            return "Price must be positive for non-market orders";

        if (TriggerPrice < 0)
            return "TriggerPrice must be non-negative";

        // Allow special values: -1 (28-day default) and 0 (IOC/Market orders)
        // Otherwise, expiry must be a future timestamp
        if (OrderExpiry != OrderConstants.Default28DayOrderExpiry &&
            OrderExpiry != OrderConstants.DefaultIocExpiry &&
            OrderExpiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return "OrderExpiry must be in the future";

        return null;
    }
}

/// <summary>
/// High-level request model for grouped orders (OCO, OTO, OTOCO).
/// </summary>
public class CreateGroupedOrdersRequest
{
    /// <summary>
    /// Type of order grouping (OTO, OCO, or OTOCO).
    /// </summary>
    public required GroupingType GroupingType { get; init; }

    /// <summary>
    /// List of orders to be grouped together.
    /// Must contain 2-3 orders depending on grouping type.
    /// </summary>
    public required List<CreateOrderRequest> Orders { get; init; }

    /// <summary>
    /// Validates the grouped order request.
    /// </summary>
    /// <returns>Null if valid, otherwise an error message.</returns>
    public string? Validate()
    {
        if (Orders == null || Orders.Count == 0)
            return "Orders list cannot be empty";

        if (GroupingType == GroupingType.OneCancelOther && Orders.Count != 2)
            return "OCO orders require exactly 2 orders";

        if (GroupingType == GroupingType.OneTriggerOther && Orders.Count != 2)
            return "OTO orders require exactly 2 orders";

        if (GroupingType == GroupingType.OneTriggerOneCancelOther && Orders.Count != 3)
            return "OTOCO orders require exactly 3 orders";

        for (int i = 0; i < Orders.Count; i++)
        {
            var error = Orders[i].Validate();
            if (error != null)
                return $"Order {i}: {error}";
        }

        return null;
    }
}

/// <summary>
/// High-level request model for modifying an existing order.
/// </summary>
public class ModifyOrderRequest
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketIndex { get; init; }

    /// <summary>
    /// ID of the order to modify.
    /// </summary>
    public required long OrderId { get; init; }

    /// <summary>
    /// New client order index (if changing).
    /// </summary>
    public required long NewClientOrderIndex { get; init; }

    /// <summary>
    /// New order size in base asset units.
    /// </summary>
    public required long NewBaseAmount { get; init; }

    /// <summary>
    /// New order price.
    /// </summary>
    public required long NewPrice { get; init; }

    /// <summary>
    /// Validates the modify order request.
    /// </summary>
    /// <returns>Null if valid, otherwise an error message.</returns>
    public string? Validate()
    {
        if (MarketIndex < 0)
            return "MarketIndex must be non-negative";

        if (OrderId <= 0)
            return "OrderId must be positive";

        if (NewClientOrderIndex <= 0)
            return "NewClientOrderIndex must be positive";

        if (NewBaseAmount <= 0)
            return "NewBaseAmount must be positive";

        if (NewPrice <= 0)
            return "NewPrice must be positive";

        return null;
    }
}

/// <summary>
/// High-level request model for updating position leverage.
/// </summary>
public class UpdateLeverageRequest
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required int MarketIndex { get; init; }

    /// <summary>
    /// Margin mode (cross or isolated).
    /// </summary>
    public required MarginMode MarginMode { get; init; }

    /// <summary>
    /// Leverage multiplier (e.g., 10 for 10x leverage).
    /// </summary>
    public required int Leverage { get; init; }

    /// <summary>
    /// Validates the leverage update request.
    /// </summary>
    /// <returns>Null if valid, otherwise an error message.</returns>
    public string? Validate()
    {
        if (MarketIndex < 0)
            return "MarketIndex must be non-negative";

        if (Leverage <= 0 || Leverage > 100)
            return "Leverage must be between 1 and 100";

        return null;
    }
}
