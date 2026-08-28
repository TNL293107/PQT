# Advanced Quant Research

**Status: none of this is implemented, and none of it is scheduled before
Phase 8.** This document exists to place five capabilities, record what each one
actually requires, and state honestly which of them Vietnamese market data can
support today.

The Research Foundation Upgrade does not build any of them. It establishes their
architectural prerequisites — chiefly announcement time (U1), recorded
universes (U2), the canonical dataset (U5) and the experiment log (U9).

Roadmap: [`../roadmap/pqt-roadmap-v2.md`](../roadmap/pqt-roadmap-v2.md).

---

## Ownership matrix

Every capability has **exactly one** canonical owner. No additional phases are
created for them, and no ownership is duplicated across Phases 7, 8, 11 or 12.

| Capability | Canonical owner | Status | Prerequisites |
| --- | --- | --- | --- |
| Prediction Market Mispricing | **Phase 12** | `RESEARCH ONLY` | Calibration and expected-value substrate (Phase 7); a legally usable, sufficiently liquid market |
| Information Diffusion | **Phase 12** | `PLANNED` | U1 announcement time · Phase 8 event-study framework · Phase 11 news and event corpus · entity-to-instrument mapping |
| Implied Risk-Neutral Distribution (Breeden–Litzenberger) | **Phase 12** | `BLOCKED` | Options instrument and quote/surface model; adequate options, strike and quote data |
| Backtest Overfitting Detection — **core** | **Phase 8** | `PLANNED` | U1 · U2 · U9 `trial_count` · Phase 7 |
| Advanced multiple-testing (Reality Check, SPA, large-scale CSCV) | **Phase 12** | `PLANNED` | Phase 8 plus a genuine strategy library |

---

## 1. Prediction Market Mispricing — `RESEARCH ONLY`

**Owner: Phase 12.**

The intended comparison is sound:

```
market-implied probability   vs   model-estimated probability
```

```
Prediction market → contract normalisation → implied probability
                  → fair probability → calibration → mispricing
                  → expected value → signal
```

Accounting for fees, spread, liquidity, resolution rules, time to resolution,
contract semantics, calibration quality and correlation between contracts.

**The obstacle is data, not method.** There is no legal, sufficiently liquid
prediction market on Vietnamese equities. The venues that exist are US-centric
and jurisdictionally inaccessible from this project's market.

**The architecture must not be built around any prediction-market vendor.**
Structuring Phase 12 around a specific venue would produce a module that cannot
run and cannot be repurposed.

**What is actually reusable** is the substrate rather than the feed: a
probability calibration, fair-value and expected-value framework that takes an
implied probability from *somewhere* and prices the disagreement net of costs.
That substrate belongs in Phase 7, applies immediately to instruments that do
exist in Vietnam — covered-warrant implied moves and VN30F basis — and can
consume prediction-market data later as one optional source among several.

The capability stays `RESEARCH ONLY` until a legally usable and sufficiently
liquid market exists.

---

## 2. Information Diffusion — `PLANNED`

**Owner: Phase 12.**

How information propagates through a market, and how fast.

```
Information event → source → announcement timestamp
                  → entity mapping → instrument mapping
                  → event classification
                  → market reaction (price · volume · volatility)
                  → sector propagation → cross-asset propagation
                  → lead / lag → diffusion speed and half-life
                  → signal
```

Research questions it can answer: price, volume and volatility reaction; sector
and cross-asset propagation; lead-lag structure; where price discovery happens;
information half-life; and whether source quality predicts reaction size.

**The Upgrade genuinely unblocks this.** Announcement time is the one
prerequisite that cannot be retrofitted — reconstructing when the market learned
something, after the fact, from data that never recorded it, is not possible.
U1 makes it a first-class, non-collapsible concept, and U4 proves it is used
rather than merely stored.

The rest is downstream work: Phase 11 supplies the corpus and the
entity-to-instrument mapping, Phase 8 supplies event-study returns that are
point-in-time correct.

---

## 3. Implied Risk-Neutral Distribution — `BLOCKED`

