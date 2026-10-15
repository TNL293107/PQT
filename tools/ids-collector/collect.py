"""Collect financial-statement filings from the SSC disclosure portal (IDS).

The portal (congbothongtin.ssc.gov.vn) is an Oracle ADF application with no
API, so this drives it in a browser, as a person would, and writes each
filing page as one JSON document for the backend's IDS provider to read
(ADR-029; the two-stage shape is ADR-015's).

It writes what the page shows and parses nothing that matters: values stay
cell text and column headings stay words. Deciding what a column means is
the importer's job, done once and tested.

Usage:
    python collect.py --ticker FPT VCB --from-year 2022 --to-year 2026 \\
        --out ../../data/fundamentals/ids

Filings already written are skipped, so a run resumes where the last one
stopped. Pages are read one at a time with a pause between them: this is a
public service, read at a person's pace.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
import unicodedata
from dataclasses import dataclass
from datetime import date, datetime, timezone
from pathlib import Path

from playwright.sync_api import Error as PlaywrightError
from playwright.sync_api import Page, sync_playwright

SEARCH_URL = "https://congbothongtin.ssc.gov.vn/faces/NewsSearch"
SCHEMA = "pqt.ids-filing/1"
PAGE_SIZE = 15

# The search form's field ids, as the portal renders them.
TICKER_FIELD = "pt9:it8112::content"
FROM_FIELD = "pt9:id1::content"
TO_FIELD = "pt9:id2::content"

STATEMENT_TAB = re.compile(r"^(BC|KQ|LC)[A-Z\-]*$")
LINE_CODE = re.compile(r"^\.?\d+[a-z]?$", re.IGNORECASE)


@dataclass(frozen=True)
class Listing:
    """One row of the search results."""

    venue: str
    ticker: str
    report_name: str
    company: str
    summary: str
    submitted_on: date

    @property
    def reference(self) -> str:
        digest = hashlib.sha1(
            f"{self.report_name}|{self.summary}|{self.submitted_on.isoformat()}".encode("utf-8")
        ).hexdigest()[:12]
        return f"{self.ticker}-{self.submitted_on:%Y%m%d}-{digest}"


def nfc(text: str) -> str:
    """Composes Vietnamese text. The portal serves some words decomposed, which no
    pattern written in composed form would match."""
    return unicodedata.normalize("NFC", text)


def set_field(page: Page, field_id: str, value: str) -> None:
    page.evaluate(
        """([id, value]) => {
            const el = document.getElementById(id);
            el.focus();
            el.value = value;
            el.dispatchEvent(new Event('input', {bubbles: true}));
            el.dispatchEvent(new Event('change', {bubbles: true}));
            el.blur();
        }""",
        [field_id, value],
    )


RETRY_WAITS = (30, 60, 120, 300)


def search(page: Page, ticker: str, start: date, end: date, pause: float) -> tuple[int, list[Listing]]:
    """Runs one search, retrying through a dropped connection or a slow page."""
    for wait in (*RETRY_WAITS, None):
        try:
            return search_once(page, ticker, start, end, pause)
        except PlaywrightError as error:
            if wait is None:
                raise
            print(f"retrying in {wait}s after: {str(error).splitlines()[0]}", file=sys.stderr)
            time.sleep(wait)

    raise AssertionError("unreachable")


def search_once(page: Page, ticker: str, start: date, end: date, pause: float) -> tuple[int, list[Listing]]:
    """Runs one search and returns the total the portal reports and the rows shown."""
    page.goto(SEARCH_URL, wait_until="networkidle")
    page.wait_for_selector(f"[id='{TICKER_FIELD}']")
    set_field(page, TICKER_FIELD, ticker)
    set_field(page, FROM_FIELD, f"{start:%d/%m/%Y}")
    set_field(page, TO_FIELD, f"{end:%d/%m/%Y}")
    page.get_by_text("Tìm kiếm", exact=True).first.click()
    page.wait_for_load_state("networkidle")
    time.sleep(pause)

    total_text = page.locator("body").inner_text()
    match = re.search(r"Tổng số tin:\s*(\d+)", total_text)
    total = int(match.group(1)) if match else 0

    rows = page.evaluate(
        """(ticker) => [...document.querySelectorAll('tr')]
            .map(tr => [...tr.querySelectorAll('td')].map(td => td.innerText.trim()).filter(Boolean))
            .filter(cells => cells.length >= 7 && /^\\d+$/.test(cells[0]) && cells[2] === ticker)""",
        ticker,
    )

    listings = [
        Listing(
            venue=cells[1],
            ticker=cells[2],
            report_name=nfc(cells[3]),
            company=nfc(cells[4]),
            summary=nfc(cells[5]),
            submitted_on=datetime.strptime(cells[6], "%d/%m/%Y").date(),
        )
        for cells in rows
    ]
    return total, listings


def list_filings(page: Page, ticker: str, start: date, end: date, pause: float) -> list[Listing]:
    """Lists every filing in a range, halving it until each search fits one page.

    Halving the range instead of paging keeps away from the portal's ADF
    pager, whose state does not survive a filing page being opened.
    """
    total, listings = search(page, ticker, start, end, pause)

    if total <= PAGE_SIZE:
        return listings

    if start == end:
        raise RuntimeError(f"{ticker} has {total} filings on {start}; more than one page in a single day.")

    middle = date.fromordinal((start.toordinal() + end.toordinal()) // 2)
    return list_filings(page, ticker, start, middle, pause) + list_filings(
        page, ticker, date.fromordinal(middle.toordinal() + 1), end, pause
    )


def listed_in(page: Page, listing: Listing, start: date, end: date, pause: float) -> bool:
    """Whether a search over a range returns this filing.

    A search shows one page. When the range holds more filings than that,
    a filing missing from the page may be on the next, so the range is
    halved until each half fits.
    """
    total, rows = search(page, listing.ticker, start, end, pause)

    if any(row.report_name == listing.report_name and row.summary == listing.summary for row in rows):
        return True

    if total <= PAGE_SIZE or start == end:
        return False

    middle = date.fromordinal((start.toordinal() + end.toordinal()) // 2)
    return listed_in(page, listing, start, middle, pause) or listed_in(
        page, listing, date.fromordinal(middle.toordinal() + 1), end, pause
    )


def indexed_on(page: Page, listing: Listing, pause: float) -> date:
    """Finds the date the portal's search files a filing under.

    The listing shows when a filing was sent, but the search filters on
    another date, later for some filings and earlier for others: ACB's
    separate FY2023 statements show 06/03/2024 and are found only on
    11/03/2024. The values read now are the filing as it stands at the
    later date, so it is recorded, and the importer dates the values by the
    later of the two. Either side of the sent date is searched.
    """
    if listed_in(page, listing, listing.submitted_on, listing.submitted_on, pause):
        return listing.submitted_on

    low = date.fromordinal(listing.submitted_on.toordinal() - 400)
    high = min(date.fromordinal(listing.submitted_on.toordinal() + 400), date.today())

    if not listed_in(page, listing, low, high, pause):
        raise RuntimeError(f"{listing.reference}: no search within 400 days of {listing.submitted_on} returns it.")

    while low < high:
        middle = date.fromordinal((low.toordinal() + high.toordinal()) // 2)

        if listed_in(page, listing, low, middle, pause):
            high = middle
        else:
            low = date.fromordinal(middle.toordinal() + 1)

    return low


def read_filing(page: Page, listing: Listing, pause: float) -> dict:
    """Opens a filing from its search and reads its form and statement tabs."""
    indexed = indexed_on(page, listing, pause)

    # Found on its sent date, the page already shows that day's results.
    if indexed != listing.submitted_on:
        search(page, listing.ticker, indexed, indexed, pause)

    opened = page.evaluate(
        """([name, summary]) => {
            const row = [...document.querySelectorAll('tr')].find(tr => {
                const cells = [...tr.querySelectorAll('td')].map(td => td.innerText.trim().normalize('NFC'));
                return cells.includes(name) && cells.includes(summary);
            });
            const link = row && [...row.querySelectorAll('a')].find(a => a.textContent.trim().length > 5);
            if (!link) return false;
            link.click();
            return true;
        }""",
        [listing.report_name, listing.summary],
    )

    if not opened:
        raise RuntimeError(f"{listing.reference}: the filing could not be found again to open it.")

    page.wait_for_load_state("networkidle")
    page.wait_for_selector("text=Chi tiết dữ liệu báo cáo", timeout=30_000)
    time.sleep(pause)

    form = page.evaluate(
        """() => {
            const text = document.body.innerText;
            const block = (text.match(/Chi tiết tin công bố[\\s\\S]*?Chi tiết dữ liệu báo cáo/) || [''])[0];
            return block;
        }"""
    )

    form = nfc(form)
    year = re.search(r"Năm tài chính\s*\*?\s*(\d{4})", form)
    quarter = re.search(r"Quý\s*\*?\s*([1-4])\b", form)
    document = re.search(r"(\S+\.pdf)", form, re.IGNORECASE)

    if not year:
        raise RuntimeError(f"{listing.reference}: the form states no fiscal year.")

    tabs = page.evaluate(
        """(pattern) => [...new Set([...document.querySelectorAll('a,div,span')]
            .filter(e => e.children.length === 0 && new RegExp(pattern).test(e.textContent.trim()) && e.offsetParent)
            .map(e => e.textContent.trim()))]""",
        STATEMENT_TAB.pattern,
    )

    tables = []
    previous = None

    for index, tab in enumerate(tabs):
        page.evaluate(
            """(name) => [...document.querySelectorAll('a,div,span')]
                .find(e => e.children.length === 0 && e.textContent.trim() === name && e.offsetParent).click()""",
            tab,
        )
        page.wait_for_load_state("networkidle")
        time.sleep(pause)

        # The first tab is the one shown on arrival; every later one must
        # replace it. On a slow portal the click returns before the new tab
        # is drawn, and reading then captured one statement under another's
        # name. So wait until the table differs from the last one and holds
        # still across two reads.
        table = read_table(page)
        deadline = time.monotonic() + 45

        while True:
            settled = read_table(page)
            changed = index == 0 or signature(settled) != previous
            if changed and signature(settled) == signature(table) and settled["rows"]:
                break
            if time.monotonic() > deadline:
                raise RuntimeError(f"{listing.reference}: tab {tab} never replaced the one before it.")
            table = settled
            time.sleep(1)

        previous = signature(table)

        if table["rows"]:
            tables.append({"name": tab, **table})

    signatures = [signature(table) for table in tables]

    if len(set(signatures)) != len(signatures):
        raise RuntimeError(f"{listing.reference}: two tabs read the same table.")

    return {
        "schema": SCHEMA,
        "source": "IDS",
        "reference": listing.reference,
        "ticker": listing.ticker,
        "venue": listing.venue,
        "reportName": listing.report_name,
        "summary": listing.summary,
        "submittedOn": listing.submitted_on.isoformat(),
        "indexedOn": indexed.isoformat(),
        "submittedAtUtc": None,
        "fiscalYear": int(year.group(1)),
        "fiscalQuarter": int(quarter.group(1)) if quarter else None,
        "document": document.group(1) if document else None,
        "fetchedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "tables": tables,
    }


def signature(table: dict) -> str:
    """What a table holds: its headings and every row's code and cells."""
    return json.dumps([table["columns"], [[row["code"], row["values"]] for row in table["rows"]]], ensure_ascii=False)


