import { act, render, renderHook, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CurrentSecurityProvider } from "./CurrentSecurityProvider";
import { useCurrentSecurity } from "./currentSecurity";
import type { Instrument } from "../types/instrument";

const fpt: Instrument = {
  instrumentId: "id-fpt",
  ticker: "FPT",
  name: "FPT Corporation",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

const vnm: Instrument = {
  instrumentId: "id-vnm",
  ticker: "VNM",
  name: "Vietnam Dairy Products Joint Stock Company",
  assetType: "Equity",
  exchange: "HOSE",
  currency: "VND",
  status: "Listed",
};

function renderCurrentSecurity() {
  return renderHook(() => useCurrentSecurity(), {
    wrapper: ({ children }) => (
      <CurrentSecurityProvider>{children}</CurrentSecurityProvider>
    ),
  });
}

describe("CurrentSecurityContext", () => {
  it("starts with no security selected", () => {
    const { result } = renderCurrentSecurity();

    expect(result.current.security).toBeNull();
  });

  it("holds the security that was selected", () => {
    // Scenario E: selecting a security changes the centralised context.
    const { result } = renderCurrentSecurity();

    act(() => result.current.select(fpt));

    expect(result.current.security).toEqual(fpt);
  });

  it("keeps the canonical identifier, not just the ticker", () => {
    // Every module downstream joins on the identifier. A ticker changes on an
    // exchange transfer and can be reassigned after a delisting, so a module
    // holding one would eventually describe the wrong company.
    const { result } = renderCurrentSecurity();

    act(() => result.current.select(fpt));

    expect(result.current.security?.instrumentId).toBe("id-fpt");
  });

  it("leaves nothing of the previous security behind on a change", () => {
    // Scenario F: after switching from FPT to VNM, no part of FPT remains
    // readable through the context.
    const { result } = renderCurrentSecurity();

    act(() => result.current.select(fpt));
    act(() => result.current.select(vnm));

    expect(result.current.security).toEqual(vnm);
    expect(result.current.security?.instrumentId).not.toBe(fpt.instrumentId);
    expect(result.current.security?.ticker).not.toBe(fpt.ticker);
  });

  it("clears back to no security", () => {
    const { result } = renderCurrentSecurity();

    act(() => result.current.select(fpt));
    act(() => result.current.clear());

    expect(result.current.security).toBeNull();
  });

  it("gives every consumer the same value", () => {
    // The point of holding this centrally: two panels must never disagree
    // about what the terminal is looking at.
    function Reader({ label }: { readonly label: string }) {
      const { security } = useCurrentSecurity();
      return <span data-testid={label}>{security?.ticker ?? "none"}</span>;
    }

    function Selector() {
      const { select } = useCurrentSecurity();
      return (
        <button type="button" onClick={() => select(vnm)}>
          Select
        </button>
      );
    }

    render(
      <CurrentSecurityProvider>
        <Reader label="first" />
        <Reader label="second" />
        <Selector />
      </CurrentSecurityProvider>,
    );

    act(() => screen.getByRole("button", { name: "Select" }).click());

    expect(screen.getByTestId("first")).toHaveTextContent("VNM");
    expect(screen.getByTestId("second")).toHaveTextContent("VNM");
  });

  it("fails loudly when used outside the provider", () => {
    // A consumer mounted in the wrong place would otherwise render an empty
    // panel instead of failing, and that is a bug nobody finds.
    expect(() => renderHook(() => useCurrentSecurity())).toThrow(
      /CurrentSecurityProvider/u,
    );
  });
});
