# Census boundaries: terms

`nc-boundaries.json` is derived by `tools/build_locators.py` from the U.S. Census Bureau's cartographic boundary
files, which are generalized from the TIGER/Line database. They are works of the United States Government and are not
subject to copyright in the United States (17 U.S.C. § 105), so they may be used, changed and redistributed without
permission. The Census Bureau asks for credit, which every locator map gives in its caption: "Boundaries: U.S. Census
Bureau, cartographic boundary files (year)."

The file's own `source` block names the vintage, the scale and the SHA-256 of each Census file it was built from.
