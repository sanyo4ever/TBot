# TBot 3.4.6b stability scope

The user selected console operation and algorithm stability after an audit of Ronnie, Raphael and Whatsupp. The existing authorization covers our fork branches, reviewed PR merges, publication and side-by-side local preparation; installed clients must not be started or stopped. Public author repositories remain read-only.

Keep our released 3.4.6a as the base: TBot b1d961564378a28201ffcfc34d4f00e3d46b9ae9 and Go 52d5284fe88e7409e27f1344326ca326b42462de. Compare donor releases, not stale main branches: Ronnie 521f43f3/e9f365f8, Raphael d3bd6dd0/aec70899, Whatsupp 78d188c4 (vendored Go).

## Approach

Use narrow behavior fixes and regressions on our current architecture. A full donor branch replacement would discard our strict target/galaxy/recall validation, Techs/highscore/LF compatibility, Admiral and profile fixes. A UI/updater transplant is out of scope by the user's explicit console preference; Config UI also has separate restricted terms. Large farm/harvest schedulers, ledger, bulk discovery and PoW/login work remain independent future changes.

## Selected behavior

- Espionage reports with explicit zero counts remain eligible only when complete fleet/defence information proves them defenceless. Positive or invalid negative counts and missing information remain rejected; preserve probe-specific satellite behavior.
- Brain shipment capacity uses the actual sending origin's cargo bonuses. Returned celestial snapshots are chained rather than discarded; lifeform limits are initialized for each celestial. Keep construction ordering and defaults.
- Single discovery uses its freshly loaded page token, validates mission eligibility and sends at most one state-changing POST. Reconcile a normal internal redirect only against a genuinely new matching fleet; reject external/login redirects and ambiguous/rejected responses. C# must not replay the discovery POST. Do not import bulk-system discovery or claim universal dispatch idempotency.
- Repair focused settings lock publication and guaranteed lock release on failure; retain existing file format and watcher behavior. Ctrl+C completes shutdown signalling once and permits graceful disposal. Do not introduce forced semaphore unlocks, worker overlap, pause gates or atomic-file/watch semantics as an incidental change.

No new production dependencies, SDK downgrade or ImageSharp major bump. Go donor code retains MIT attribution; independently implement small C# behavior fixes in our existing code and record idea sources. No Config UI code/assets/templates, author license keys or private configuration enter source or artifacts.

## Evidence and release

Tests use synthetic complete fixtures, fake clocks/bridges and local temporary files only. Prove the original failure before fixing each selected behavior, then run the complete relevant suites. Include malformed/negative/unknown data, distinct sender/destination bonuses, object replacement, competing settings access, repeat cancellation and accepted-discovery/ambiguous-response cases. No login, fleet send, game/Telegram network or secret access.

Independently review both patches, publish Go PR against our main first, merge only after CI, then pin its merge SHA in TBot PR. Version author 3.4.6 plus revision b; the next manual release workflow must pass all tests and seven packages. Validate public ZIP/hash/source provenance and prepare a new 3.4.6b folder from the currently used local configuration, leaving existing processes untouched.
