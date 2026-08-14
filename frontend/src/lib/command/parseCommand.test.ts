import { describe, expect, it } from "vitest";
import { parseCommand } from "./parseCommand";

describe("parseCommand", () => {
  it("treats a lone token as the security", () => {
    expect(parseCommand("FPT")).toEqual({
      securityQuery: "FPT",
      functionCode: null,
    });
  });

  it("splits a recognised function off the end", () => {
    expect(parseCommand("FPT GP")).toEqual({
      securityQuery: "FPT",
      functionCode: "GP",
    });
  });

  it("upper-cases the function mnemonic", () => {
    expect(parseCommand("fpt news").functionCode).toBe("NEWS");
  });

  it("keeps a multi-word company name whole", () => {
    // The reason function mnemonics are an allow-list. A shape rule such as
    // "a short trailing word is a function" would split this into a security
    // of "Vietnam Dairy" and a function of "PRODUCTS".
    expect(parseCommand("Vietnam Dairy Products")).toEqual({
      securityQuery: "Vietnam Dairy Products",
      functionCode: null,
    });
  });

  it("does not mistake a short Vietnamese name fragment for a function", () => {
    // "Hoa Phat", "Ngan Hang", "Viet Nam" — short words are exactly what
    // Vietnamese company names are made of.
    expect(parseCommand("Hoa Phat")).toEqual({
      securityQuery: "Hoa Phat",
      functionCode: null,
    });
  });

  it("collapses stray whitespace", () => {
    expect(parseCommand("   FPT    GP  ")).toEqual({
      securityQuery: "FPT",
      functionCode: "GP",
    });
  });

  it("returns an empty security for blank input", () => {
    expect(parseCommand("   ")).toEqual({
      securityQuery: "",
      functionCode: null,
    });
  });

  it("does not treat a lone mnemonic as a function", () => {
    // Typing "GP" on its own is a search for a security called GP, not a
    // function with no subject.
    expect(parseCommand("GP")).toEqual({
      securityQuery: "GP",
      functionCode: null,
    });
  });
});
