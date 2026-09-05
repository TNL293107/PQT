import { describe, expect, it } from "vitest";
import { completeEntry, parseEntry, readCount } from "./entry";

describe("parseEntry", () => {
  it("reads a bare verb", () => {
    const entry = parseEntry("help");

    expect(entry.verb).toBe("HELP");
    expect(entry.functionCode).toBeNull();
    expect(entry.securityQuery).toBe("");
  });

  it("gives find the rest of the line as its query", () => {
    expect(parseEntry("find hoa phat").securityQuery).toBe("hoa phat");
  });

  it("splits a security from the function that follows it", () => {
    const entry = parseEntry("FPT GP");

    expect(entry.securityQuery).toBe("FPT");
    expect(entry.functionCode).toBe("GP");
  });

  it("reads a security on its own", () => {
    const entry = parseEntry("fpt");

    expect(entry.securityQuery).toBe("fpt");
    expect(entry.functionCode).toBeNull();
    expect(entry.verb).toBeNull();
  });

  it("does not mistake a company name's last word for a function", () => {
    // The reason the mnemonics are an allow-list rather than a shape test. A
    // rule like "one to four letters is a function" splits Hoa Phat into a
    // security HOA and a function PHAT, and short words are exactly what
    // Vietnamese company names are made of.
    const entry = parseEntry("Hoa Phat");

    expect(entry.securityQuery).toBe("Hoa Phat");
    expect(entry.functionCode).toBeNull();
  });

  it("keeps a multi-word security in front of its function", () => {
    const entry = parseEntry("Hoa Phat GP");

    expect(entry.securityQuery).toBe("Hoa Phat");
    expect(entry.functionCode).toBe("GP");
  });

  it("reads an option with a value", () => {
    const entry = parseEntry("FPT GP --limit 40");

    expect(entry.securityQuery).toBe("FPT");
    expect(entry.functionCode).toBe("GP");
    expect(entry.options["limit"]).toBe("40");
  });

  it("reads an option with no value as a flag", () => {
    expect(parseEntry("FPT GP --raw").options).toEqual({ raw: null });
  });

  it("reads a flag that is followed by another option", () => {
    const entry = parseEntry("FPT GP --raw --limit 5");

    expect(entry.options["raw"]).toBeNull();
    expect(entry.options["limit"]).toBe("5");
  });

  it("strips options before splitting the security, whatever their position", () => {
    // Reading them in the other order makes 'Hoa Phat --limit 5' ambiguous
    // about where the name ends.
    const entry = parseEntry("Hoa Phat --limit 5 GP");

    expect(entry.securityQuery).toBe("Hoa Phat");
    expect(entry.functionCode).toBe("GP");
  });

  it("keeps the raw line so the transcript can echo what was typed", () => {
    expect(parseEntry("  FPT   GP  ").raw).toBe("FPT   GP");
  });

  it("reads an empty line as nothing", () => {
    const entry = parseEntry("   ");

    expect(entry.raw).toBe("");
    expect(entry.verb).toBeNull();
    expect(entry.securityQuery).toBe("");
  });
});

describe("readCount", () => {
  it("reads a positive whole number", () => {
    expect(readCount({ limit: "40" }, "limit")).toBe(40);
  });

  it.each(["0", "-3", "all", ""])("refuses %s", (value) => {
    expect(readCount({ limit: value }, "limit")).toBeNull();
  });

  it("is null when the option is absent", () => {
    expect(readCount({}, "limit")).toBeNull();
  });
});

describe("completeEntry", () => {
  it("completes an unambiguous mnemonic", () => {
    expect(completeEntry("FPT QL")).toBe("FPT QLTY");
  });

  it("completes a verb in the first position", () => {
    expect(completeEntry("hea")).toBe("HEALTH");
  });

  it("leaves an ambiguous prefix alone", () => {
    // H matches both HELP and HEALTH, and guessing between them would move the
    // caret somewhere the user did not ask for.
    expect(completeEntry("h")).toBeNull();
  });

  it("does not complete a security", () => {
    // The console holds no symbol table, and offering one from the transcript
    // would suggest tickers this deployment may not hold.
    expect(completeEntry("FP")).toBeNull();
  });

  it("does nothing after a trailing space", () => {
    expect(completeEntry("FPT ")).toBeNull();
  });

  it("does nothing on an empty line", () => {
    expect(completeEntry("")).toBeNull();
  });
});