def read_table(page: Page) -> dict:
    """Reads the statement table currently drawn: its value headings and rows."""
    return page.evaluate(
            """(codePattern) => {
                const code = new RegExp(codePattern, 'i');
                const headers = [...new Set([...document.querySelectorAll('th,[role=columnheader]')]
                    .filter(e => e.offsetParent)
                    .map(e => e.innerText.replace(/\\s+/g, ' ').trim().normalize('NFC'))
                    .filter(Boolean))];
                const columns = headers.slice(3);
                const rows = [...document.querySelectorAll('tr')]
                    .map(tr => [...tr.querySelectorAll('td')].map(td => td.innerText.trim()))
                    .filter(cells => cells.length >= 4 && code.test(cells[1] || ''))
                    .map(cells => ({
                        label: cells[0].replace(/\\s+/g, ' ').normalize('NFC'),
                        code: cells[1],
                        values: cells.slice(3, 3 + columns.length),
                    }));
                return {columns, rows};
            }""",
        LINE_CODE.pattern,
    )


def is_financial_statement(listing: Listing) -> bool:
    """Keeps the statements themselves, not the letters that explain them."""
    return listing.report_name.lower().startswith(("báo cáo tài chính", "bctc")) or any(
        word in listing.report_name.lower() for word in ("hợp nhất", "riêng", "mẹ")
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ticker", nargs="+", required=True)
    parser.add_argument("--from-year", type=int, required=True)
    parser.add_argument("--to-year", type=int, default=date.today().year)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--pause", type=float, default=1.5, help="seconds between page reads")
    parser.add_argument("--channel", default="msedge", help="installed browser to drive (msedge, chrome)")
    parser.add_argument("--headed", action="store_true", help="show the browser")
    args = parser.parse_args()

    written = skipped = failed = 0

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(channel=args.channel, headless=not args.headed)
        page = browser.new_page(viewport={"width": 1400, "height": 900}, locale="vi-VN")

        for ticker in (t.strip().upper() for t in args.ticker):
            folder = args.out / ticker
            folder.mkdir(parents=True, exist_ok=True)

            for year in range(args.from_year, args.to_year + 1):
                try:
                    listings = list_filings(page, ticker, date(year, 1, 1), date(year, 12, 31), args.pause)
                except Exception as error:  # noqa: BLE001 - one year must not stop the run
                    failed += 1
                    print(f"FAILED {ticker} {year} listing: {str(error).splitlines()[0]}", file=sys.stderr)
                    continue

                for listing in listings:
                    if not is_financial_statement(listing):
                        continue

                    target = folder / f"{listing.reference}.json"

                    if target.exists():
                        skipped += 1
                        continue

                    try:
                        filing = read_filing(page, listing, args.pause)
                    except Exception as error:  # noqa: BLE001 - one filing must not stop the run
                        failed += 1
                        print(f"FAILED {listing.reference}: {error}", file=sys.stderr)
                        continue

                    # Written beside the target and renamed, so a run stopped
                    # mid-write never leaves a truncated document for the
                    # importer, or one the next run would skip as held.
                    partial = target.with_suffix(".json.partial")
                    partial.write_text(json.dumps(filing, ensure_ascii=False, indent=2), encoding="utf-8")
                    partial.replace(target)
                    written += 1
                    print(f"wrote {target.name} ({len(filing['tables'])} tab(s))")

        browser.close()

    print(f"{written} written, {skipped} already held, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
