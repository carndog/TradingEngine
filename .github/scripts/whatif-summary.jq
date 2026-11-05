def redacted_path($path):
  if ($path // "") | test("EToro__MarketData__|PriceCollection__ExchangeSessions__")
  then "(private app setting path redacted)"
  else ($path // "?")
  end;

def delta_rows:
  . as $d
  | ($d.children // []) as $children
  | if ($children | length) > 0 then
      $children[] | delta_rows
    else
      "| `\(redacted_path($d.path))` | \($d.propertyChangeType // $d.changeType // "Unknown") |"
    end;

(.changes // [])[] |
  "##### `\(.resourceId)`",
  "",
  "| Property path | Change |",
  "| --- | --- |",
  ((.delta // []) | if length == 0 then
    "| — | No property-level delta reported |"
  else
    (.[] | delta_rows)
  end),
  ""
