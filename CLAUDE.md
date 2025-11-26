# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Project Overview: Adaptive Liquidity & Trend Engine (ALTE)
Internal Codename: Nexus
1. The Vision
We are building an autonomous trading system that bridges the gap between High-Frequency Market Making and Long-Term Trend Following.
Our goal is to create a "Smart Agent" that operates continuously within the crypto markets. Unlike static grid bots that degrade in trending markets, the ALTE system is designed to adapt its behavior in real-time, aiming to capture the high yield of volatility (scalping) while preserving the upside potential of a bull run (holding).
2. Core Capabilities
We aim to have a system that possesses four distinct "intelligences" working in unison:
A. Dynamic Grid Geometry (The Market Maker)
The system will not use fixed price levels. Instead, it will function as an elastic market maker.
Volatility Adaptation: The bot will constantly read the ATR (Average True Range). If the market is quiet, it tightens the grid to capture small profits (0.2% moves). If the market is volatile, it expands the grid to capture large swings (2.0% moves) and reduce fee drag.
Order Book Awareness: The bot will analyze the order book depth to place limit orders at high-probability liquidity clusters (support/resistance zones) rather than arbitrary math-based intervals.
B. Smart Inventory Management (The Trend Follower)
This is the system's primary differentiator. It dynamically alters the ratio of Base Asset (Crypto) vs. Quote Asset (USDT/USD) held in the portfolio based on the macro trend.
Bull Trend Behavior: When a strong uptrend is detected (via Moving Averages/MACD), the bot shifts to an 80/20 Skew. It slows down selling and "trails" the buy orders upward, ensuring we remain heavily invested to capture asset appreciation.
Bear Trend Behavior: When a downtrend is confirmed, the bot shifts to a 20/80 Skew. It sells bounces aggressively to accumulate cash and widens buy orders significantly to avoid "catching a falling knife."
C. The "Infinite Upside" Module
To solve the problem of selling too early (Impermanent Loss), the system will feature a "Trailing Grid" mechanism.
Logic: As the price breaches the top of our grid, the entire grid structure moves up. The bot is forbidden from selling the last 10-20% of the position, ensuring we always have a "Moon Bag" if the asset goes parabolic.
D. Sentinel Risk Protection
A hard-coded safety layer that overrides all other logic to protect capital.
Flash Crash Pausing: If the price drops >X% in Y minutes, buying is suspended immediately to allow the market to find a bottom.
Liquidity Check: The bot will assess volume. If volume dries up (signaling a potential trap or dead coin), the bot widens spreads to minimize risk.
3. The Logic Engine (How it Thinks)
We aim to build a decision loop that runs every few seconds:
Analyze State: What is the current trend? What is the current volatility?
Check Inventory: Do we have too much coin (risk) or too much cash (opportunity cost) based on the current Trend State?
Optimize Orders:
If Inventory is optimal: Adjust grid lines to match current volatility.
If Inventory is wrong: Execute rebalancing trades (e.g., if the trend flips bearish, sell 30% of the stack immediately to reach the new safety target).
Execute: Place/Cancel limit orders via API.
4. Target Outcome (The "Definition of Done")
We will know the project is successful when we have a functioning bot that:
Outperforms "Buy & Hold" in sideways and bear markets by accumulating more units of the asset.
Matches (or closely trails) "Buy & Hold" in bull markets by refusing to sell its entire stack early.
Requires minimal human intervention, automatically adjusting its own parameters (grid width, inventory skew, risk tolerance) as the market cycles between calm, pump, and dump.
5. Summary
In short, we are not building a bot that just "buys low and sells high" within a box. We are building a smart asset manager that knows when to act like a Scalper (harvesting profit) and when to act like an Investor (holding for growth).

## Technical stack
- .NET 10
- Aspire 13
- ASP.NET Core
- Blazor
- Lighter DEX (decentralized exchange)

## Development Principles

