import { describe, expect, it } from "vitest";
import { bandCenter, indexAt, linearScale, PAGE_STEP, slotWidth, stepIndex, type Band } from "./scale";

describe("linearScale", () => {
  it("maps the domain onto the range, inverted for a y-axis", () => {
    const y = linearScale([100, 200], [180, 10]);

    expect(y(100)).toBe(180);
    expect(y(200)).toBe(10);
    expect(y(150)).toBe(95);
  });

  it("maps a zero-width domain to the middle of the range instead of dividing by zero", () => {
    const y = linearScale([5, 5], [100, 0]);

    expect(y(5)).toBe(50);
    expect(y(999)).toBe(50);
  });
});

describe("bands", () => {
  const band: Band = { left: 10, width: 100, count: 4 };

  it("gives each session an equal slot and centres it", () => {
    expect(slotWidth(band)).toBe(25);
    expect(bandCenter(band, 0)).toBe(22.5);
    expect(bandCenter(band, 3)).toBe(97.5);
  });

  it("gives a single session the whole width", () => {
    const one: Band = { left: 0, width: 300, count: 1 };

    expect(bandCenter(one, 0)).toBe(150);
    expect(indexAt(one, 12)).toBe(0);
  });
});

describe("indexAt — hit-testing", () => {
  const band: Band = { left: 10, width: 100, count: 4 };

  it("finds the session whose slot contains the pointer", () => {
    expect(indexAt(band, 10)).toBe(0);
    expect(indexAt(band, 34.9)).toBe(0);
    expect(indexAt(band, 35)).toBe(1);
    expect(indexAt(band, 109)).toBe(3);
  });

  it("clamps a pointer just outside the plot to the nearest session", () => {
    expect(indexAt(band, -40)).toBe(0);
    expect(indexAt(band, 500)).toBe(3);
  });

  it("finds nothing in an empty series", () => {
    expect(indexAt({ left: 0, width: 100, count: 0 }, 50)).toBeNull();
  });
});

describe("stepIndex — the keyboard model", () => {
  it("steps one session at a time and stops at either end", () => {
    expect(stepIndex(5, "ArrowLeft", 10)).toBe(4);
    expect(stepIndex(5, "ArrowRight", 10)).toBe(6);
    expect(stepIndex(0, "ArrowLeft", 10)).toBe(0);
    expect(stepIndex(9, "ArrowRight", 10)).toBe(9);
  });

  it("starts from the last session, which the readout already shows at rest", () => {
    expect(stepIndex(null, "ArrowLeft", 10)).toBe(8);
    expect(stepIndex(null, "ArrowRight", 10)).toBe(9);
  });

  it("jumps to either end", () => {
    expect(stepIndex(5, "Home", 10)).toBe(0);
    expect(stepIndex(5, "End", 10)).toBe(9);
  });

  it("pages by about two trading weeks, clamped", () => {
    expect(stepIndex(25, "PageUp", 40)).toBe(25 - PAGE_STEP);
    expect(stepIndex(3, "PageUp", 40)).toBe(0);
    expect(stepIndex(35, "PageDown", 40)).toBe(39);
  });

  it("leaves every other key to the browser", () => {
    expect(stepIndex(5, "a", 10)).toBeUndefined();
    expect(stepIndex(5, "Tab", 10)).toBeUndefined();
  });

  it("has nowhere to go in an empty series", () => {
    expect(stepIndex(null, "Home", 0)).toBeUndefined();
  });

  it("stays put in a one-session series", () => {
    expect(stepIndex(null, "ArrowLeft", 1)).toBe(0);
    expect(stepIndex(0, "ArrowRight", 1)).toBe(0);
  });
});
