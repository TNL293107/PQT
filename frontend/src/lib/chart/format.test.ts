import { describe, expect, it } from "vitest";
import {
  formatChange,
  formatCompactVolume,
  formatFactor,
  formatPercent,
  formatPrice,
  formatVolume,
  stepDecimals,
} from "./format";

describe("formatPrice", () => {
  it("writes whole đồng with thousands separators, never rescaled to thousands", () => {
    expect(formatPrice(123400)).toBe("123,400");
  });

  it("keeps up to two decimals of an adjusted price and pads none onto a whole one", () => {
    expect(formatPrice(37583.3333)).toBe("37,583.33");
    expect(formatPrice(37500)).toBe("37,500");
  });

  it("uses exactly the precision an axis asks for", () => {
    expect(formatPrice(12.5, 2)).toBe("12.50");
    expect(formatPrice(124000, 0)).toBe("124,000");
  });

  it("writes a dash rather than NaN", () => {
    expect(formatPrice(Number.NaN)).toBe("—");
  });
});

describe("volume", () => {
  it("is written in full for the readout", () => {
    expect(formatVolume(1234567)).toBe("1,234,567");
    expect(formatVolume(0)).toBe("0");
  });

  it("is written compactly for an axis label", () => {
    expect(formatCompactVolume(12_345_600)).toBe("12.3M");
    expect(formatCompactVolume(0)).toBe("0");
  });
});

describe("formatChange", () => {
  it("always writes the sign, so a move never reads as a level", () => {
    expect(formatChange(900, 2)).toBe("+900 (+2.00%)");
    expect(formatChange(-450.5, -1.234)).toBe("-450.5 (-1.23%)");
  });

  it("marks an unchanged close as neither up nor down", () => {
    expect(formatChange(0, 0)).toBe("±0 (0.00%)");
  });

  it("omits the percentage when the base was zero", () => {
    expect(formatChange(100, null)).toBe("+100");
  });
});

describe("formatPercent", () => {
  it("does not claim a direction for a move that rounds to zero", () => {
    expect(formatPercent(-0.001)).toBe("0.00%");
  });
});

describe("stepDecimals", () => {
  it("gives whole-number steps no decimals", () => {
    expect(stepDecimals(500)).toBe(0);
    expect(stepDecimals(1)).toBe(0);
  });

  it("gives fractional steps just enough", () => {
    expect(stepDecimals(0.5)).toBe(1);
    expect(stepDecimals(0.1)).toBe(1);
    expect(stepDecimals(0.05)).toBe(2);
  });

  it("treats a step that is not positive as whole", () => {
    expect(stepDecimals(0)).toBe(0);
    expect(stepDecimals(Number.NaN)).toBe(0);
  });
});

describe("formatFactor", () => {
  it("keeps four decimals, enough to tell entitlements apart", () => {
    expect(formatFactor(1 / 1.2)).toBe("×0.8333");
    expect(formatFactor(1)).toBe("×1");
  });
});