**KISS - Keep It Simple Stupid**

- Avoid complexity; use the simplest solution that works
- Minimal abstractions; only create them when they provide clear value, but always use Interfaces for the Services
- Direct solutions over clever ones
- If something feels complex, simplify it

**Code Style**

- Never use `#region` directives - they obscure code structure
- If a class needs regions, it's too large - split it

## Architecture

### Project Structure

```
GridBot/
├── GridBot.AppHost/           # Aspire orchestration (entry point)
├── GridBot.ApiService/        # Backend API - Lighter trading endpoints
├── GridBot.Lighter/           # Lighter DEX client library (P/Invoke + REST)
├── GridBot.Web/               # Blazor Server frontend
└── GridBot.ServiceDefaults/   # Shared Aspire defaults (telemetry, health)
```

### Key Components

**GridBot.Lighter** - Core trading library with two layers:
- `SignerClient` - P/Invoke wrapper for native signing library (signer-amd64.dll/so)
- `LighterQueryClient` / `LighterCommandClient` - REST API clients for Lighter DEX
- `ILighterQueryClient` - Read operations (account, orders, markets)
- `ILighterCommandClient` - Write operations (create/cancel/modify orders)

**GridBot.ApiService** - Exposes Lighter operations via REST:
- `/api/lighter/orders` - Order management (create, modify, cancel)
- `/api/lighter/account/{id}` - Account info and active orders
- `/api/lighter/markets` - Market data and order books
- `/api/lighter/leverage` - Position leverage management
- `/api/lighter/nonce` - Nonce synchronization

### Service Registration

Services are registered via dependency injection in Program.cs:
```csharp
builder.Services.AddLighterClient(builder.Configuration);
```

Configuration in appsettings.json under `"Lighter"` section (use user secrets for PrivateKey).

### Aspire Orchestration

AppHost startup order: Redis → ApiService → Web

Service discovery uses `https+http://apiservice` scheme.

## Development Commands

### Run Application
```bash
dotnet run --project GridBot.AppHost
```
Starts: Aspire Dashboard, Redis, ApiService, Web frontend

### Build
```bash
dotnet build GridBot.slnx
```

### User Secrets (for Lighter private key)
```bash
dotnet user-secrets set "Lighter:PrivateKey" "your-key" --project GridBot.AppHost
```

## Native Library Requirements

GridBot.Lighter requires platform-specific native signing libraries:
- Windows: `signer-amd64.dll`
- Linux: `signer-amd64.so`

Located in `GridBot.Lighter/Native/`, automatically copied to output during build.

## Key Interfaces

When implementing trading features, inject these interfaces:
- `ILighterQueryClient` - For reading data (accounts, orders, markets)
- `ILighterCommandClient` - For trading operations (requires signing)

Both are registered as singletons via `AddLighterClient()`.

## Important Constants

```csharp
ChainId.Mainnet = 304
ChainId.Testnet = 300
OrderConstants.UsdcTickerScale = 1_000_000
OrderConstants.Default28DayOrderExpiry = -1
```

## Health Endpoints

- `/health` - Readiness (all checks)
- `/alive` - Liveness (tagged "live" checks)

Only exposed in Development environment.

# Rules
We want to leverage subagents as much as possible
Workflow should be to
1. Translate User requirements $ARGUMENT of prompt into the tasks and enriching it with the `"trading-risk-manager"`
2. If we need something specific with the Ligther DEX we leverage `"lighter-api-specialist"`
3. Create implementation plan
4. Pass the plan to the `"dotnet-feature-builder"`
5. Review the code with `"csharp-code-reviewer"` all the critical findings should be immediately worked on by the `"dotnet-feature-builder"` again and basically reiterate 4. and 5. until no critical findings
6. Passing the changes as well overall code the `"trading-bot-auditor"` which should evaluate correctness of the code from the view of crypto trading and evalute system as a whole
7. Phase done, update all .md doc files