**Owner: Phase 12.**

Breeden–Litzenberger recovers the risk-neutral density from the second
derivative of call prices with respect to strike:

```
Option prices → arbitrage-free surface → smoothing / interpolation
              → ∂²C(K,T) / ∂K²  →  risk-neutral PDF

f(K) = exp(rT) · ∂²C(K,T) / ∂K²
```

Outputs would be the risk-neutral density, implied mean and variance, skew,
kurtosis and tail probabilities.

### Why it is blocked

**Vietnam has no listed equity options.** What exists is issuer-specific covered
warrants — few strikes, embedded issuer credit risk, wide spreads, inconsistent
coverage — and VN30F index futures. A defensible strike ladder cannot be
assembled from that.

The method is also unforgiving of thin data. Naive numerical differentiation of
noisy quoted prices produces a confident-looking density that means nothing: the
second derivative amplifies bid-ask noise, sparse strikes make interpolation
dominate the answer, and violated arbitrage bounds can produce negative
"probabilities". Any real implementation needs an arbitrage-free fit — SVI or a
constrained spline — before differentiating, plus correct handling of dividends,
discounting and expiry.

### Conditions to move it off `BLOCKED`

1. Evidence that adequate options, strike and quote data actually exists.
2. An options and derivatives instrument model.
3. A quote and surface data model.
4. An arbitrage-free fitting step.

### The interim proxy, if one is built

A covered-warrant implied-move indicator is legitimate interim research. If it
is built it **must be explicitly labelled a proxy** — in its name, in its API
and in anything it renders — and must never be presented as a
Breeden–Litzenberger risk-neutral distribution. Publishing a pseudo-RND derived
from insufficient data would be exactly the kind of confident wrongness the rest
of this architecture is built to prevent.

---

## 4. Backtest Overfitting Detection

The most valuable of the five, and the one closest to being immediately useful.
It is deliberately **split across two phases** rather than kept whole in an
advanced-methods bucket.

```
Strategy → Backtest → Statistical validation → Robustness testing
         → Overfitting detection → Out-of-sample evaluation
         → Research confidence
```

### Core — `PLANNED`, owner **Phase 8**

Part of Phase 8's definition of done, because a backtest without an overfitting
check is not a finished backtest.

| Method | Why it earns its place |
| --- | --- |
| Walk-forward | Cheap, and the closest thing to how a strategy would actually have been run |
| Out-of-sample evaluation | The baseline claim any result must survive |
| Parameter perturbation | A result that dies when a lookback moves by one day was noise |
| Universe perturbation | Depends on U2; a result that requires exactly today's VN30 is a survivorship artefact |
| Transaction-cost and slippage perturbation | Vietnamese costs and lot sizes are large enough to erase a marginal edge |
| Regime analysis | A strategy that only worked in 2021 should say so |
| **Deflated Sharpe Ratio** | Directly prices the number of attempts. Requires U9's `trial_count` |
| **PBO via CSCV** | Estimates the probability the selected configuration is overfit. Also requires `trial_count` |

The last two are why **Phase 8 cannot complete before Gate B**: they take an
input that only the experiment log produces.

### Advanced — `PLANNED`, owner **Phase 12**

White's Reality Check, Hansen's SPA, and large-scale permutation and Monte-Carlo
suites. These require a bootstrap across a full strategy universe, and are
premature before a genuine strategy library exists. Running them over three
strategies would produce a number with the appearance of rigour and none of the
content.

### The methods deliberately not adopted yet

Not every published method is worth implementing. The selection above favours
the ones that (a) answer a question this project will actually face — one
researcher, many configurations, one market — and (b) can be validated against a
synthetic case with a known answer. Anything that cannot be tested against a
known-overfit control does not go in.

---

## What this document does not claim

- None of these five capabilities is designed in detail. Prerequisites are placed; designs are not written.
- No claim is made that Vietnamese options or prediction-market data will become available.
- The `BLOCKED` status on the risk-neutral distribution is a statement about data availability as understood at the time of writing, and should be re-checked rather than assumed permanent.
